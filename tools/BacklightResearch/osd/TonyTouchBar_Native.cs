using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace T2TouchBar;

internal sealed record ForegroundApp(string Executable, string Title, string? Path)
{
    public static readonly ForegroundApp Empty = new("", "", null);
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static ForegroundApp GetForegroundApp()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == 0) return ForegroundApp.Empty;
            GetWindowThreadProcessId(window, out var pid);
            using var process = Process.GetProcessById((int)pid);
            var title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            string? path = null;
            try { path = process.MainModule?.FileName; } catch { }
            return new ForegroundApp(process.ProcessName + ".exe", title.ToString(), path);
        }
        catch { return ForegroundApp.Empty; }
    }

    public static (int Percent, bool Charging) GetBattery()
    {
        if (!GetSystemPowerStatus(out var state) || state.BatteryLifePercent == 255) return (-1, false);
        return (state.BatteryLifePercent, state.AcLineStatus == 1);
    }

    public static void AdjustBrightness(int delta)
    {
        try
        {
            using var levels = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            var current = levels.Get().Cast<ManagementObject>().Select(item => Convert.ToInt32(item["CurrentBrightness"])).FirstOrDefault();
            var target = (byte)Math.Clamp(current + delta, 0, 100);
            using var methods = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject item in methods.Get()) item.InvokeMethod("WmiSetBrightness", [1u, target]);
        }
        catch { }
    }

    public static void TapKey(string shortcut)
    {
        var codes = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => TryVirtualKey(value, out var code) ? code : (ushort)0).ToArray();
        if (codes.Length == 0 || codes.Any(code => code == 0)) return;
        var inputs = codes.Select(code => new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = code } } })
            .Concat(codes.Reverse().Select(code => new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = code, Flags = 2 } } })).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    public static void FocusProcess(params string[] names)
    {
        foreach (var name in names)
        {
            var process = Process.GetProcessesByName(name).FirstOrDefault(item => item.MainWindowHandle != 0);
            if (process is not null) { SetForegroundWindow(process.MainWindowHandle); return; }
        }
    }

    private static bool TryVirtualKey(string value, out ushort key)
    {
        value = value.Trim().ToUpperInvariant();
        if (value.Length == 1 && value[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            key = value[0];
            return true;
        }
        if (value.StartsWith('F') && int.TryParse(value[1..], out var number) && number is >= 1 and <= 24)
        {
            key = (ushort)(0x70 + number - 1);
            return true;
        }
        key = value switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "SHIFT" => 0x10,
            "ALT" => 0x12,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "DELETE" or "DEL" => 0x2E,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "VOLUME_MUTE" => 0xAD,
            "VOLUME_DOWN" => 0xAE,
            "VOLUME_UP" => 0xAF,
            "MEDIA_NEXT" => 0xB0,
            "MEDIA_PREVIOUS" => 0xB1,
            "MEDIA_PLAY_PAUSE" => 0xB3,
            _ => 0
        };
        return key != 0;
    }
}
