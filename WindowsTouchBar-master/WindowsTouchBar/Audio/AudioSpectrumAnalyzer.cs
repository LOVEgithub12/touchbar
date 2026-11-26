namespace WindowsTouchBar.Audio;

// A 128 ms bass window is zero-padded to 8192 points at 48 kHz. Shorter
// mid/treble windows retain transients; the default analyzer is unchanged.
// Combine channel power so opposite stereo phases cannot erase a tone.
internal sealed class AudioSpectrumAnalyzer
{
    public const int BinCount = 512;
    public const int ReferenceBinCount = 1024;
    private const int Size = 4096;
    private readonly float[][] ring;
    private readonly Window bass = new(Size), transient = new(Size / 2);
    private readonly Window? detailedBass;
    private readonly SpectrumResponse? referenceResponse;
    private readonly Bin[] detailedBands = new Bin[ReferenceBinCount];
    private readonly Band[] referenceBands = new Band[ReferenceBinCount];
    private readonly float[][] referenceDb = Enumerable.Range(0, 3).Select(_ => new float[ReferenceBinCount]).ToArray();
    private readonly int hop;
    private readonly float transformSeconds;
    private float loudness;
    private double inputPower;
    private bool inputActive;
    private float inputAttenuationDb;
    private bool levelInitialized;
    private double inputSquareSum;
    private int inputSampleCount;
    private readonly Band[] bands = new Band[BinCount];
    private int cursor, untilTransform = Size;
    public ReadOnlyMemory<float> Latest { get; private set; }

    public AudioSpectrumAnalyzer(int sampleRate, int channels, bool referenceSpectrum = false)
    {
        ring = Enumerable.Range(0, channels).Select(_ => new float[referenceSpectrum ? Size * 2 : Size]).ToArray();
        hop = Math.Max(1, (int)Math.Round(sampleRate / 60.0));
        transformSeconds = hop / (float)sampleRate;
        if (referenceSpectrum)
        {
            detailedBass = new Window(Size * 2, Size * 3 / 2);
            referenceResponse = new SpectrumResponse();
            untilTransform = hop;
        }
        float limit = Math.Min(16500, sampleRate * 0.47f);
        float frequency(float x) => 32 * MathF.Pow(limit / 32, x);
        for (int i = 0; i < BinCount; i++)
        {
            float hz = frequency((i + 0.5f) / BinCount);
            float lo = frequency(i / (float)BinCount), hi = frequency((i + 1f) / BinCount);
            float mix = Math.Clamp(MathF.Log2(hz / 220) / MathF.Log2(470f / 220), 0, 1);
            bands[i] = new Band(new Bin(hz, lo, hi, Size / (float)sampleRate),
                new Bin(hz, lo, hi, (Size / 2f) / sampleRate), mix,
                1.4f + Math.Clamp(MathF.Log2(hz / 180) * 2.2f, 0, 13));
        }
        if (referenceSpectrum)
        for (int i = 0; i < ReferenceBinCount; i++)
        {
            float hz = referenceResponse!.FrequencyAt(i, limit);
            // Sample the calibrated frequency directly in FFT power space.
            // Resampling a logarithmic dB grid a second time widens fine tips.
            float lo = hz, hi = hz;
            referenceBands[i] = new Band(new Bin(hz, lo, hi, Size / (float)sampleRate),
                new Bin(hz, lo, hi, (Size / 2f) / sampleRate), 0, 0);
            detailedBands[i] = new Bin(hz, lo, hi, (Size * 2f) / sampleRate);
        }
    }

    public void Add(float sample, int channel)
    {
        // Shared-mode floating-point audio can legitimately exceed full scale.
        // Keep its shape for the reference spectrum; clipping here invents
        // harmonics before the FFT. Reject only non-finite/corrupt magnitudes.
        float value = float.IsFinite(sample) && MathF.Abs(sample) <= 1e6f ? sample : 0;
        ring[channel][cursor] = referenceResponse == null ? Math.Clamp(value, -1f, 1f) : value;
        if (referenceResponse != null)
        {
            inputSquareSum += (double)value * value;
            inputSampleCount++;
        }
        if (channel != ring.Length - 1) return;
        cursor = (cursor + 1) & (ring[0].Length - 1);
        if (--untilTransform > 0) return;
        untilTransform = hop;
        Transform();
    }

    private void Transform()
    {
        float inputOffset = referenceResponse == null ? 0 : UpdateInputLevel();
        bass.Measure(ring, cursor);
        transient.Measure(ring, cursor);
        detailedBass?.Measure(ring, cursor);
        var result = new float[BinCount];
        for (int i = 0; i < result.Length; i++)
        {
            var band = bands[i];
            float low = band.Mix < 1 ? Height(bass.Read(band.Long), band.Gain + inputOffset) : 0;
            float high = band.Mix > 0 ? Height(transient.Read(band.Short), band.Gain + inputOffset) : 0;
            result[i] = low + (high - low) * band.Mix;
        }
        if (detailedBass != null)
            for (int i = 0; i < ReferenceBinCount; i++)
            {
                referenceDb[0][i] = Decibels(detailedBass.Read(detailedBands[i])) + inputOffset;
                referenceDb[1][i] = Decibels(bass.Read(referenceBands[i].Long)) + inputOffset;
                referenceDb[2][i] = Decibels(transient.Read(referenceBands[i].Short)) + inputOffset;
            }

        // Loopback PCM is captured after the endpoint volume. Compensate only
        // the slow overall level: raising the level dependence to 0.70 removes
        // 30% of system-volume influence while preserving beat peaks and flux.
        float framePeak = 0;
        for (int i = 0; i < result.Length; i++) framePeak = Math.Max(framePeak, result[i]);
        if (referenceResponse != null)
            result = referenceResponse.Read(referenceDb, transformSeconds, inputOffset, (float)Math.Sqrt(inputPower), inputActive);
        if (referenceResponse == null && framePeak > .012f)
        {
            float follow = 1 - MathF.Exp(-transformSeconds / 2.5f);
            loudness = loudness <= 0 ? framePeak : loudness + (framePeak - loudness) * follow;
            float gain = Math.Clamp(MathF.Pow(.48f / Math.Max(.03f, loudness), .30f), .72f, 1.50f);
            if (MathF.Abs(gain - 1) > .002f)
                for (int i = 0; i < result.Length; i++)
                    result[i] = referenceResponse == null ? Math.Clamp(result[i] * gain, 0, 1) : Math.Max(0, result[i] * gain);
        }
        Latest = result;
    }

