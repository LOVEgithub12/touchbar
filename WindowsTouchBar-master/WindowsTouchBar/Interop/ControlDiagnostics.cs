namespace WindowsTouchBar.Interop;

internal static class ControlDiagnostics
{
    private static readonly object sync = new();
    private static string? lastMessage;
    private static DateTime lastWrite;

    public static void Write(string message)
    {
        lock (sync)
        {
            var now = DateTime.UtcNow;
            if (message == lastMessage && now - lastWrite < TimeSpan.FromSeconds(5)) return;
            lastMessage = message;
            lastWrite = now;
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "WindowsTouchBar.controls.log"),
                    $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
            catch { /* Optional controls must not stop the display when logging fails. */ }
        }
    }
}
