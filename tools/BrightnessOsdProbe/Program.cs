using System.Runtime.InteropServices;
using System.Text;
using WindowsTouchBar.Interop;

// This probe only displays its own indicator. It does not set brightness, send keys, or open a device.
try
{
IntPtr before = Native.GetForegroundWindow();
string className = $"WindowsTouchBar.BrightnessOsd.{Environment.ProcessId}";
BrightnessOsd.ShowScreen(37);
IntPtr window = IntPtr.Zero;
Require(SpinWait.SpinUntil(() =>
{
    window = Native.FindWindow(className, null);
    return window != IntPtr.Zero && Native.IsWindowVisible(window);
}, 3000), "Indicator became visible.");
Require(Native.GetForegroundWindow() == before, "Showing the indicator preserves foreground focus.");
long styles = Native.GetWindowLongPtr(window, -20).ToInt64();
Require((styles & 0x080800a8) == 0x080800a8, "Window is nonactivating, topmost, a tool window, and click-through.");
Require(Native.GetWindowRect(window, out var bounds) && bounds.Left >= 0 && bounds.Top >= 0 &&
    bounds.Width >= 230 && bounds.Height >= 88, "Window has a valid top-left desktop position and scaled size.");
Require(WaitForTitle(window, "屏幕亮度 37%"), "Screen percentage was rendered into the window state.");

// Repeated requests must update the same window and postpone its hide deadline.
for (int i = 0; i <= 100; i++)
{
    BrightnessOsd.ShowKeyboard(i);
    Thread.Sleep(12);
}
Require(WaitForTitle(window, "键盘背光 100%"), "Rapid updates retain the latest keyboard percentage.");
Thread.Sleep(900);
Require(Native.IsWindowVisible(window), "Repeat updates reset the auto-hide timer.");
BrightnessOsd.ShowUnavailable("键盘背光");
Require(WaitForTitle(window, "键盘背光 不可用"), "Unavailable hardware is shown without a fabricated percentage.");
Require(Native.GetForegroundWindow() == before, "Updates preserve foreground focus.");
Require(SpinWait.SpinUntil(() => !Native.IsWindowVisible(window), 3000), "Indicator hides automatically.");
BrightnessOsd.ShowScreen(21);
Require(SpinWait.SpinUntil(() => Native.IsWindowVisible(window), 1000), "Hidden indicator can be displayed again.");
BrightnessOsd.Shutdown();
Require(Native.FindWindow(className, null) == IntPtr.Zero, "Shutdown removes the window and its message loop.");
Console.WriteLine("PASS: Brightness OSD checks complete. No hardware brightness or keyboard input was changed.");
return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: " + error.Message);
    return 1;
}
finally { BrightnessOsd.Shutdown(); }

static void Require(bool condition, string description)
{
    if (!condition) { BrightnessOsd.Shutdown(); throw new InvalidOperationException(description); }
    Console.WriteLine("PASS: " + description);
}
static bool WaitForTitle(IntPtr window, string expected) => SpinWait.SpinUntil(() =>
{
    var text = new StringBuilder(256);
    Native.GetWindowText(window, text, text.Capacity);
    return text.ToString() == expected;
}, 1000);

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
}
