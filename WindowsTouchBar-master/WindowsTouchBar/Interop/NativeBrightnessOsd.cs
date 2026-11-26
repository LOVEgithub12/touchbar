using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsTouchBar.Interop;

/// <summary>Asks the Windows 10 shell to display its own hardware brightness flyout.</summary>
internal static class NativeBrightnessOsd
{
    private const uint AboveLockUxBand = 18;
    private const int BrightnessShellNotification = 55;
    private static readonly object initializationLock = new();
    private static Task<bool>? initialization;

    public static void Show()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            if (version.Major != 10 || version.Build >= 22000)
            {
                ControlDiagnostics.Write("Native brightness indicator: this shell notification is supported only on Windows 10.");
                return;
            }
            var candidates = FindFlyouts();
            if (candidates.Count != 0)
            {
                ShowCore(() => candidates, GetDispatcherWindow, Notify, ControlDiagnostics.Write);
                return;
            }

            // COM activation can wait on an unresponsive Explorer. Keep it off the
            // touch thread and allow at most one outstanding initialization attempt.
            Task<bool> pending;
            lock (initializationLock)
            {
                if (initialization == null || initialization.IsCompleted)
                    initialization = Task.Run(() => ShowCore(FindFlyouts, GetDispatcherWindow, Notify, ControlDiagnostics.Write));
                pending = initialization;
            }
            // Normally completes before WMI writes, so the shell subscribes first.
            if (!pending.Wait(500))
                ControlDiagnostics.Write("Native brightness indicator initialization timed out; screen brightness adjustment will continue.");
        }
        catch (Exception error)
        {
            // Feedback must never prevent the panel adjustment.
            ControlDiagnostics.Write("Native brightness indicator: " + error.Message);
        }
    }

    // The controller receives 55 only until it creates the hardware flyout. After
    // creation it unsubscribes, and the flyout itself receives SHELLHOOK instead.
    // Both paths are required; sending only to either endpoint breaks one state.
    internal static bool ShowCore(Func<IReadOnlyList<IntPtr>> findFlyouts,
        Func<IntPtr> getDispatcher, Func<IntPtr, bool> notify, Action<string> log)
    {
        try
        {
            var candidates = findFlyouts();
            if (candidates.Count == 0)
            {
                var dispatcher = getDispatcher();
                if (dispatcher == IntPtr.Zero)
                {
                    log("Native brightness indicator: Explorer brightness dispatcher unavailable.");
                    return false;
                }
                if (!notify(dispatcher)) return false;
                // Re-resolve after creation, including the race with a real volume
                // key creating the flyout and unsubscribing the controller first.
                candidates = findFlyouts();
            }
            if (candidates.Count != 1)
            {
                log($"Native brightness indicator: expected one verified hardware flyout, found {candidates.Count} after initialization.");
                return false;
            }
            return notify(candidates[0]);
        }
        catch (Exception error)
        {
            log("Native brightness indicator: " + error.Message);
            return false;
        }
    }

    private static IReadOnlyList<IntPtr> FindFlyouts()
    {
        // Never retain HWNDs across Explorer restart.
        var candidates = new List<IntPtr>();
        IntPtr host = IntPtr.Zero;
        while ((host = FindWindowEx(IntPtr.Zero, host, "NativeHWNDHost", null)) != IntPtr.Zero)
            if (IsFlyout(host) && IsExplorerWindow(host)) candidates.Add(host);
        return candidates;
    }

    private static bool IsFlyout(IntPtr window) =>
        WindowClass(window) == "NativeHWNDHost" &&
        FindWindowEx(window, IntPtr.Zero, "DirectUIHWND", null) != IntPtr.Zero &&
        GetWindowBand(window, out uint band) && band == AboveLockUxBand;

    private static bool IsExplorerWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint processId);
        using var current = Process.GetCurrentProcess();
        return IsSystemExplorer(processId, current.SessionId,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
    }

    private static string WindowClass(IntPtr window)
    {
        var name = new StringBuilder(256);
        return GetClassName(window, name, name.Capacity) == 0 ? "" : name.ToString();
    }

    private static IntPtr GetDispatcherWindow()
    {
        // Ask the current shell for its exact hidden message window. Do not guess
        // among WorkerW windows, broadcast messages, or change volume to warm it up.
        var clsid = new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239");
        var iid = typeof(IShellServiceProvider).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out var shell));
        object? service = null;
        try
        {
            var sid = new Guid("BF8B6EDE-2069-4010-A9E7-4480E24C01CA");
            iid = typeof(IObjectWithWindow).GUID;
            Marshal.ThrowExceptionForHR(shell.QueryService(ref sid, ref iid, out service));
            Marshal.ThrowExceptionForHR(((IObjectWithWindow)service).GetWindow(out var window));
            return window != IntPtr.Zero && WindowClass(window) == "WorkerW" && IsExplorerWindow(window)
                ? window : IntPtr.Zero;
        }
        finally
        {
            if (service != null) Marshal.ReleaseComObject(service);
            Marshal.ReleaseComObject(shell);
        }
    }

    private static bool Notify(IntPtr window)
    {
        // Revalidate just before sending, in case Explorer exited during lookup.
        if (!IsExplorerWindow(window) || !(IsFlyout(window) || WindowClass(window) == "WorkerW")) return false;
        uint shellHook = RegisterWindowMessage("SHELLHOOK");
        if (shellHook != 0 && SendMessageTimeout(window, shellHook,
                new IntPtr(BrightnessShellNotification), IntPtr.Zero, 0x0003, 200, out _) != IntPtr.Zero) return true;
        ControlDiagnostics.Write($"Native brightness indicator notification failed: Win32 {Marshal.GetLastWin32Error()}.");
        return false;
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("B482CD93-28D0-4F19-B018-D0BD02102DCC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectWithWindow
    {
        // Slot order matters: GetWindow follows SetWindow. SetWindow is never used.
        [PreserveSig] int SetWindow(IntPtr window);
        [PreserveSig] int GetWindow(out IntPtr window);
    }

    private static bool IsSystemExplorer(uint processId, int sessionId, string expectedPath)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (process.SessionId != sessionId || !string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)) return false;
            IntPtr handle = OpenProcess(0x1000, false, processId); // QUERY_LIMITED_INFORMATION.
            if (handle == IntPtr.Zero) return false;
            try
            {
                var path = new StringBuilder(32768);
                uint length = (uint)path.Capacity;
                return QueryFullProcessImageName(handle, 0, path, ref length) &&
                    string.Equals(path.ToString(), expectedPath, StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(handle); }
        }
        catch { return false; } // A candidate may exit during enumeration.
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr hostParent, IntPtr after, string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IShellServiceProvider result);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowBand(IntPtr window, out uint band);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
