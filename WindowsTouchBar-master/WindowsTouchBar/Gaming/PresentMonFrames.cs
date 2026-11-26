using System.Globalization;
using System.Diagnostics;
using System.Text;

namespace WindowsTouchBar.Gaming;

// App-present intervals, grouped by swapchain. Summing multiple windows would double FPS.
internal sealed class PresentMonFrames
{
    private string[]? columns;
    private readonly Dictionary<string, Queue<(double At, double Ms)>> chains = new();
    public static string[] ParseCsv(string text)
    {
        var fields = new List<string>(); var field = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; }
            else if (text[i] == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(text[i]);
        }
        fields.Add(field.ToString()); return fields.ToArray();
    }
    public void Add(string line, int pid, double now)
    {
        var values = ParseCsv(line);
        if (values.Contains("ProcessID")) { columns = values; return; }
        if (columns is null || values.Length != columns.Length) return;
        string Get(string name) { int index = Array.FindIndex(columns, c => c.Equals(name, StringComparison.OrdinalIgnoreCase)); return index < 0 ? "" : values[index]; }
        if (!int.TryParse(Get("ProcessID"), out int actual) || actual != pid) return;
        if (!double.TryParse(Get("MsBetweenPresents"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) || !double.IsFinite(ms) || ms <= 0 || ms > 5000) return;
        // Use the event's real QPC time when available, so buffered CSV cannot revive stale FPS.
        string qpc = Get("TimeInQPC");
        if (string.IsNullOrEmpty(qpc)) qpc = Get("QPCTime");
        if (string.IsNullOrEmpty(qpc)) qpc = Get("CPUStartQPC");
        double at = now;
        if (!string.IsNullOrEmpty(qpc)) {
            if (!double.TryParse(qpc, NumberStyles.Float, CultureInfo.InvariantCulture, out double ticks)) return;
            at = ticks / Stopwatch.Frequency;
            if (!double.IsFinite(at) || now - at > 3 || at - now > .1) return;
        }
        string chain = Get("SwapChainAddress");
        if (!chains.TryGetValue(chain, out var frames))
        {
            if (chains.Count >= 32) chains.Remove(chains.MinBy(p => p.Value.LastOrDefault().At).Key);
            chains[chain] = frames = new();
        }
        if (frames.Count > 0 && at < frames.Last().At) return;
        frames.Enqueue((at, ms));
        // ETW flushes arrive in batches (~0.5–2.2 s observed). Calculate the
        // window in event time; retain it between batches with a hard age limit.
        while (frames.Count > 4000 || frames.Count > 0 && at - frames.Peek().At > 1.5) frames.Dequeue();
    }
    public (double? Fps, double? Ms) Read(double now)
    {
        foreach (var frames in chains.Values) if (frames.Count > 0 && now - frames.Last().At > 3) frames.Clear();
        var active = chains.Values.Where(q => q.Count >= 2).OrderByDescending(q => q.Count).FirstOrDefault();
        if (active is null) return (null, null);
        double ms = active.Average(f => f.Ms); return (1000 / ms, ms);
    }
}
