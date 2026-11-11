using System.Runtime.InteropServices;
using WindowsInput.Native;

namespace WindowsTouchBar.Interop;

/// <summary>Routes volume actions to the desktop shell regardless of foreground integrity.</summary>
internal static class NativeVolumeControl
{
    private const uint WmAppCommand = 0x0319;

    internal static bool IsVolumeKey(VirtualKeyCode key) => key is
        VirtualKeyCode.VOLUME_MUTE or VirtualKeyCode.VOLUME_DOWN or VirtualKeyCode.VOLUME_UP;

    public static void Send(VirtualKeyCode key)
    {
        try
        {
            int command = key switch
            {
                VirtualKeyCode.VOLUME_MUTE => 8,
                VirtualKeyCode.VOLUME_DOWN => 9,
                VirtualKeyCode.VOLUME_UP => 10,
                _ => throw new ArgumentOutOfRangeException(nameof(key))
            };
            // Reacquire the shell for every action so an Explorer restart cannot
            // leave a stale handle. Windows adjusts volume and owns the native OSD.
            IntPtr shell = GetShellWindow();
            if (shell == IntPtr.Zero)
            {
                ControlDiagnostics.Write("System volume: Windows desktop shell is unavailable.");
                return;
            }
            // Queue exactly one action without waiting on Explorer while holding
            // the touch/view lock. Do not also send a key: that can double a step.
            if (!PostMessage(shell, WmAppCommand, shell, new IntPtr(command << 16)))
                ControlDiagnostics.Write($"System volume command failed: Win32 {Marshal.GetLastWin32Error()}.");
        }
        catch (Exception error)
        {
            ControlDiagnostics.Write("System volume: " + error.Message);
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
