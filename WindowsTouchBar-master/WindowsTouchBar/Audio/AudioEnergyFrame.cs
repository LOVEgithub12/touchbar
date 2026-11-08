namespace WindowsTouchBar.Audio;

/// <summary>Only derived amplitudes are retained; loopback PCM is never saved.</summary>
internal sealed record AudioEnergyFrame(
    float Bass, float Mid, float Treble, float Level, bool IsCapturing,
    string DeviceName, long SampleFrames, double CapturedAtSeconds)
{
    public static readonly AudioEnergyFrame Empty = new(0, 0, 0, 0, false, "", 0, 0);
}
