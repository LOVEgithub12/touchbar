using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;

using var output = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "fn-events.log"), false) { AutoFlush = true };
void Log(string text) { output.WriteLine($"{DateTime.Now:O} {text}"); }
using var signal = new AutoResetEvent(false);
using var device = Native.CreateFile(@"\\.\KeyManager", 0xc0000000, 3, 0, 3, 0, 0);
if (device.IsInvalid) { Log($"OPEN FAILED {Marshal.GetLastWin32Error()}"); return; }
if (!Native.DeviceIoControl(device, 0xb40320e3, signal.SafeWaitHandle.DangerousGetHandle(), 0, 0, 0, out _, 0))
{ Log($"REGISTER FAILED {Marshal.GetLastWin32Error()}"); return; }
Log("READY: observational event registration only; logs key activity counts, never key codes or text.");
var until = DateTime.UtcNow.AddMinutes(4);
int pulse = 0;
while (DateTime.UtcNow < until)
{
    if (!signal.WaitOne(100)) continue;
    int count = 0;
    for (int key = 8; key < 256; key++) if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) count++;
    Log($"PULSE {++pulse} activeVirtualKeys={count}");
}
Log($"DONE pulses={pulse}");
static class Native
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(SafeFileHandle device, uint code, nint input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
}
