using System.Text.Json;
using WindowsTouchBar.Interop;

namespace WindowsTouchBar.Commands;

internal sealed class FoobarPlaybackCommand : ITouchCommand
{
    private static readonly object mailboxLock = new();
    private const int MaximumCommands = 16;
    private readonly string action;
    private readonly string path;
    public FoobarPlaybackCommand(string action, string? mailboxPath = null)
    {
        if (action is not ("previous" or "playpause" or "next")) throw new ArgumentException("Unsupported player action.", nameof(action));
        this.action = action;
        path = mailboxPath ?? Path.Combine(AppContext.BaseDirectory, "foobar-command.json");
    }

    public void TouchDown()
    {
        lock (mailboxLock) WriteCommand();
    }

    private void WriteCommand()
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var pending = new List<PlaybackMessage>();
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length <= 4096)
                {
                    using var source = JsonDocument.Parse(File.ReadAllText(path));
                    var root = source.RootElement;
                    var messages = root.TryGetProperty("commands", out var queue) && queue.ValueKind == JsonValueKind.Array
                        ? queue.EnumerateArray().Take(MaximumCommands).ToArray() : new[] { root };
                    foreach (var item in messages)
                    {
                        var message = item.Deserialize<PlaybackMessage>();
                        if (message is not null && message.id is { Length: > 0 and <= 128 } &&
                            message.action is "previous" or "playpause" or "next" &&
                            now - message.timestampUtcMs is >= 0 and <= 3000)
                            pending.Add(message);
                    }
                }
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
            {
                // A damaged old mailbox must not prevent a new press from working.
            }
            pending = pending.TakeLast(MaximumCommands - 1).ToList();
            pending.Add(new PlaybackMessage(Guid.NewGuid().ToString("N"), action, now));
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { version = 1, commands = pending }));
            File.Move(temporary, path, true);
        }
        catch (Exception error) { ControlDiagnostics.Write("foobar2000 control: " + error.Message); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
    public void TouchUp() { }

    private sealed record PlaybackMessage(string id, string action, long timestampUtcMs);
}
