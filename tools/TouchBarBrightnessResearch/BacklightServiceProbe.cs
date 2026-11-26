using WindowsTouchBar.Interop;
using WindowsTouchBar.Events;

int before = TouchBarBacklightService.ReadCurrentMode();
Console.WriteLine($"BeforeMode={before}");
// Explicit opt-in: briefly turn only the validated Touch Bar backlight off,
// feed one gesture through the real wake policy, then restore the saved report.
// This verifies hardware reports, not a physical finger or the UI input route.
if (args.Contains("--touch-roundtrip"))
{
    using var opened = TouchBarBacklightService.OpenConnection();
    var saved = new byte[116];
    saved[0] = 1;
    opened.GetFeature(saved);
    try
    {
        var off = (byte[])saved.Clone();
        off[0] = 1; off[1] = 1; off[2] = 4;
        opened.SetFeature(off);
        if (TouchBarBacklightService.ReadCurrentMode() != 4)
            throw new IOException("The probe could not confirm the initial off state.");
        Console.WriteLine("OffMode=4");
        using var service = new TouchBarBacklightService(Console.WriteLine);
        bool down = service.HandleTouch(TouchEventType.TouchDown);
        int awake = TouchBarBacklightService.ReadCurrentMode();
        bool move = service.HandleTouch(TouchEventType.TouchMove);
        bool up = service.HandleTouch(TouchEventType.TouchUp);
        Console.WriteLine($"TouchMode={awake}; WakeOnlyGesture={down && move && up}");
        if (awake != 1 || !down || !move || !up)
            throw new IOException("The off-state gesture did not wake and suppress all three event types.");
        if (service.HandleTouch(TouchEventType.TouchDown) || service.HandleTouch(TouchEventType.TouchUp))
            throw new IOException("The second gesture was incorrectly suppressed.");
    }
    finally
    {
        saved[0] = 1; saved[1] = 1; saved[2] = (byte)before;
        opened.SetFeature(saved);
        Console.WriteLine($"AfterMode={TouchBarBacklightService.ReadCurrentMode()}");
    }
    return;
}
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
