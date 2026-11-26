using WindowsTouchBar.Events;
using WindowsTouchBar.Interop;

internal static class TouchWakeChecks
{
    public static void OffGesture()
    {
        using var rig = new Rig(4);
        Require(rig.Service.HandleTouch(TouchEventType.TouchDown), "Off-state down must only wake.");
        Require(rig.Device.Mode == 1, "Touch did not wake to full/on.");
        Require(rig.Device.Writes.Count == 1, "Wake must use one confirmed write.");
        var request = rig.Device.Writes[0];
        Require(request[0] == 1 && request[1] == 1 && request[2] == 1,
            "Wrong report ID, auxiliary byte, or full/on mode.");
        Require(request.Skip(3).SequenceEqual(rig.Device.Initial.Skip(3)), "Wake modified unrelated report bytes.");
        Require(rig.Service.HandleTouch(TouchEventType.TouchMove), "Waking move must not reach controls.");
        Require(rig.Service.HandleTouch(TouchEventType.TouchUp), "Waking release must not reach controls.");
        Require(!rig.Service.HandleTouch(TouchEventType.TouchDown), "Second, visible tap should operate controls.");
        Require(!rig.Service.HandleTouch(TouchEventType.TouchUp), "Second tap release should operate controls.");
    }

    public static void LitGesture()
    {
        foreach (byte mode in new byte[] { 1, 3 })
        {
            using var rig = new Rig(mode);
            Require(!rig.Service.HandleTouch(TouchEventType.TouchDown), "Lit/dim controls should accept first tap.");
            Require(rig.Device.Mode == 1, "Dim touch did not restore full/on.");
            Require(!rig.Service.HandleTouch(TouchEventType.TouchUp), "Lit release was swallowed.");
        }
    }

    public static void HeldAndRenewed()
    {
        using var rig = new Rig(4);
        rig.Service.HandleTouch(TouchEventType.TouchDown);
        rig.Now += 90_000; // Stationary holds do not generate TouchMove reports.
        rig.Service.Poll();
        Require(rig.Device.Mode == 1 && !rig.Device.Closed, "Stationary held finger lost its wake lease.");
        rig.Service.HandleTouch(TouchEventType.TouchUp);
        rig.Now += TouchBarBacklightService.TouchAwakeMilliseconds - 1;
        rig.Service.Poll();
        Require(rig.Device.Mode == 1, "Lease expired before 30 seconds after release.");
        rig.Service.HandleTouch(TouchEventType.TouchDown);
        rig.Now += 50;
        rig.Service.HandleTouch(TouchEventType.TouchUp);
        rig.Now += TouchBarBacklightService.TouchAwakeMilliseconds - 1;
        rig.Service.Poll();
        Require(rig.Device.Mode == 1, "Later touch did not renew the lease.");
        rig.Now++;
        rig.Service.Poll();
        Require(rig.Device.Mode == 4 && rig.Device.Closed, "Expired touch lease did not restore original off state.");
    }

    public static void LyricsOverlap()
    {
        using var rig = new Rig(4);
        rig.Service.SetLyricsVisible(true);
        rig.Service.Poll();
        rig.Service.HandleTouch(TouchEventType.TouchDown);
        rig.Service.HandleTouch(TouchEventType.TouchUp);
        rig.Service.SetLyricsVisible(false);
        rig.Now += 29_999;
        rig.Service.Poll();
        Require(rig.Device.Mode == 1, "Leaving lyrics erased the active touch lease.");
        rig.Service.SetLyricsVisible(true);
        rig.Now += 90_000;
        rig.Service.Poll();
        Require(rig.Device.Mode == 1, "Expired touch lease turned visible lyrics off.");
        rig.Service.SetLyricsVisible(false);
        rig.Service.Poll();
        Require(rig.Device.Mode == 4, "Inactive lyrics and expired lease did not release the backlight.");
    }

    public static void ContinuousLyrics()
    {
        using var rig = new Rig(1);
        rig.Service.SetLyricsVisible(true);
        long lastWrite = rig.Now;
        for (int poll = 0; poll < 2400; poll++, rig.Now += 250)
        {
            // The emulated panel would blank if GETs alone were used to keep it on.
            Require(rig.Now - lastWrite < 3000, "Visible lyrics missed a keep-awake renewal.");
            int count = rig.Device.Writes.Count;
            rig.Service.Poll();
            if (count != rig.Device.Writes.Count) lastWrite = rig.Now;
        }
        Require(rig.Device.Writes.Count >= 2390, "Expected 250 ms renewals over ten minutes.");
        rig.Service.SetLyricsVisible(false);
        rig.Service.Poll();
        int writesAfterRelease = rig.Device.Writes.Count;
        rig.Now += 10_000;
        rig.Service.Poll();
        Require(rig.Device.Closed && rig.Device.Writes.Count == writesAfterRelease,
            "Lyrics renewals continued after release.");

        // GetLastInputInfo has a 32-bit tick count, including on long-running hosts.
        rig.Now = (1L << 32) + 123;
        rig.DesktopInput = unchecked((uint)(rig.Now - 19_999));
        Require(rig.Service.LyricsPollInterval() == 250, "Active desktop used the fast watch.");
        rig.Now++;
        Require(rig.Service.LyricsPollInterval() == 16, "Desktop idle watch missed a tick wrap.");

        using var fading = new Rig(2);
        fading.Service.SetLyricsVisible(true);
        fading.Service.Poll();
        Require(fading.Device.Mode == 1 && fading.Errors.Count == 0, "An in-flight fade delayed lyric wake.");
        fading.Service.SetLyricsVisible(false);
        fading.Service.Poll();
        Require(fading.Device.Closed && fading.Device.Writes.Count == 1, "Restored an unknown transition state.");
    }

