using HidSharp;

namespace WindowsTouchBar.Interop;

/// <summary>
/// Keeps only the separate T2 Touch Bar backlight awake while lyrics are visible.
/// Protocol facts: Linux hid-appletb-bl.c, FF12:0020 auxiliary=1,
/// FF12:0021 state=1 full/on, 3 dim, 4 off. This is not the keyboard backlight.
/// </summary>
internal sealed class TouchBarBacklightService : IDisposable
{
    private const int ReportLength = 116;
    private const uint CollectionUsage = 0xff120001;
    private readonly Action<string> log;
    private readonly AutoResetEvent changed = new(false);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private int visible;
    private int disposed;
    private HidStream? stream;
    private byte? originalMode;
    private bool reportedOn;
    private string? lastError;

    public TouchBarBacklightService(Action<string>? log = null)
    {
        this.log = log ?? ControlDiagnostics.Write;
        worker = Task.Run(Run);
    }

    public void SetLyricsVisible(bool value)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        int requested = value ? 1 : 0;
        if (Interlocked.Exchange(ref visible, requested) != requested)
            changed.Set();
    }

    private void Run()
    {
        var waits = new[] { changed, cancellation.Token.WaitHandle };
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (Volatile.Read(ref visible) != 0)
                {
                    try { AssertFullOn(); }
                    catch (Exception error)
                    {
                        ReportError(error);
                        // Keep a successfully opened session so deactivation can
                        // still restore the saved mode after a transient error.
                    }
                }
                else ReleaseSession();
                WaitHandle.WaitAny(waits, 1000);
            }
        }
        finally { ReleaseSession(); }
    }

    private void AssertFullOn()
    {
        if (stream is null)
        {
            var device = FindDevice();
            stream = device.Open();
            stream.ReadTimeout = 1000;
            stream.WriteTimeout = 1000;
            try { originalMode = ReadReport(stream)[2]; }
            catch { stream.Dispose(); stream = null; throw; }
        }

        var current = ReadReport(stream);
        // Preserve every unestablished field, including transition values.
        var request = (byte[])current.Clone();
        request[0] = 1;
        request[1] = 1;
        request[2] = 1;
        stream.SetFeature(request);
        byte actual = ReadReport(stream)[2];
        if (actual != 1) throw new IOException($"Touch Bar backlight did not confirm full/on (mode={actual}).");
        if (!reportedOn)
        {
            log($"Touch Bar backlight: full/on confirmed; original mode={originalMode}; keep-awake active while lyrics are visible.");
            reportedOn = true;
        }
        lastError = null;
    }

    private void ReleaseSession()
    {
        if (stream is null) return;
        try
        {
            if (originalMode is 3 or 4)
            {
                var report = ReadReport(stream);
                report[0] = 1;
                report[1] = 1;
                report[2] = originalMode.Value;
                stream.SetFeature(report);
                byte actual = ReadReport(stream)[2];
                if (actual != originalMode) throw new IOException($"Touch Bar backlight restore was not confirmed (expected={originalMode}, actual={actual}).");
                log($"Touch Bar backlight: restored original mode={originalMode}; keep-awake released.");
            }
            else log("Touch Bar backlight: keep-awake released; original full/on mode retained.");
        }
        catch (Exception error) { ReportError(error); }
        finally
        {
            stream.Dispose(); stream = null;
            originalMode = null;
            reportedOn = false;
        }
    }

    private void ReportError(Exception error)
    {
        string message = error.GetType().Name + ": " + error.Message;
        if (lastError == message) return;
        lastError = message;
        log("Touch Bar backlight unavailable: " + message);
    }

    private static HidDevice FindDevice()
    {
        var supported = new List<HidDevice>();
        foreach (var device in DeviceList.Local.GetHidDevices(0x05ac, 0x8102))
        {
            if (!device.DevicePath.Contains("&mi_00#", StringComparison.OrdinalIgnoreCase)) continue;
            var collection = device.GetReportDescriptor().DeviceItems
                .FirstOrDefault(item => item.Usages.ContainsValue(CollectionUsage));
            var report = collection?.FeatureReports.FirstOrDefault(item => item.ReportID == 1);
            if (report is null || report.Length != ReportLength || device.GetMaxFeatureReportLength() != ReportLength) continue;
            var fields = report.DataItems.ToArray();
            if (fields.Length < 2 || fields[0].ElementBits != 8 || fields[0].ElementCount != 1 ||
                fields[1].ElementBits != 8 || fields[1].ElementCount != 1 ||
                !fields[0].Usages.ContainsValue(0xff120020) || !fields[1].Usages.ContainsValue(0xff120021) ||
                fields[0].LogicalMinimum > 1 || fields[0].LogicalMaximum < 1 ||
                fields[1].LogicalMinimum > 1 || fields[1].LogicalMaximum < 4) continue;
            supported.Add(device);
        }
        if (supported.Count != 1) throw new NotSupportedException($"Expected exactly one T2 Touch Bar backlight collection; found {supported.Count}.");
        return supported[0];
    }

    private static byte[] ReadReport(HidStream opened)
    {
        var report = new byte[ReportLength];
        report[0] = 1;
        opened.GetFeature(report);
        if (report[0] != 1 || report[2] is not (1 or 3 or 4))
            throw new IOException($"Unexpected Touch Bar backlight response (report={report[0]}, mode={report[2]}).");
        return report;
    }

    // The controlled probe uses the same validated endpoint and packet builder.
    // This returns the reported full/dim/off state, not a measured luminance.
    internal static int ReadCurrentMode()
    {
        using var opened = FindDevice().Open();
        return ReadReport(opened)[2];
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel();
        changed.Set();
        // Owner must call this outside its render lock.
        worker.GetAwaiter().GetResult();
        changed.Dispose();
        cancellation.Dispose();
    }
}
