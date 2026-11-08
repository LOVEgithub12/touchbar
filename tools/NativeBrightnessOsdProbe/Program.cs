using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Windows 10 twinui!CHardwareButtonFlyout::ShowFlyoutFromShellHook explicitly handles
// SHELLHOOK wParam=55 by selecting brightness (type 3), ignoring lParam.
var targets = new List<(IntPtr Host, IntPtr Child, uint ProcessId, uint Band)>();
IntPtr previous = IntPtr.Zero;
while ((previous = Native.FindWindowEx(IntPtr.Zero, previous, "NativeHWNDHost", null)) != IntPtr.Zero)
{
    Native.GetWindowThreadProcessId(previous, out uint processId);
    using var process = Process.GetProcessById((int)processId);
    if (!string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)) continue;
    IntPtr child = Native.FindWindowEx(previous, IntPtr.Zero, "DirectUIHWND", null);
    if (child == IntPtr.Zero) continue;
    if (!Native.GetWindowBand(previous, out uint band) || band != 18) continue; // AboveLockUX.
    targets.Add((previous, child, processId, band));
    Log($"Candidate: host=0x{previous.ToInt64():x}; child=0x{child.ToInt64():x}; pid={processId}; band={band}; visible={Native.IsWindowVisible(previous)}");
}
if (targets.Count != 1)
{
    Log($"No notification sent: expected one verified Explorer hardware flyout, found {targets.Count}.");
    return 2;
}
var target = targets[0];
uint shellHook = Native.RegisterWindowMessage("SHELLHOOK");
if (shellHook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
var foreground = Native.GetForegroundWindow();
bool sent = Native.PostMessage(target.Host, shellHook, new IntPtr(55), IntPtr.Zero);
Log($"ONE PostMessage: msg=0x{shellHook:x}; wParam=55; lParam=0; success={sent}; error={(sent ? 0 : Marshal.GetLastWin32Error())}");
if (!sent) return 1;
var elapsed = Stopwatch.StartNew();
bool visible = SpinWait.SpinUntil(() => Native.IsWindowVisible(target.Host) && Native.IsWindowVisible(target.Child), 2000);
Native.GetWindowRect(target.Host, out var bounds);
Log($"Result: visible={visible}; elapsedMs={elapsed.ElapsedMilliseconds}; rect={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}; focusUnchanged={foreground == Native.GetForegroundWindow()}");
Log("No hardware brightness was changed and no input key was sent.");
return visible ? 0 : 1;

static void Log(string message) => Console.WriteLine($"{DateTime.Now:O} {message}");

static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] internal static extern bool GetWindowBand(IntPtr window, out uint band);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
