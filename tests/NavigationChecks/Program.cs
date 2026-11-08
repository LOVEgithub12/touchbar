using WindowsTouchBar.Lyrics;

var checks = new (string Name, Action Run)[]
{
    ("Paused and stopped playback stays on the full main page", PausedAndStopped),
    ("Manual main page returns after four seconds, then stays dismissed", FourSecondReturn),
    ("Touch at an unrendered idle boundary keeps the visible page", IdleBoundaryTouch),
    ("Every touch phase restarts the idle interval", TouchRestartsIdle),
    ("A held finger prevents automatic page changes", HeldTouch),
    ("Pause clears manual navigation and resume shows lyrics", PauseResume),
    ("Changing tracks while playing preserves the manual main page", TrackChange)
};
int failures = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS: " + check.Name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + check.Name + "\n" + error); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed. No clock waits, UI, player or USB access.");
return failures == 0 ? 0 : 1;

static void Require(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void PausedAndStopped()
{
    double now = 100;
    var navigation = new LyricsNavigation(() => now);
    Require(!navigation.ShowLyrics, "A stopped player showed lyrics.");
    navigation.OpenMain();
    now += 30;
    Require(!navigation.ShowLyrics, "A stopped player timed out into lyrics.");
    navigation.SetPlaying(true);
    Require(navigation.ShowLyrics, "Starting playback did not show lyrics.");
    navigation.SetPlaying(false);
    navigation.TouchDown(); navigation.TouchUp();
    now += 3600;
    Require(!navigation.ShowLyrics, "Paused playback returned to lyrics after idle.");
}

static void FourSecondReturn()
{
    double now = 100;
    var navigation = new LyricsNavigation(() => now);
    navigation.SetPlaying(true);
    navigation.OpenMain();
    Require(!navigation.ShowLyrics, "The player icon did not open the main page.");
    now = 103.999;
    Require(!navigation.ShowLyrics, "Main page disappeared before four seconds.");
    now = 104;
    Require(navigation.ShowLyrics, "Main page did not return at four seconds.");
    navigation.TouchDown(); navigation.TouchUp();
    Require(navigation.ShowLyrics, "Touching lyrics resurrected the expired main-page selection.");
}

static void IdleBoundaryTouch()
{
    double now = 0;
    var navigation = new LyricsNavigation(() => now);
    navigation.SetPlaying(true);
    navigation.OpenMain();
    now = 3.99;
    Require(!navigation.ShowLyrics, "Main page expired before the deadline.");
    // At 30 fps a touch may arrive just after the deadline but before the next
    // render tick. The still-visible control must remain under the finger.
    now = 4.01;
    navigation.TouchDown();
    now = 8.5;
    Require(!navigation.ShowLyrics, "A late touch allowed a page change beneath the held finger.");
    navigation.TouchUp();
    now = 12.49;
    Require(!navigation.ShowLyrics, "Release did not restart the idle interval.");
    now = 12.5;
    Require(navigation.ShowLyrics, "Idle did not resume after release.");
    navigation.TouchDown();
    Require(navigation.ShowLyrics, "Touch resurrected a main page that had already expired and rendered.");
}

static void TouchRestartsIdle()
{
    double now = 0;
    var navigation = new LyricsNavigation(() => now);
    navigation.SetPlaying(true);
    navigation.OpenMain();
    now = 3;
    navigation.TouchDown();
    now = 4;
    navigation.TouchMove();
    now = 5;
    navigation.TouchUp();
    now = 8.999;
    Require(!navigation.ShowLyrics, "Touch release did not restart the idle interval.");
    now = 9;
    Require(navigation.ShowLyrics, "Idle interval did not expire after the final touch release.");
}

static void HeldTouch()
{
    double now = 0;
    var navigation = new LyricsNavigation(() => now);
    navigation.SetPlaying(true);
    navigation.TouchDown();
    navigation.OpenMain();
    now = 60;
    Require(!navigation.ShowLyrics, "Page changed beneath a held finger.");
    navigation.TouchUp();
    now = 63.999;
    Require(!navigation.ShowLyrics, "Held button release did not start a fresh idle interval.");
    now = 64;
    Require(navigation.ShowLyrics, "Released main page did not return to lyrics.");
}

static void PauseResume()
{
    double now = 0;
    var navigation = new LyricsNavigation(() => now);
    navigation.SetPlaying(true);
    navigation.OpenMain();
    now = 2;
    navigation.SetPlaying(false);
    now = 100;
    Require(!navigation.ShowLyrics, "Pause left lyrics visible.");
    navigation.SetPlaying(true);
    Require(navigation.ShowLyrics, "Resume restored the old manual-main timeout instead of lyrics.");
}

static void TrackChange()
{
    double now = 0;
    var navigation = new LyricsNavigation(() => now);
    navigation.SetPlaying(true);
    navigation.OpenMain();
    now = 2;
    navigation.SetPlaying(true); // New track snapshot, same playing state.
    Require(!navigation.ShowLyrics, "A track change dismissed the main page.");
    now = 4;
    Require(navigation.ShowLyrics, "Track metadata updates extended the idle timeout.");
}
