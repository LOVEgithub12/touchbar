using System.Buffers.Binary;
using System.Diagnostics;
using HidSharp;

namespace WindowsTouchBar.Interop;

/// <summary>
/// Controls the separate T2 keyboard-backlight HID collection, without replacing
/// the keyboard driver. Protocol: Linux hid-apple.c, apple_magic_backlight_set.
/// </summary>
internal static class KeyboardBacklightAPI
{
    private const uint KeyboardBacklightUsage = 0xff00000f;
    // Linux uses rate 0 during initialization. Apply immediately so repeated
    // button presses read a settled value rather than the middle of a fade.
    private const int Transition = 0x005e;
    private static readonly object sync = new();
    private static string? lastDevicePath;
    private static bool switchedOffHere;
    private static bool reportedPowerReadLimitation;

    // Report 3 cannot be read on this T2. Before our first adjustment this is the
    // device's saved brightness level; it does not prove that its LEDs are on.
    public static int GetCurrentBrightness()
    {
        lock (sync)
        {
            var device = FindDevice(out int minimum, out int maximum);
            using var stream = device.Open();
            int raw = ReadLevel(stream, minimum, maximum);
            return switchedOffHere ? 0 : ToPercent(raw, maximum);
        }
    }

    public static int Adjust(int delta)
    {
        lock (sync)
        {
            var device = FindDevice(out int minimum, out int maximum);
            using var stream = device.Open();
            int before = ReadLevel(stream, minimum, maximum);
            int current = switchedOffHere ? 0 : ToPercent(before, maximum);
            int percent = Math.Clamp(current + delta, 0, 100);

            if (!reportedPowerReadLimitation)
            {
                ControlDiagnostics.Write("Keyboard backlight: feature 3 power state is not readable; initial percentage uses the device's saved brightness level.");
                reportedPowerReadLimitation = true;
            }

            // The descriptor packs the 8-bit power value directly before the
            // 32-bit transition field, followed by 24 bits of padding.
            var power = new byte[9];
            power[0] = 3;
            power[1] = percent == 0 ? (byte)0 : (byte)1;
            BinaryPrimitives.WriteInt32LittleEndian(power.AsSpan(2, 4), Transition);
            stream.SetFeature(power); // Throws if the driver rejects the report.
            switchedOffHere = percent == 0;

            if (percent == 0)
            {
                ControlDiagnostics.Write($"Keyboard backlight: power-off report accepted; saved raw level={before}. Power readback is unavailable.");
                return 0;
            }

            int target = Math.Clamp((int)Math.Round(percent * maximum / 100.0), minimum, maximum);
            var brightness = new byte[9];
            brightness[0] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(brightness.AsSpan(1, 4), target);
            BinaryPrimitives.WriteInt32LittleEndian(brightness.AsSpan(5, 4), Transition);
            stream.SetFeature(brightness);

            // The device quantizes its level. Confirm the requested level with
            // a bounded retry for device/transport latency.
            int tolerance = Math.Max(2, maximum / 1000);
            int actual;
            var timer = Stopwatch.StartNew();
            do
            {
                actual = ReadLevel(stream, minimum, maximum);
                if (Math.Abs(actual - target) <= tolerance)
                {
                    int confirmed = ToPercent(actual, maximum);
                    ControlDiagnostics.Write($"Keyboard backlight: requested={percent}%, beforeRaw={before}, targetRaw={target}, readbackRaw={actual}, confirmed={confirmed}%.");
                    return confirmed;
                }
                Thread.Sleep(25);
            } while (timer.ElapsedMilliseconds < 1000);

            throw new IOException($"Keyboard backlight did not reach the requested level: target={target}, readback={actual}.");
        }
    }

    private static HidDevice FindDevice(out int minimum, out int maximum)
    {
        foreach (var device in DeviceList.Local.GetHidDevices(0x05ac, 0x8102))
        {
            var item = device.GetReportDescriptor().DeviceItems
                .FirstOrDefault(i => i.Usages.ContainsValue(KeyboardBacklightUsage));
            if (item is null) continue;

            var brightness = item.FeatureReports.FirstOrDefault(r => r.ReportID == 1);
            var power = item.FeatureReports.FirstOrDefault(r => r.ReportID == 3);
            if (brightness is null || power is null || brightness.Length != 9 || power.Length != 9)
                continue;

            var brightnessFields = brightness.DataItems.ToArray();
            var powerFields = power.DataItems.ToArray();
            if (brightnessFields.Length != 2 || powerFields.Length < 2 ||
                brightnessFields[0].ElementBits != 32 || brightnessFields[0].ElementCount != 1 ||
                brightnessFields[1].ElementBits != 32 || brightnessFields[1].ElementCount != 1 ||
                powerFields[0].ElementBits != 8 || powerFields[0].ElementCount != 1 ||
                powerFields[1].ElementBits != 32 || powerFields[1].ElementCount != 1 ||
                !brightnessFields[0].Usages.ContainsValue(0xff700001) ||
                !brightnessFields[1].Usages.ContainsValue(0xff700002) ||
                !powerFields[0].Usages.ContainsValue(0xff700003) ||
                !powerFields[1].Usages.ContainsValue(0xff700004))
                continue;

            minimum = brightnessFields[0].LogicalMinimum;
            maximum = brightnessFields[0].LogicalMaximum;
            if (minimum < 0 || maximum <= minimum) continue;
            if (lastDevicePath != device.DevicePath)
            {
                lastDevicePath = device.DevicePath;
                switchedOffHere = false;
            }
            return device;
        }
        throw new NotSupportedException("A supported Apple T2 keyboard-backlight HID collection was not found.");
    }

    private static int ReadLevel(HidStream stream, int minimum, int maximum)
    {
        var report = new byte[9];
        report[0] = 1;
        stream.GetFeature(report);
        // This device returns report ID 0 for GET_REPORT(1); the four-byte
        // brightness payload still starts at offset 1, verified by a roundtrip.
        int value = BinaryPrimitives.ReadInt32LittleEndian(report.AsSpan(1, 4));
        if (value < 0 || value > maximum || (value > 0 && value < minimum))
            throw new IOException($"The keyboard returned an invalid backlight level: {value}.");
        return value;
    }

    private static int ToPercent(int value, int maximum) =>
        Math.Clamp((int)Math.Round(value * 100.0 / maximum), 0, 100);
}
