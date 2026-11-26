namespace WindowsTouchBar.Gaming;

internal sealed record TelemetrySnapshot(double? CpuTemperature = null, double? CpuLoad = null, double? CpuClockMHz = null,
    double? GpuTemperature = null, double? GpuLoad = null, double? GpuClockMHz = null, double? Fps = null, double? FrameTimeMs = null)
{
    public static readonly TelemetrySnapshot Empty = new();
}
