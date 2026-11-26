using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using WindowsTouchBar.Interop;
using NAudio.CoreAudioApi;

if (!args.Contains("--show")) { Console.WriteLine("Use --show [--adjust] [--hold] [--cold-route] for an explicit live check."); return 2; }
using var enumerator = new MMDeviceEnumerator();
using var audio = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
float volume = audio.AudioEndpointVolume.MasterVolumeLevelScalar;
bool muted = audio.AudioEndpointVolume.Mute;
int brightness = BrightlessAPI.GetCurrentBrightness();
IntPtr foreground = Native.GetForegroundWindow();
string initialHosts = Describe();
Console.WriteLine($"Before brightness={brightness}; volume={volume}; mute={muted}; hosts={initialHosts}");
bool success = false;
try
{
    var timer = Stopwatch.StartNew();
    if (args.Contains("--cold-route"))
    {
        // Exercises COM lookup and the controller-to-host handoff without destroying
        // Explorer's existing flyout. This is NOT a fresh-login creation test.
        int enumerations = 0;
        success = NativeBrightnessOsd.ShowCore(
            () => ++enumerations == 1 ? Array.Empty<IntPtr>() : (IReadOnlyList<IntPtr>)Call("FindFlyouts")!,
            () => (IntPtr)Call("GetDispatcherWindow")!,
            window => (bool)Call("Notify", window)!, Console.WriteLine);
        Console.WriteLine($"Cold route lookup={success}; enumerationCount={enumerations}; actualInitialHostAlreadyExisted={initialHosts != "0"}");
    }
    else NativeBrightnessOsd.Show();
    Console.WriteLine($"Show returned in {timer.ElapsedMilliseconds}ms");
    if (args.Contains("--adjust"))
    {
        int delta = brightness >= 88 ? -2 : 2;
        int value = BrightlessAPI.Adjust(delta);
        Console.WriteLine($"Adjust({delta}) returned={value}; actual={BrightlessAPI.GetCurrentBrightness()}");
        if (value == brightness) throw new Exception("Brightness did not change.");
    }
    Thread.Sleep(150);
    string after = Describe();
    Console.WriteLine("After: " + after);
    success = after.Contains("visible=True, child=True");
    if (args.Contains("--hold"))
        for (int i=0; i<20; i++) { NativeBrightnessOsd.Show(); Thread.Sleep(1000); }
}
finally
{
    if (args.Contains("--adjust")) BrightlessAPI.SetCurrentBrightness(brightness);
    bool volumeUnchanged = volume == audio.AudioEndpointVolume.MasterVolumeLevelScalar && muted == audio.AudioEndpointVolume.Mute;
    bool focusUnchanged = foreground == Native.GetForegroundWindow();
    bool restored = brightness == BrightlessAPI.GetCurrentBrightness();
    Console.WriteLine($"Final volumeUnchanged={volumeUnchanged}; focusUnchanged={focusUnchanged}; brightnessRestored={restored}; hosts={Describe()}");
    success &= volumeUnchanged && focusUnchanged && restored;
}
return success ? 0 : 1;

static object? Call(string method, params object[] values) => typeof(NativeBrightnessOsd).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,values);
static string Describe()
{
    var items=new List<string>();
    foreach(var host in (IReadOnlyList<IntPtr>)Call("FindFlyouts")!)
    {
        var child=Native.FindWindowEx(host,IntPtr.Zero,"DirectUIHWND",null);
        Native.GetWindowRect(host,out var bounds);
        items.Add($"host=0x{host:x}, visible={Native.IsWindowVisible(host)}, child={Native.IsWindowVisible(child)}, bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}");
    }
    return items.Count==0 ? "0" : string.Join("; ",items);
}
static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string name,string? title);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect bounds);
}