    public static void DesktopActivity()
    {
        foreach (uint? updatedInput in new uint?[] { 456, null })
        {
            using var rig = new Rig(4);
            rig.Service.HandleTouch(TouchEventType.TouchDown);
            rig.Service.HandleTouch(TouchEventType.TouchUp);
            rig.DesktopInput = updatedInput;
            rig.Now += TouchBarBacklightService.TouchAwakeMilliseconds;
            rig.Service.Poll();
            Require(rig.Device.Mode == 1 && rig.Device.Closed,
                "Restored a stale off state after desktop input changed or became unavailable.");
        }
    }

    public static void TransitionAndFailure()
    {
        using (var rig = new Rig(4))
        {
            rig.Device.TransitionReads = 3;
            Require(rig.Service.HandleTouch(TouchEventType.TouchDown), "Transition retry lost off-state suppression.");
            Require(rig.Device.Mode == 1 && rig.Delays == 3, "Full/on transition was not retried and confirmed.");
            rig.Service.HandleTouch(TouchEventType.TouchUp);
        }
        using (var rig = new Rig(4))
        {
            rig.Device.FailWrite = true;
            Require(rig.Service.HandleTouch(TouchEventType.TouchDown), "Failed off-state wake allowed a blind button press.");
            Require(rig.Errors.Count == 1, "Backlight failure should be logged without terminating input.");
            Require(rig.Service.HandleTouch(TouchEventType.TouchUp), "Failed wake released a blind gesture into controls.");
            rig.Device.FailWrite = false;
            rig.Service.Poll();
            Require(rig.Device.Mode == 1, "A transient write failure did not recover on the worker.");
        }
    }

    public static void RenderAndDisposal()
    {
        using var rig = new Rig(4);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Device.BeforeRead = () =>
        {
            entered.Set();
            if (!release.Wait(3000)) throw new TimeoutException("Test feature read was not released.");
        };
        var touch = Task.Run(() => rig.Service.HandleTouch(TouchEventType.TouchDown));
        try
        {
            Require(entered.Wait(1500), "Touch did not enter the fake HID read.");
            var render = Task.Run(() => rig.Service.SetLyricsVisible(true));
            Require(render.Wait(500), "Lyrics/render policy was blocked by feature-report I/O.");
        }
        finally { release.Set(); }
        Require(touch.Wait(1500) && touch.Result, "Touch wake did not finish after feature I/O resumed.");
        rig.Service.Dispose();
        int writes = rig.Device.Writes.Count;
        rig.Service.SetLyricsVisible(true);
        rig.Service.HandleTouch(TouchEventType.TouchDown);
        rig.Service.Poll();
        Require(rig.Device.Closed && rig.Device.Writes.Count == writes,
            "A late callback reopened/wrote the backlight after disposal.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Rig : IDisposable
    {
        public long Now = 1000;
        public uint? DesktopInput = 123;
        public int Delays;
        public readonly FakeConnection Device;
        public readonly TouchBarBacklightService Service;
        public readonly List<string> Errors = new();
        public Rig(byte mode)
        {
            Device = new FakeConnection(mode);
            Service = new TouchBarBacklightService(() => Device, () => Now, () => DesktopInput,
                line => { if (line.Contains("unavailable")) Errors.Add(line); },
                retryDelay: _ => Delays++);
        }
        public void Dispose() => Service.Dispose();
    }

    private sealed class FakeConnection : TouchBarBacklightService.IBacklightConnection
    {
        public readonly byte[] Initial = Enumerable.Range(0, 116).Select(i => (byte)(i + 17)).ToArray();
        public readonly List<byte[]> Writes = new();
        public byte Mode;
        public bool Closed;
        public bool FailWrite;
        public int TransitionReads;
        public Action? BeforeRead;
        private int pendingTransitionReads;
        public FakeConnection(byte mode) { Mode = mode; Initial[0] = 1; Initial[1] = 5; Initial[2] = mode; }
        public void GetFeature(byte[] report)
        {
            BeforeRead?.Invoke();
            if (Closed) throw new ObjectDisposedException(nameof(FakeConnection));
            Initial.CopyTo(report, 0);
            report[2] = pendingTransitionReads-- > 0 ? (byte)2 : Mode;
        }
        public void SetFeature(byte[] report)
        {
            if (Closed) throw new ObjectDisposedException(nameof(FakeConnection));
            if (FailWrite) throw new IOException("Injected feature write failure.");
            Writes.Add((byte[])report.Clone());
            Mode = report[2];
            pendingTransitionReads = TransitionReads;
        }
        public void Dispose() => Closed = true;
    }
}
