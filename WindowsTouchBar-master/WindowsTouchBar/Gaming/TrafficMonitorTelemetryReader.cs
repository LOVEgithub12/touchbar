using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace WindowsTouchBar.Gaming;

// One coherent CPU/GPU packet. PresentMon supplies FPS independently.
internal sealed class TrafficMonitorTelemetryReader : IDisposable
{
    internal const string MappingName = @"Local\TouchBar.TrafficMonitor.Telemetry.v2";
    private MemoryMappedFile? mapping;
    private MemoryMappedViewAccessor? view;
    private long retryAt;
    private readonly string name;
    public TrafficMonitorTelemetryReader(string name = MappingName) { this.name = name; }
    public TelemetrySnapshot Read()
    {
        long now = Stopwatch.GetTimestamp();
        try {
            if (view is null) {
                if (now < retryAt) return TelemetrySnapshot.Empty;
                retryAt = now + Stopwatch.Frequency;
                mapping = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
                view = mapping.CreateViewAccessor(0, 128, MemoryMappedFileAccess.Read);
            }
            for (int attempt = 0; attempt < 3; ++attempt) {
                int before = view.ReadInt32(0);
                if ((before & 1) != 0) continue;
                Thread.MemoryBarrier();
                uint magic = view.ReadUInt32(4), version = view.ReadUInt32(8), size = view.ReadUInt32(12);
                long stamp = view.ReadInt64(16), frequency = view.ReadInt64(24);
                uint valid = view.ReadUInt32(36), temperatureSource = view.ReadUInt32(112);
                double temperature = view.ReadDouble(40), load = view.ReadDouble(48), clock = view.ReadDouble(56);
                double gpuTemperature = view.ReadDouble(64), gpuLoad = view.ReadDouble(72), gpuClock = view.ReadDouble(80);
                uint gpuSource = view.ReadUInt32(116);
                Thread.MemoryBarrier();
                if (before != view.ReadInt32(0)) continue;
                now = Stopwatch.GetTimestamp();
                // Reject future, stale, incompatible and torn samples as a whole; never merge another collector.
                if (magic != 0x5442544d || version != 2 || size != 128 || frequency != Stopwatch.Frequency ||
                    stamp <= 0 || now < stamp || (now - stamp) / (double)frequency > 3) return TelemetrySnapshot.Empty;
                return new TelemetrySnapshot(
                    (valid & 1) != 0 && temperatureSource is 1 or 2 && Finite(temperature, 0, 150, false) ? temperature : null,
                    (valid & 2) != 0 && Finite(load, 0, 100, true) ? load : null,
                    (valid & 4) != 0 && Finite(clock, 0, 20000, false) ? clock : null,
                    gpuSource == 1 && (valid & 8) != 0 && Finite(gpuTemperature, 0, 150, false) ? gpuTemperature : null,
                    gpuSource == 1 && (valid & 16) != 0 && Finite(gpuLoad, 0, 100, true) ? gpuLoad : null,
                    gpuSource == 1 && (valid & 32) != 0 && Finite(gpuClock, 0, 20000, true) ? gpuClock : null);
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Dispose(); }
        return TelemetrySnapshot.Empty;
    }
    private static bool Finite(double value, double min, double max, bool includeMin) =>
        double.IsFinite(value) && (includeMin ? value >= min : value > min) && value <= max;
    internal static bool IsConfigured(string? directory = null)
    {
        try { return File.ReadAllText(Path.Combine(directory ?? AppContext.BaseDirectory, "Telemetry", "cpu-source.txt")).Trim()
            .Equals("trafficmonitor", StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
    public void Dispose() { view?.Dispose(); mapping?.Dispose(); view = null; mapping = null; }
}
