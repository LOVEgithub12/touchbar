using System.Diagnostics;

namespace WindowsTouchBar.Views;

// Owned by TouchBar's view lock. Only real input renews the monotonic idle clock;
// foreground/player polling must not extend a manually opened main page.
internal sealed class BrowserMainNavigation
{
    public const double MainIdleSeconds = 4;
    private readonly Func<double> clock;
    private IntPtr window;
    private bool eligible;
    private bool temporaryMain;
    private bool touchHeld;
    private double lastTouch;

    public BrowserMainNavigation(Func<double>? clock = null)
    {
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    public bool MainActive
    {
        get
        {
            if (temporaryMain && !touchHeld && clock() - lastTouch >= MainIdleSeconds)
                temporaryMain = false;
            return temporaryMain;
        }
    }

    public void SetContext(string? configuredApplication, IntPtr foregroundWindow,
        bool playerPlaying, bool functionPageActive)
    {
        bool nextEligible = string.Equals(configuredApplication, "chrome.exe", StringComparison.OrdinalIgnoreCase)
            && foregroundWindow != IntPtr.Zero && !playerPlaying && !functionPageActive;
        if (!nextEligible || !eligible || window != foregroundWindow) Cancel();
        window = foregroundWindow;
        eligible = nextEligible;
    }

    public bool OpenMain()
    {
        if (!eligible) return false;
        temporaryMain = true;
        lastTouch = clock();
        return true;
    }

    public void Cancel() => temporaryMain = false;
    public void TouchDown() => Touch(true);
    public void TouchMove() => Touch(true);
    public void TouchUp() => Touch(false);

    private void Touch(bool held)
    {
        // Keep the still-visible page if input arrives between its idle deadline
        // and the next render tick. Once observed, expiry cannot be resurrected.
        touchHeld = held;
        lastTouch = clock();
    }
}
