using WindowsTouchBar.Interop;

int before = TouchBarBacklightService.ReadCurrentMode();
Console.WriteLine($"BeforeMode={before}");
if (!args.Contains("--roundtrip")) return;
using var confirmed = new ManualResetEventSlim(false);
using (var service = new TouchBarBacklightService(message =>
{
    Console.WriteLine(message);
    if (message.StartsWith("Touch Bar backlight: full/on confirmed;", StringComparison.Ordinal)) confirmed.Set();
}))
{
    service.SetLyricsVisible(true);
    if (!confirmed.Wait(3000)) throw new IOException("The service did not confirm an accepted full/on request.");
    int active = TouchBarBacklightService.ReadCurrentMode();
    Console.WriteLine($"ActiveMode={active}");
    if (active != 1) throw new IOException("Full/on was not confirmed.");
    service.SetLyricsVisible(false);
}
int restored = TouchBarBacklightService.ReadCurrentMode();
Console.WriteLine($"AfterMode={restored}");
if (restored != before) throw new IOException("Original mode was not restored.");
