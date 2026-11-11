using System.Text.Json;

namespace WindowsTouchBar.Lyrics;

internal enum LyricsBackground { Aurora, Spectrum, Off }

internal static class VisualizerPreference
{
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "visualizer-preference.json");
    public static LyricsBackground Load(Action<string> log, string? path = null)
    {
        try
        {
            path ??= PathName;
            if (!File.Exists(path)) return LyricsBackground.Aurora;
            var preference = JsonSerializer.Deserialize<Preference>(File.ReadAllText(path));
            if (preference?.Enabled == false) return LyricsBackground.Off;
            return preference?.Background == nameof(LyricsBackground.Spectrum) ? LyricsBackground.Spectrum : LyricsBackground.Aurora;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log("Visualizer preference could not be read: " + ex.Message);
            return LyricsBackground.Aurora;
        }
    }
    public static void Save(LyricsBackground background, Action<string> log, string? path = null)
    {
        try
        {
            path ??= PathName;
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference
                { Enabled = background != LyricsBackground.Off, Background = background.ToString() }));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log("Visualizer preference could not be saved: " + ex.Message);
        }
    }
    private sealed class Preference
    {
        public bool Enabled { get; set; } = true;
        public string? Background { get; set; }
    }
}
