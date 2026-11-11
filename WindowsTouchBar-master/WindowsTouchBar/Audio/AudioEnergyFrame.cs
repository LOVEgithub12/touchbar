namespace WindowsTouchBar.Audio;

/// <summary>Only derived amplitudes are retained; loopback PCM is never saved.</summary>
internal sealed record AudioEnergyFrame(
    float Bass, float Mid, float Treble, float Level, bool IsCapturing,
    string DeviceName, long SampleFrames, double CapturedAtSeconds)
{
    public ReadOnlyMemory<float> Spectrum { get; init; }
    public float SpectrumScale { get; init; } = 1f;

    internal AudioEnergyFrame FadeForAge(double ageSeconds)
    {
        if (!double.IsFinite(ageSeconds) || ageSeconds < 0) return Empty;
        if (ageSeconds <= 0.18) return this;

        float fade = (float)Math.Exp(-(ageSeconds - 0.18) / 0.3);
        return this with
        {
            Bass = Bass * fade,
            Mid = Mid * fade,
            Treble = Treble * fade,
            Level = Level * fade,
            SpectrumScale = SpectrumScale * fade
        };
    }

    public static readonly AudioEnergyFrame Empty = new(0, 0, 0, 0, false, "", 0, 0);
}
