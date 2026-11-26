using WindowsTouchBar.Interop;

internal static class BrightnessOsdChecks
{
    public static void ColdAndWarm()
    {
        var shell = new Shell();
        Require(shell.Show(), "First brightness tap must create and show the native flyout.");
        Require(shell.Created == 1 && shell.Visible && shell.DispatcherQueries == 1, "Cold tap must use the shell creation endpoint.");
        shell.Visible = false;
        Require(shell.Show() && shell.Visible, "Second tap must show the existing flyout.");
        Require(shell.Created == 1 && shell.DispatcherQueries == 1, "Warm taps must not use the unsubscribed creation endpoint.");
    }

    public static void RestartAndRace()
    {
        var shell = new Shell();
        Require(shell.Show(), "Initial display failed.");
        var old = shell.Host;
        shell.Host = IntPtr.Zero;
        shell.NextHost = new IntPtr(303);
        Require(shell.Show() && shell.Host != old && shell.Visible, "Explorer restart must resolve a new native HWND.");
        shell = new Shell { CreateDuringLookup = true };
        Require(shell.Show() && shell.Visible, "A concurrent volume key may create the flyout and unsubscribe the controller during lookup.");
    }

    public static void FailureIsolation()
    {
        var shell = new Shell { DispatcherAvailable = false };
        Require(!shell.Show() && shell.Notifications == 0, "Missing shell must not send to an invalid HWND.");
        shell = new Shell { SendSucceeds = false };
        Require(!shell.Show() && shell.Notifications == 1, "A hung shell must stop the route without repeated messages.");
        int messages = 0;
        bool result = NativeBrightnessOsd.ShowCore(() => new[] { new IntPtr(1), new IntPtr(2) },
            () => throw new Exception("Must not initialize with ambiguous hosts"), _ => { messages++; return true; }, _ => { });
        Require(!result && messages == 0, "Ambiguous hosts must not receive a broadcast.");
        result = NativeBrightnessOsd.ShowCore(() => Array.Empty<IntPtr>(),
            () => throw new System.Runtime.InteropServices.COMException("Explorer exited"), _ => true, _ => { });
        Require(!result, "COM failure must be contained so hardware adjustment can continue.");
        result = NativeBrightnessOsd.ShowCore(() => Array.Empty<IntPtr>(),
            () => new IntPtr(10), _ => true, _ => { });
        Require(!result, "A sent message alone must not count as successful flyout creation.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    // Models Explorer's actual handoff: the controller stops responding after it
    // creates a host; each later display must reach the host directly.
    private sealed class Shell
    {
        public IntPtr Host;
        public IntPtr NextHost = new(202);
        public bool Visible, CreateDuringLookup;
        public bool DispatcherAvailable = true, SendSucceeds = true;
        public int Created, DispatcherQueries, Notifications;
        public bool Show() => NativeBrightnessOsd.ShowCore(
            () => Host == IntPtr.Zero ? Array.Empty<IntPtr>() : new[] { Host },
            () =>
            {
                DispatcherQueries++;
                if (CreateDuringLookup) Create();
                return DispatcherAvailable ? new IntPtr(101) : IntPtr.Zero;
            },
            target =>
            {
                Notifications++;
                if (!SendSucceeds) return false;
                if (target == new IntPtr(101))
                {
                    if (Host == IntPtr.Zero) { Create(); Visible = true; }
                    return true;
                }
                if (target != Host) throw new InvalidOperationException("A stale/unverified HWND was used.");
                Visible = true;
                return true;
            }, _ => { });
        private void Create() { Host = NextHost; Created++; }
    }
}
