using HidSharp;
using System.Runtime.InteropServices;
using WindowsTouchBar.Events;

namespace WindowsTouchBar.Interop;

/// <summary>
/// Keeps the separate T2 Touch Bar backlight awake for lyrics and touch activity.
/// Protocol facts: Linux hid-appletb-bl.c, FF12:0020 auxiliary=1,
/// FF12:0021 state=1 full/on, 3 dim, 4 off. This is not the keyboard backlight.
/// </summary>
internal sealed class TouchBarBacklightService : IDisposable
{
    private const int ReportLength = 116;
    private const uint CollectionUsage = 0xff120001;
    internal const int TouchAwakeMilliseconds = 30_000;
    internal const int LyricsRefreshMilliseconds = 250;
    private const uint DesktopIdleWatchMilliseconds = 20_000;
    private long nextLyricsRefresh;
    private readonly Action<string> log;
    private readonly object stateLock = new();
    private readonly object ioLock = new();
    private readonly Func<IBacklightConnection> openConnection;
    private readonly Func<long> milliseconds;
    private readonly Func<uint?> lastDesktopInput;
    private readonly Action<int> retryDelay;
    private readonly AutoResetEvent changed = new(false);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task? worker;
    private bool visible;
    private bool disposed;
    private bool touchHeld;
    private bool wakeOnlyTouch;
    private long touchAwakeUntil;
    private IBacklightConnection? stream;
    private byte? originalMode;
    private uint? originalDesktopInput;
    private bool reportedOn;
    private string? lastError;

    public TouchBarBacklightService(Action<string>? log = null)
        : this(OpenConnection,
            () => Environment.TickCount64, ReadLastDesktopInput, log, true, Thread.Sleep)
    {
    }

    // Tests use the production policy and report handling with a fake connection/clock.
    internal TouchBarBacklightService(Func<IBacklightConnection> openConnection,
        Func<long> milliseconds, Func<uint?> lastDesktopInput, Action<string>? log = null,
        bool startWorker = false, Action<int>? retryDelay = null)
    {
        this.openConnection = openConnection;
        this.milliseconds = milliseconds;
        this.lastDesktopInput = lastDesktopInput;
        this.retryDelay = retryDelay ?? Thread.Sleep;
        this.log = log ?? ControlDiagnostics.Write;
        if (startWorker) worker = Task.Run(Run);
    }

    public void SetLyricsVisible(bool value)
    {
        lock (stateLock)
        {
            if (disposed || visible == value) return;
            visible = value;
            changed.Set();
        }
    }

    // Called before the owner's view/render lock: feature-report retries must not
    // stall animation. A gesture that began with the backlight off only wakes it.
    public bool HandleTouch(TouchEventType type)
    {
        if (type != TouchEventType.TouchDown)
        {
            lock (stateLock)
            {
                if (disposed) return false;
                bool consume = wakeOnlyTouch;
                if (touchHeld)
                {
                    touchAwakeUntil = milliseconds() + TouchAwakeMilliseconds;
                    if (type == TouchEventType.TouchUp) touchHeld = false;
                    changed.Set();
                }
                if (type == TouchEventType.TouchUp) wakeOnlyTouch = false;
                return consume;
            }
        }

        lock (ioLock)
        {
            lock (stateLock)
            {
                if (disposed) return false;
                touchHeld = true;
                touchAwakeUntil = milliseconds() + TouchAwakeMilliseconds;
                wakeOnlyTouch = false;
                changed.Set();
            }
            bool wasOff = false;
            try
            {
                EnsureConnection();
                var report = ReadStableReport(stream!, retryDelay);
                wasOff = report[2] == 4;
                AssertFullOn(report);
                if (wasOff)
                    log("Touch Bar touch wake: off -> full/on; first gesture only wakes the display.");
            }
            catch (Exception error) { ReportError(error); }
            lock (stateLock) wakeOnlyTouch = wasOff;
            return wasOff;
        }
    }

    // One worker iteration, also used by deterministic tests with a virtual clock.
    internal void Poll()
    {
        lock (ioLock)
        {
            bool awake, lyrics;
            lock (stateLock)
            {
                lyrics = !disposed && visible;
                awake = !disposed && (lyrics || touchHeld || milliseconds() < touchAwakeUntil);
            }
            if (!lyrics) nextLyricsRefresh = 0;
            if (!awake) { ReleaseSession(); return; }
            try
            {
                // A successful GET of mode 1 does not renew the panel's idle timer.
                // Renew while lyrics own the display, even when the report is still on.
                bool renew = lyrics && milliseconds() >= nextLyricsRefresh;
                EnsureConnection(allowTransition: lyrics);
                if (AssertFullOn(renew: renew) && lyrics)
                    nextLyricsRefresh = milliseconds() + LyricsRefreshMilliseconds;
            }
            catch (Exception error)
            {
                // Retain the original state through a transient feature-report error.
                ReportError(error);
            }
        }
    }