    private float UpdateInputLevel()
    {
        double power = inputSquareSum / Math.Max(1, inputSampleCount);
        inputActive = power >= 1e-8;
        inputSquareSum = 0;
        inputSampleCount = 0;
        // Never turn a silent/noisy gap up. Hold sensitivity through silence,
        // so a pause cannot gradually inflate the next note or background hiss.
        if (power < 1e-8) return inputAttenuationDb;
        if (!levelInitialized)
        {
            inputPower = power;
            levelInitialized = true;
        }
        else
        {
            double follow = 1 - Math.Exp(-transformSeconds / .4);
            inputPower += (power - inputPower) * follow;
        }
        // A single common dB offset BEFORE the nonlinear visual response keeps
        // all frequency ratios intact. Only attenuate sustained loud material;
        // quiet passages retain their original sensitivity and beat contrast.
        float target = (float)Math.Clamp(10 * Math.Log10(.12 * .12 / Math.Max(inputPower, 1e-12)), -36, 0);
        float seconds = target < inputAttenuationDb ? .18f : 3f;
        inputAttenuationDb += (target - inputAttenuationDb) * (1 - MathF.Exp(-transformSeconds / seconds));
        return inputAttenuationDb;
    }

    private static float Height(float power, float gain)
    {
        float db = 10 * MathF.Log10(Math.Max(power, 1e-12f)) + gain;
        return MathF.Pow(Math.Clamp((db + 66) / 55, 0, 1), 1.57f);
    }

    private static float Decibels(float power) => 10 * MathF.Log10(Math.Max(power, 1e-12f));

    private readonly record struct Band(Bin Long, Bin Short, float Mix, float Gain);
    private readonly record struct Bin(int Center, float Fraction, int From, int To)
    {
        public Bin(float hz, float lo, float hi, float scale) : this(
            (int)(hz * scale), hz * scale % 1, (int)MathF.Ceiling(lo * scale), (int)(hi * scale)) { }
    }

    private sealed class Window
    {
        private readonly int size, windowLength;
        private readonly float[] power, real, imaginary, window, cosine, sine;
        private readonly int[] reversed;
        private float calibration;

        public Window(int size, int windowLength = 0)
        {
            this.size = size;
            this.windowLength = windowLength == 0 ? size : windowLength;
            power = new float[size / 2 + 1];
            real = new float[size];
            imaginary = new float[size];
            window = new float[size];
            cosine = new float[size / 2];
            sine = new float[size / 2];
            reversed = new int[size];
            for (int i = 0; i < size; i++)
            {
                int position = i - (size - this.windowLength);
                window[i] = position < 0 ? 0 : 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * position / this.windowLength);
                reversed[i] = (reversed[i >> 1] >> 1) | ((i & 1) * (size / 2));
            }
            for (int i = 0; i < size / 2; i++)
            {
                cosine[i] = MathF.Cos(-2 * MathF.PI * i / size);
                sine[i] = MathF.Sin(-2 * MathF.PI * i / size);
            }
        }

        public void Measure(float[][] ring, int cursor)
        {
            calibration = 16f / (windowLength * windowLength * ring.Length);
            Array.Clear(power);
            int bufferSize = ring[0].Length;
            int startAt = cursor + bufferSize - size;
            foreach (float[] channel in ring)
            {
                Array.Clear(imaginary);
                for (int i = 0; i < size; i++)
                    real[reversed[i]] = channel[(startAt + i) & (bufferSize - 1)] * window[i];
                for (int length = 2; length <= size; length *= 2)
                {
                    int half = length / 2, stride = size / length;
                    for (int start = 0; start < size; start += length)
                    for (int j = 0; j < half; j++)
                    {
                        int a = start + j, b = a + half, k = j * stride;
                        float r = real[b] * cosine[k] - imaginary[b] * sine[k];
                        float im = real[b] * sine[k] + imaginary[b] * cosine[k];
                        real[b] = real[a] - r;
                        imaginary[b] = imaginary[a] - im;
                        real[a] += r;
                        imaginary[a] += im;
                    }
                }
                for (int i = 1; i < power.Length; i++)
                    power[i] += real[i] * real[i] + imaginary[i] * imaginary[i];
            }
        }

        public float Read(Bin bin)
        {
            int k = bin.Center;
            float p = power[k] + (power[k + 1] - power[k]) * bin.Fraction;
            for (int j = bin.From; j <= bin.To; j++) p = Math.Max(p, power[j]);
            return p * calibration;
        }
    }
}
