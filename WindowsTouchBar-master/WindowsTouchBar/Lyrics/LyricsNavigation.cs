using System.Diagnostics;

namespace WindowsTouchBar.Lyrics;

// Called under the owner's render lock. A monotonic clock makes the idle timer
// independent of wall-clock changes and allows deterministic checks without sleeps.
internal sealed class LyricsNavigation
{
    public const double MainIdleSeconds = 4;
    private readonly Func<double> clock;
    private bool playing;
    private bool temporaryMain;
    private bool touchHeld;
    private double lastTouch;

    public LyricsNavigation(Func<double>? clock = null)
    {
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    public bool ShowLyrics
    {
        get
        {
            ExpireTemporaryMain();
            return playing && !temporaryMain;
        }
    }

    public void SetPlaying(bool isPlaying)
    {
        playing = isPlaying;
        // Pause/stop always returns home. Resuming starts directly in lyrics,
        // rather than resurrecting a previous manually opened home page.
        if (!playing) temporaryMain = false;
        else ExpireTemporaryMain();
    }

    public void OpenMain()
    {
        temporaryMain = playing;
        lastTouch = clock();
    }

    public void TouchDown() => Touch(true);
    public void TouchMove() => Touch(true);
    public void TouchUp() => Touch(false);

    private void Touch(bool held)
    {
        // Input acts on the page that is still visible. If its deadline passed
        // between render ticks, accept the touch before deciding to leave it;
        // otherwise the next render could switch pages under a held finger.
        // An expiry already observed by ShowLyrics/SetPlaying stays latched.
        touchHeld = held;
        lastTouch = clock();
    }

    private void ExpireTemporaryMain()
    {
        if (temporaryMain && playing && !touchHeld && clock() - lastTouch >= MainIdleSeconds)
            temporaryMain = false;
    }
}