    private void Run()
    {
        var waits = new[] { changed, cancellation.Token.WaitHandle };
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                Poll();
                bool lyrics;
                lock (stateLock) lyrics = visible;
                int interval = lyrics && Volatile.Read(ref lastError) is null ? LyricsPollInterval() : 1000;
                WaitHandle.WaitAny(waits, interval);
            }
        }
        finally { lock (ioLock) ReleaseSession(); }
    }

    // Boot Camp can send a separate idle-off command even after a successful
    // renewal. Watch for that fade at display-frame cadence once the desktop idles.
    internal int LyricsPollInterval()
    {
        uint? last = lastDesktopInput();
        uint idle = unchecked((uint)milliseconds() - last.GetValueOrDefault());
        return !last.HasValue || idle >= DesktopIdleWatchMilliseconds ? 16 : 250;
    }

    private void EnsureConnection(bool allowTransition = false)
    {
        if (stream is null)
        {
            stream = openConnection();
            try
            {
                byte mode = (allowTransition ? ReadReport(stream) : ReadStableReport(stream, retryDelay))[2];
                // An in-flight fade has no known stable mode to restore. Lyrics can
                // take over immediately and hand control back to desktop idle on exit.
                originalMode = IsTransitionMode(mode) ? null : mode;
                originalDesktopInput = lastDesktopInput();
            }
            catch { stream.Dispose(); stream = null; throw; }
        }
    }

    private bool AssertFullOn(byte[]? current = null, bool renew = false)
    {
        EnsureConnection();
        current ??= ReadReport(stream!);
        bool write = renew || RequiresFullOn(current[2]);
        if (write)
        {
            bool confirmed = false;
            for (int writeAttempt = 0; writeAttempt < 3 && !confirmed; writeAttempt++)
            {
                var request = (byte[])current.Clone();
                request[0] = 1;
                request[1] = 1;
                request[2] = 1;
                stream!.SetFeature(request);

                for (int readAttempt = 0; readAttempt < 8; readAttempt++)
                {
                    current = ReadReport(stream!);
                    if (current[2] == 1)
                    {
                        confirmed = true;
                        break;
                    }
                    if (!IsTransitionMode(current[2])) break;
                    retryDelay(35);
                }
            }
            if (!confirmed)
                throw new IOException($"Touch Bar backlight did not confirm full/on after transition retries (mode={current[2]}).");
        }
        if (!reportedOn)
        {
            log($"Touch Bar backlight: full/on confirmed; original mode={originalMode}; keep-awake active for lyrics or touch.");
            reportedOn = true;
        }
        lastError = null;
        return write;
    }

    private void ReleaseSession()
    {
        if (stream is null) return;
        try
        {
            // A real mouse/keyboard action may have legitimately woken the bar
            // during our lease. Never overwrite that with an old saved off state.
            bool desktopUnchanged = originalDesktopInput.HasValue &&
                lastDesktopInput() == originalDesktopInput;
            if ((originalMode is 3 or 4) && desktopUnchanged)
            {
                var report = ReadStableReport(stream, retryDelay);
                report[0] = 1;
                report[1] = 1;
                report[2] = originalMode.Value;
                stream.SetFeature(report);
                byte actual = ReadStableReport(stream, retryDelay)[2];
                if (actual != originalMode) throw new IOException($"Touch Bar backlight restore was not confirmed (expected={originalMode}, actual={actual}).");
                log($"Touch Bar backlight: restored original mode={originalMode}; keep-awake released.");
            }
            else log("Touch Bar backlight: keep-awake released; current mode retained for desktop idle policy.");
        }
        catch (Exception error) { ReportError(error); }
        finally
        {
            stream.Dispose(); stream = null;
            originalMode = null;
            originalDesktopInput = null;
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

    private static byte[] ReadReport(IBacklightConnection opened)
    {
        var report = new byte[ReportLength];
        report[0] = 1;
        opened.GetFeature(report);
        if (report[0] != 1 || !IsSupportedMode(report[2]))
            throw new IOException($"Unexpected Touch Bar backlight response (report={report[0]}, mode={report[2]}).");
        return report;
    }

    // The controlled probe uses the same validated endpoint and packet builder.
    // This returns the reported full/dim/off state, not a measured luminance.
    internal static int ReadCurrentMode()
    {
        using var opened = OpenConnection();
        return ReadStableReport(opened, Thread.Sleep)[2];
    }

    internal static IBacklightConnection OpenConnection() => new HidBacklightConnection(FindDevice().Open());

    internal static bool IsSupportedMode(byte mode) => mode is 1 or 2 or 3 or 4;
    internal static bool IsTransitionMode(byte mode) => mode == 2;
    internal static bool RequiresFullOn(byte mode) => mode != 1;

    private static byte[] ReadStableReport(IBacklightConnection opened, Action<int> delay)
    {
        byte[] report = ReadReport(opened);
        for (int attempt = 0; IsTransitionMode(report[2]) && attempt < 8; attempt++)
        {
            delay(35);
            report = ReadReport(opened);
        }
        if (IsTransitionMode(report[2]))
            throw new IOException("Touch Bar backlight remained in transition after bounded retries.");
        return report;
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            if (disposed) return;
            disposed = true;
            cancellation.Cancel();
            changed.Set();
        }
        // Owner must call this outside its render lock.
        worker?.GetAwaiter().GetResult();
        lock (ioLock) ReleaseSession();
        changed.Dispose();
        cancellation.Dispose();
    }

    private static uint? ReadLastDesktopInput()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.Time : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    internal interface IBacklightConnection : IDisposable
    {
        void GetFeature(byte[] report);
        void SetFeature(byte[] report);
    }

    private sealed class HidBacklightConnection : IBacklightConnection
    {
        private readonly HidStream opened;
        public HidBacklightConnection(HidStream opened)
        {
            this.opened = opened;
            opened.ReadTimeout = 1000;
            opened.WriteTimeout = 1000;
        }
        public void GetFeature(byte[] report) => opened.GetFeature(report);
        public void SetFeature(byte[] report) => opened.SetFeature(report);
        public void Dispose() => opened.Dispose();
    }
}
