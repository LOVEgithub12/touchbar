using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsTouchBar.Interop;

/// <summary>Asks the Windows 10 shell to display its own hardware brightness flyout.</summary>
internal static class NativeBrightnessOsd
{
    private const uint AboveLockUxBand = 18;
    private const int BrightnessShellNotification = 55;

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
            // On Windows 10, twinui!CHardwareButtonFlyout::ShowFlyoutFromShellHook
            // maps 55 to brightness mode (3), registers brightness notifications,
            // and ignores lParam. This is shell-version-specific, not WM_APPCOMMAND.
            // Re-enumerate each time so restarting Explorer cannot leave a stale HWND.
            var candidates = new List<IntPtr>();
            string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var currentProcess = Process.GetCurrentProcess();
            int sessionId = currentProcess.SessionId;
            IntPtr host = IntPtr.Zero;
            while ((host = FindWindowEx(hostParent: IntPtr.Zero, host, "NativeHWNDHost", null)) != IntPtr.Zero)
            {
                if (FindWindowEx(host, IntPtr.Zero, "DirectUIHWND", null) == IntPtr.Zero) continue;
                if (!GetWindowBand(host, out uint band) || band != AboveLockUxBand) continue;
                GetWindowThreadProcessId(host, out uint processId);
                if (IsSystemExplorer(processId, sessionId, explorerPath)) candidates.Add(host);
            }
            if (candidates.Count != 1)
            {
                ControlDiagnostics.Write($"Native brightness indicator unavailable: expected one Windows Explorer hardware flyout, found {candidates.Count}. If Explorer was restarted, its native volume flyout may need to be opened once.");
                return;
            }
            uint shellHook = RegisterWindowMessage("SHELLHOOK");
            // Complete the handler before WMI changes the value, allowing Windows
            // to register its brightness watcher. Never block on a hung shell.
            if (shellHook == 0 || SendMessageTimeout(candidates[0], shellHook,
                    new IntPtr(BrightnessShellNotification), IntPtr.Zero, 0x0003, 200, out _) == IntPtr.Zero)
                ControlDiagnostics.Write($"Native brightness indicator notification failed: Win32 {Marshal.GetLastWin32Error()}.");
        }
        catch (Exception error)
        {
            // Feedback must never prevent the panel adjustment.
            ControlDiagnostics.Write("Native brightness indicator: " + error.Message);
        }
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
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowBand(IntPtr window, out uint band);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
