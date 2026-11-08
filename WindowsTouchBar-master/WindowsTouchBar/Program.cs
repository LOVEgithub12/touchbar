using WindowsTouchBar;

using System.Text.Json;
using System.Text.Json.Nodes;

bool checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--diagnose", StringComparer.OrdinalIgnoreCase);
bool useWsl = args.Contains("--wsl", StringComparer.OrdinalIgnoreCase);
bool useUsbIp = args.Contains("--usbip", StringComparer.OrdinalIgnoreCase);
bool background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
const string StopEventName = @"Local\WindowsTouchBar.Stop";
string statusPath = Path.Combine(AppContext.BaseDirectory, "WindowsTouchBar.status.log");
string errorPath = Path.Combine(AppContext.BaseDirectory, "WindowsTouchBar.error.log");

void WriteStatus(string message) => File.WriteAllText(statusPath,
    $"{DateTime.Now:O}{Environment.NewLine}{message}{Environment.NewLine}");

try
{
    if (args.Contains("--stop", StringComparer.OrdinalIgnoreCase))
    {
        if (EventWaitHandle.TryOpenExisting(StopEventName, out var runningStopEvent))
        {
            using (runningStopEvent) runningStopEvent.Set();
        }
        return;
    }

    string? defaultMode = args.Contains("--media", StringComparer.OrdinalIgnoreCase) ? "media" :
        args.Contains("--functions", StringComparer.OrdinalIgnoreCase) ? "function" : null;
    if (defaultMode is not null)
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var settings = JsonNode.Parse(File.ReadAllText(settingsPath),
            new JsonNodeOptions { PropertyNameCaseInsensitive = true })?.AsObject()
            ?? throw new JsonException("settings.json must contain an object.");
        var views = settings["views"]?.AsArray()
            ?? throw new JsonException("settings.json must contain a views array.");
        foreach (var mode in new[] { "function", "media" })
        {
            var view = views.FirstOrDefault(v => string.Equals(v?["type"]?.GetValue<string>(), mode,
                StringComparison.OrdinalIgnoreCase))?.AsObject();
            if (view is null)
            {
                view = new JsonObject { ["type"] = mode };
                views.Add(view);
            }
            view["default"] = mode == defaultMode;
        }
        string tempPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, settingsPath, true);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    using var singleInstance = new Mutex(true, @"Local\WindowsTouchBar.Application", out bool firstInstance);
    if (!firstInstance)
    {
        // A running app owns the USB interface. Do not report a skipped diagnostic
        // as success or replace the running app's status log.
        Environment.ExitCode = checkOnly ? 2 : 0;
        return;
    }

    if (useWsl && useUsbIp) throw new ArgumentException("Select either --wsl or --usbip.");
    WriteStatus(useUsbIp ? "Opening the local USB/IP Touch Bar display and touch input..." : useWsl ? "Opening the WSL Touch Bar display bridge and touch input..." : "Opening the Touch Bar display and touch input...");
    using var touchBar = new TouchBar(useWsl, useUsbIp);
    touchBar.Ready += () => WriteStatus(checkOnly
        ? $"CHECK PASSED: {touchBar.DisplayDescription}; display handshake, touch input open, and first frame acknowledgement succeeded. Physical touch still needs a manual check."
        : $"RUNNING: {touchBar.DisplayDescription}; display initialized and first frame acknowledged. Use --stop to exit.");

    using var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName);
    var stopRegistration = ThreadPool.RegisterWaitForSingleObject(stopEvent,
        (_, _) => touchBar.RequestStop(), null, Timeout.Infinite, true);
    try
    {
        touchBar.Init(!checkOnly);
        if (touchBar.LastError is not null) throw touchBar.LastError;
        if (!checkOnly) WriteStatus("STOPPED: shutdown requested.");
    }
    finally
    {
        stopRegistration.Unregister(null);
    }
}
catch (Exception ex)
{
    File.WriteAllText(errorPath, $"{DateTime.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}");
    WriteStatus($"FAILED: {ex.Message}{Environment.NewLine}See WindowsTouchBar.error.log for details.");
    Environment.ExitCode = 1;
    if (!checkOnly && !background)
    {
        StartupMessage.Show("Touch Bar 未能启动", ex.Message +
            "\n\n请查看程序目录中的 WindowsTouchBar.status.log。\n如果提示 Display Device not found，需要先配置自定义显示驱动。");
    }
}

static class StartupMessage
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
    public static void Show(string caption, string text) => MessageBox(IntPtr.Zero, text, caption, 0x10);
}
