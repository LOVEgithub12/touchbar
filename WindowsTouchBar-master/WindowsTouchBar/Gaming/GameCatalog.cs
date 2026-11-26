using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WindowsTouchBar.Gaming;

internal sealed record GameDefinition(string Id, string Name, string Icon, string[] ExecutablePaths, string[]? WindowTitleContains = null);

internal sealed class GameCatalog
{
    private readonly GameDefinition[] games;
    public GameCatalog(IEnumerable<GameDefinition> games) => this.games = games.ToArray();
    public static GameCatalog Load(Action<string>? log = null)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "game-monitor.json");
            if (!File.Exists(path)) return new(Array.Empty<GameDefinition>());
            return new(JsonSerializer.Deserialize<GameDefinition[]>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<GameDefinition>());
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { log?.Invoke("Game catalog: " + e.Message); return new(Array.Empty<GameDefinition>()); }
    }
    public GameDefinition? Match(string? path, string? title)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string name = Path.GetFileName(path);
        if (name.Equals("LeagueClient.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LeagueClientUx.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return games.FirstOrDefault(g => g.ExecutablePaths.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) &&
            (g.WindowTitleContains is not { Length: > 0 } || g.WindowTitleContains.Any(t => (title ?? "").Contains(t, StringComparison.OrdinalIgnoreCase))));
    }
    public GameDefinition? Find(Process? process, IntPtr window)
    {
        if (process is null) return null;
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(0x1000, false, process.Id);
            if (handle == IntPtr.Zero) return null;
            var path = new StringBuilder(32768); int length = path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref length)) return null;
            var title = new StringBuilder(1024); GetWindowText(window, title, title.Capacity);
            return Match(path.ToString(), title.ToString());
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or ArgumentException) { return null; }
        finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int length);
}
