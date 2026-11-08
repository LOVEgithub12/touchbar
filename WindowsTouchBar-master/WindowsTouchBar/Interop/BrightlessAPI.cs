using System.Management;

namespace WindowsTouchBar.Interop;

internal class BrightlessAPI
{
    private static readonly object brightnessLock = new();

    public static int Adjust(int delta)
    {
        lock (brightnessLock)
        {
            var state = ReadState();
            int requested = Math.Clamp(state.Current + delta, 0, 100);
            int value = state.Levels.OrderBy(level => Math.Abs(level - requested)).First();
            if (value == state.Current) return state.Current;
            SetCurrentBrightness(value);
            // The Apple provider updates CurrentBrightness asynchronously, even with
            // Timeout=0. Read back the applied value instead of showing a made-up level.
            int actual = state.Current;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Thread.Sleep(25);
                actual = GetCurrentBrightness();
                if (actual == value) break;
            }
            return actual;
        }
    }

    public static int GetCurrentBrightness() => ReadState().Current;

    private static (int Current, byte[] Levels) ReadState()
    {
        var scope = new ManagementScope("root\\WMI");
        var query = new SelectQuery("SELECT * FROM WmiMonitorBrightness WHERE Active=True");

        using ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
        using ManagementObjectCollection objectCollection = searcher.Get();
            
        foreach (ManagementObject mObj in objectCollection)
        {
            using (mObj)
            {
                if (mObj["CurrentBrightness"] is byte value)
                {
                    var levels = mObj["Level"] as byte[];
                    return (value, levels is { Length: > 0 } ? levels : Enumerable.Range(0, 101).Select(i => (byte)i).ToArray());
                }
            }
        }

        throw new InvalidOperationException("Windows did not expose the internal display brightness.");
    }

    public static void SetCurrentBrightness(int value)
    {
        var scope = new ManagementScope("root\\WMI");
        var query = new SelectQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");

        using ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);
        using ManagementObjectCollection objectCollection = searcher.Get();

        bool applied = false;
        foreach (ManagementObject mObj in objectCollection)
        {
            using (mObj)
            {
                var result = mObj.InvokeMethod("WmiSetBrightness", new object[] { 0u, (byte)Math.Clamp(value, 0, 100) });
                // Apple advertises a Boolean return type but may return no value.
                // Numeric WMI status codes use zero for success.
                if (result is not null && result is not bool && Convert.ToUInt32(result) != 0)
                    throw new InvalidOperationException($"Windows brightness adjustment failed: {result}.");
                applied = true;
            }
        }
        if (!applied) throw new InvalidOperationException("Windows did not expose a display brightness control.");
    }
}
