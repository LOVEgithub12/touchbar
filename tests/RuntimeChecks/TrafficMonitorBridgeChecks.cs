using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using WindowsTouchBar.Gaming;

internal static class TrafficMonitorBridgeChecks
{
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    public static void Run()
    {
        string name = "Local\\TouchBar.Telemetry.Tests." + Guid.NewGuid();
        using var map = MemoryMappedFile.CreateNew(name, 128);
        using var data = map.CreateViewAccessor();
        using var reader = new TrafficMonitorTelemetryReader(name);
        void Valid() {
            data.Write(0, 2); data.Write(4, 0x5442544du); data.Write(8, 2u); data.Write(12, 128u);
            data.Write(16, Stopwatch.GetTimestamp()); data.Write(24, Stopwatch.Frequency); data.Write(36, 63u);
            data.Write(40, 72.25); data.Write(48, 0.0); data.Write(56, 3125.0); data.Write(112, 2u);
            data.Write(64, 65.5); data.Write(72, 0.0); data.Write(80, 1250.5); data.Write(116, 1u);
        }
        Valid(); var live = reader.Read();
        Check(live == new TelemetrySnapshot(72.25, 0, 3125, 65.5, 0, 1250.5), "CPU/GPU packet or idle load lost");
        data.Write(16, Stopwatch.GetTimestamp() - 4 * Stopwatch.Frequency);
        Check(reader.Read() == TelemetrySnapshot.Empty, "Stale CPU/GPU retained");
        Valid(); data.Write(0, 3); Check(reader.Read() == TelemetrySnapshot.Empty, "Partial packet accepted");
        Valid(); data.Write(8, 1u); Check(reader.Read() == TelemetrySnapshot.Empty, "Old CPU-only version accepted");
        Valid(); data.Write(12, 96u); Check(reader.Read() == TelemetrySnapshot.Empty, "Wrong packet size accepted");
        Valid(); data.Write(16, Stopwatch.GetTimestamp() + Stopwatch.Frequency); Check(reader.Read() == TelemetrySnapshot.Empty, "Future sample accepted");
        Valid(); data.Write(24, 1L); Check(reader.Read() == TelemetrySnapshot.Empty, "Wrong clock frequency accepted");
        Valid(); data.Write(40, double.NaN); data.Write(48, 101.0); data.Write(56, -1.0);
        data.Write(64, double.PositiveInfinity); data.Write(72, -1.0); data.Write(80, 20001.0);
        Check(reader.Read() == TelemetrySnapshot.Empty, "Invalid sensor values accepted");
        Valid(); data.Write(112, 0u); Check(reader.Read().CpuTemperature == null && reader.Read().GpuTemperature == 65.5, "Unverified CPU source accepted or GPU lost");
        Valid(); data.Write(116, 0u); Check(reader.Read().GpuTemperature == null && reader.Read().CpuTemperature == 72.25, "Unverified GPU source accepted or CPU lost");
        Valid(); data.Write(80, 0.0); Check(reader.Read().GpuClockMHz == 0, "Native zero GPU clock rejected");
        Valid(); data.Write(36, 31u); Check(reader.Read().GpuClockMHz == null && reader.Read().GpuTemperature == 65.5, "Unavailable clock hid other fields");
        Valid(); data.Write(36, 0u); Check(reader.Read() == TelemetrySnapshot.Empty, "Unloaded producer accepted");
        Valid(); Check(reader.Read() == live, "Producer restart did not recover with retained reader");
        using var absent = new TrafficMonitorTelemetryReader(name + ".absent");
        Check(absent.Read() == TelemetrySnapshot.Empty, "Missing bridge raised fake data");
    }
}
