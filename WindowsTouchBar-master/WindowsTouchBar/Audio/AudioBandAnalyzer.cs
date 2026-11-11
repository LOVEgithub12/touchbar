namespace WindowsTouchBar.Audio;

/// <summary>Independent channel filters avoid cancellation of out-of-phase stereo music.</summary>
internal sealed class AudioBandAnalyzer
{
    private readonly int sampleRate;
    private readonly Channel[] channels;
    private readonly double[] energy = new double[4];
    private readonly float[] smooth = new float[4];
    private int frames;

    public AudioBandAnalyzer(int sampleRate, int channelCount)
    {
        this.sampleRate = sampleRate;
        channels = Enumerable.Range(0, channelCount).Select(_ => new Channel(sampleRate)).ToArray();
    }

    public void Add(float sample, int channel)
    {
        if (!float.IsFinite(sample)) sample = 0;
        sample = Math.Clamp(sample, -1f, 1f);
        Channel state = channels[channel];
        double bass = state.Low.Process(sample);
        double mid = state.MidLow.Process(state.MidHigh.Process(sample));
        double treble = state.High.Process(sample);
        energy[0] += bass * bass;
        energy[1] += mid * mid;
        energy[2] += treble * treble;
        energy[3] += sample * sample;
        if (channel == channels.Length - 1) frames++;
    }

    public (float Bass, float Mid, float Treble, float Level) ReadAndReset()
    {
        if (frames == 0) return (smooth[0], smooth[1], smooth[2], smooth[3]);
        double elapsed = frames / (double)sampleRate;
        double divisor = frames * (double)channels.Length;
        for (int i = 0; i < 4; i++)
        {
            double rms = Math.Sqrt(energy[i] / divisor);
            double gain = i == 2 ? 8 : i == 0 ? 5 : 4;
            float target = rms < .00005 ? 0 : (float)Math.Clamp(Math.Sqrt(rms * gain), 0, 1);
            double tau = target > smooth[i] ? .015 : .06;
            smooth[i] += (target - smooth[i]) * (float)(1 - Math.Exp(-elapsed / tau));
            energy[i] = 0;
        }
        frames = 0;
        return (smooth[0], smooth[1], smooth[2], smooth[3]);
    }

    private sealed class Channel
    {
        public readonly Biquad Low, MidHigh, MidLow, High;
        public Channel(int sampleRate)
        {
            Low = new Biquad(sampleRate, 220, false);
            MidHigh = new Biquad(sampleRate, 220, true);
            MidLow = new Biquad(sampleRate, 2500, false);
            High = new Biquad(sampleRate, 2500, true);
        }
    }

    // Butterworth low/high pass filters using the RBJ audio EQ cookbook coefficients.
    private sealed class Biquad
    {
        private readonly double b0, b1, b2, a1, a2;
        private double z1, z2;
        public Biquad(int sampleRate, double frequency, bool highPass)
        {
            double omega = 2 * Math.PI * Math.Min(frequency, sampleRate * .45) / sampleRate;
            double cosine = Math.Cos(omega), alpha = Math.Sin(omega) / Math.Sqrt(2);
            double a0 = 1 + alpha;
            double k = highPass ? 1 + cosine : 1 - cosine;
            b0 = k / 2 / a0;
            b1 = (highPass ? -k : k) / a0;
            b2 = b0;
            a1 = -2 * cosine / a0;
            a2 = (1 - alpha) / a0;
        }

        public double Process(double sample)
        {
            double result = b0 * sample + z1;
            z1 = b1 * sample - a1 * result + z2;
            z2 = b2 * sample - a2 * result;
            return result;
        }
    }
}
