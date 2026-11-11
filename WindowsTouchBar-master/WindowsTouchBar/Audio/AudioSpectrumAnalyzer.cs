namespace WindowsTouchBar.Audio;

// Long bass window for resolution, short mid/treble window for transients.
// Combine channel power so opposite stereo phases cannot erase a tone.
internal sealed class AudioSpectrumAnalyzer
{
    public const int BinCount = 512;
    private const int Size = 4096;
    private readonly float[][] ring;
    private readonly Window bass = new(Size), transient = new(Size / 2);
    private readonly int hop;
    private readonly Band[] bands = new Band[BinCount];
    private int cursor, untilTransform = Size;
    public ReadOnlyMemory<float> Latest { get; private set; }

    public AudioSpectrumAnalyzer(int sampleRate, int channels)
    {
        ring = Enumerable.Range(0, channels).Select(_ => new float[Size]).ToArray();
        hop = Math.Max(1, (int)Math.Round(sampleRate / 60.0));
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
    }

    public void Add(float sample, int channel)
    {
        ring[channel][cursor] = float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0f;
        if (channel != ring.Length - 1) return;
        cursor = (cursor + 1) & (Size - 1);
        if (--untilTransform > 0) return;
        untilTransform = hop;
        Transform();
    }

    private void Transform()
    {
        bass.Measure(ring, cursor);
        transient.Measure(ring, cursor);
        var result = new float[BinCount];
        for (int i = 0; i < result.Length; i++)
        {
            var band = bands[i];
            float low = band.Mix < 1 ? Height(bass.Read(band.Long), band.Gain) : 0;
            float high = band.Mix > 0 ? Height(transient.Read(band.Short), band.Gain) : 0;
            result[i] = low + (high - low) * band.Mix;
        }
        Latest = result;
    }

    private static float Height(float power, float gain)
    {
        float db = 10 * MathF.Log10(Math.Max(power, 1e-12f)) + gain;
        return MathF.Pow(Math.Clamp((db + 66) / 55, 0, 1), 1.57f);
    }

    private readonly record struct Band(Bin Long, Bin Short, float Mix, float Gain);
    private readonly record struct Bin(int Center, float Fraction, int From, int To)
    {
        public Bin(float hz, float lo, float hi, float scale) : this(
            (int)(hz * scale), hz * scale % 1, (int)MathF.Ceiling(lo * scale), (int)(hi * scale)) { }
    }

    private sealed class Window
    {
        private readonly int size;
        private readonly float[] power, real, imaginary, window, cosine, sine;
        private readonly int[] reversed;
        private float calibration;

        public Window(int size)
        {
            this.size = size;
            power = new float[size / 2 + 1];
            real = new float[size];
            imaginary = new float[size];
            window = new float[size];
            cosine = new float[size / 2];
            sine = new float[size / 2];
            reversed = new int[size];
            for (int i = 0; i < size; i++)
            {
                window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / size);
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
            calibration = 16f / (size * size * ring.Length);
            Array.Clear(power);
            int startAt = cursor + Size - size;
            foreach (float[] channel in ring)
            {
                Array.Clear(imaginary);
                for (int i = 0; i < size; i++)
                    real[reversed[i]] = channel[(startAt + i) & (Size - 1)] * window[i];
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
