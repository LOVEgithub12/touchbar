using System.Text.Json;

namespace WindowsTouchBar.Lyrics;

internal static class VisualizerPreference
{
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "visualizer-preference.json");
    public static bool Load(Action<string> log)
    {
        try
        {
            return !File.Exists(PathName) || JsonSerializer.Deserialize<Preference>(File.ReadAllText(PathName))?.Enabled != false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log("Visualizer preference could not be read: " + ex.Message);
            return true;
        }
    }
    public static void Save(bool enabled, Action<string> log)
    {
        try
        {
            string temporary = PathName + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference { Enabled = enabled }));
            File.Move(temporary, PathName, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log("Visualizer preference could not be saved: " + ex.Message);
        }
    }
    private sealed class Preference { public bool Enabled { get; set; } = true; }
}
