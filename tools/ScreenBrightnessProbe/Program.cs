using System.Diagnostics;
using System.Management;
using WindowsTouchBar.Interop;

// An explicitly requested hardware check: one +2 adjustment, then restore the original value in finally.
var log = new List<string>();
int? original = null;
bool restored = false;
int exitCode = 0;
try
{
    original = BrightlessAPI.GetCurrentBrightness();
    byte[] levels;
    using (var search = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness WHERE Active=True"))
    using (var objects = search.Get())
    {
        using var monitor = objects.Cast<ManagementObject>().First();
        levels = (byte[])monitor["Level"];
        Log($"Instance={monitor["InstanceName"]}; original={original}; supported={levels.Min()}..{levels.Max()} ({levels.Length} levels)");
    }
    int requested = Math.Clamp(original.Value + 2, 0, 100);
    int target = levels.OrderBy(value => Math.Abs(value - requested)).First();
    Log($"Requested={requested}; supportedTarget={target}");
    var timer = Stopwatch.StartNew();
    int returned = BrightlessAPI.Adjust(2);
    Log($"Adjust(+2) returned={returned}; elapsed={timer.ElapsedMilliseconds}ms; immediateRead={BrightlessAPI.GetCurrentBrightness()}");
    int actual = WaitFor(target, 2000, "after-adjust");
    if (actual != target) throw new InvalidOperationException($"Panel did not reach target {target}; actual={actual}.");
    if (returned != target)
    {
        Log($"FAIL: Adjust returned stale level {returned}; hardware subsequently reached {target}.");
        exitCode = 1;
    }

    // Calling the same value adds no visible brightness step and exposes the exact .NET API result type.
    using (var search = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True"))
    using (var objects = search.Get())
    {
        foreach (ManagementObject method in objects)
        {
            using (method)
            {
                var result = method.InvokeMethod("WmiSetBrightness", new object[] { 0u, (byte)target });
                Log($"Same-level WmiSetBrightness raw return: type={result?.GetType().FullName ?? "<null>"}; value={result?.ToString() ?? "<null>"}");
            }
        }
    }
    Log($"Same-level confirmed={WaitFor(target, 2000, "after-same-level")}");
}
catch (Exception error)
{
    Log($"FAIL: {error}");
    exitCode = 1;
}
finally
{
    if (original.HasValue)
    {
        try
        {
            BrightlessAPI.SetCurrentBrightness(original.Value);
            int actual = WaitFor(original.Value, 2500, "restore");
            restored = actual == original.Value;
            Log($"RESTORE {(restored ? "PASS" : "FAIL")}: original={original}; final={actual}");
        }
        catch (Exception error) { Log($"RESTORE FAIL: {error}"); }
        if (!restored) exitCode = 2;
    }
    string report = Path.Combine(AppContext.BaseDirectory, "ScreenBrightnessProbe.log");
    File.WriteAllLines(report, log);
    Console.WriteLine($"Report={report}");
}
return exitCode;

int WaitFor(int expected, int timeout, string phase)
{
    var timer = Stopwatch.StartNew();
    int actual = -1, previous = -1;
    do
    {
        actual = BrightlessAPI.GetCurrentBrightness();
        if (actual != previous) Log($"{phase}: t={timer.ElapsedMilliseconds}ms actual={actual}");
        previous = actual;
        if (actual == expected) return actual;
        Thread.Sleep(25);
    } while (timer.ElapsedMilliseconds < timeout);
    return actual;
}

void Log(string text)
{
    string line = $"{DateTime.Now:O} {text}";
    log.Add(line);
    Console.WriteLine(line);
}
