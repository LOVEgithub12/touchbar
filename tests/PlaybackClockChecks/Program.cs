using WindowsTouchBar.Lyrics;

var checks = new (string Name, Action Run)[]
{
    ("Playback advances every frame between quarter-second packets", Continuous),
    ("Small packet jitter slews without frame jumps or backwards motion", Jitter),
    ("Delayed observations are projected to receipt time", Delayed),
    ("Pause freezes and resume starts from the player position", Pause),
    ("Seeks and new tracks reset immediately", Seek),
    ("Stalled packets freeze after a bounded prediction horizon", Stale),
    ("Old and invalid packets do not corrupt the current clock", Invalid)
};
int failed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS: " + check.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL: " + check.Name + "\n" + error); }
}
Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed. No clock waits, player or USB access.");
return failed == 0 ? 0 : 1;

static void Near(double actual, double expected, string reason, double tolerance = 1e-8)
{
    if (Math.Abs(actual - expected) > tolerance) throw new InvalidOperationException($"{reason}: {actual} != {expected}");
}
static void Require(bool result, string reason) { if (!result) throw new InvalidOperationException(reason); }
static void Continuous()
{
    var clock = new LyricsPlaybackClock();
    clock.Update(26, true, "song", 10, 10);
    for (int frame = 1; frame <= 15; frame++) Near(clock.PositionAt(10 + frame / 60d), 26 + frame / 60d, "Clock stopped at a word/packet boundary");
    double before = clock.PositionAt(10.25);
    clock.Update(26.25, true, "song", 10.25, 10.25);
    Near(clock.PositionAt(10.25), before, "A correct packet snapped the clock");
}
static void Jitter()
{
    var clock = new LyricsPlaybackClock();
    clock.Update(0, true, "song", 0, 0);
    double last = 0;
    for (int frame = 1; frame <= 600; frame++)
    {
        double now = frame / 60d;
        if (frame % 15 == 0)
        {
            double before = clock.PositionAt(now);
            double jitter = frame % 30 == 0 ? 0.045 : -0.045;
            clock.Update(now + jitter, true, "song", now, now);
            Near(clock.PositionAt(now), before, "Quarter-second update jumped");
        }
        double position = clock.PositionAt(now);
        double delta = position - last;
        Require(delta >= 0.9 / 60d - 1e-8 && delta <= 1.1 / 60d + 1e-8, "Small correction caused an abrupt rate change");
        Require(Math.Abs(position - now) < 0.07, "Smoothing drift grew beyond jitter");
        last = position;
    }
}
static void Delayed()
{
    var clock = new LyricsPlaybackClock();
    clock.Update(5, true, "song", 10, 10.2);
    Near(clock.PositionAt(10.2), 5.2, "Delayed initial packet was not projected");
    Near(clock.PositionAt(10.3), 5.3, "Delayed packet stalled rendering");
}
static void Pause()
{
    var clock = new LyricsPlaybackClock();
    clock.Update(5, true, "song", 10, 10);
    clock.Update(5.2, false, "song", 10.2, 10.25);
    Near(clock.PositionAt(99), 5.2, "Paused position moved");
    clock.Update(5.2, true, "song", 99, 99);
    Near(clock.PositionAt(99.1), 5.3, "Resume inherited an old correction");
}
static void Seek()
{
    var clock = new LyricsPlaybackClock();
    clock.Update(50, true, "song", 10, 10);
    clock.Update(10, true, "song", 10.25, 10.25);
    Near(clock.PositionAt(10.25), 10, "Backward seek was smoothed");
    clock.Update(70, true, "song", 10.5, 10.5);
    Near(clock.PositionAt(10.5), 70, "Forward seek was smoothed");
    clock.Update(0, true, "other", 10.6, 10.6);
    Near(clock.PositionAt(10.6), 0, "New track retained old position");
}
static void Stale()
{
    var clock = new LyricsPlaybackClock();
    clock.Update(5, true, "song", 10, 10);
    Near(clock.PositionAt(15), 5.75, "Missing packets did not freeze");
    clock.Update(10, true, "song", 15, 15);
    Near(clock.PositionAt(15), 10, "Fresh playback did not recover immediately");
}
static void Invalid()
{
    var clock = new LyricsPlaybackClock();
    clock.Update(10, true, "song", 10, 10);
    clock.Update(0, true, "song", 9, 10.1);
    clock.Update(double.NaN, true, "song", 10.1, 10.1);
    Near(clock.PositionAt(10.2), 10.2, "Invalid/out-of-order packet corrupted time");
}
