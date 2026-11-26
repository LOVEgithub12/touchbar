using WindowsTouchBar.Views;

internal static class BrowserNavigationChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void IdleReturn()
    {
        double now = 0;
        var navigation = new BrowserMainNavigation(() => now);
        Require(!navigation.OpenMain(), "A main page opened without a configured Chrome context.");
        navigation.SetContext("CHROME.EXE", new IntPtr(10), false, false);
        Require(navigation.OpenMain() && navigation.MainActive, "Chrome did not open main.");
        now = 3.999;
        navigation.SetContext("chrome.exe", new IntPtr(10), false, false);
        Require(navigation.MainActive, "Main expired before four seconds.");
        now = 4;
        Require(!navigation.MainActive, "Foreground polling extended the idle timer.");
        navigation.TouchDown(); navigation.TouchUp();
        Require(!navigation.MainActive, "Touch resurrected an already expired main page.");
    }

    public static void TouchTiming()
    {
        double now = 0;
        var navigation = new BrowserMainNavigation(() => now);
        navigation.SetContext("chrome.exe", new IntPtr(10), false, false);
        navigation.TouchDown();
        Require(navigation.OpenMain(), "Chrome did not open main.");
        now = 60;
        Require(navigation.MainActive, "A held finger allowed the timer to change pages.");
        navigation.TouchMove();
        now = 61;
        navigation.TouchUp();
        now = 64.999;
        Require(navigation.MainActive, "The idle deadline did not start at release.");
        now = 65;
        Require(!navigation.MainActive, "The release deadline failed to expire.");

        now = 100;
        navigation.OpenMain();
        now = 104.01; // Deadline passed, but the main page is still visible.
        navigation.TouchDown();
        now = 120;
        Require(navigation.MainActive, "An unrendered boundary changed pages under a new touch.");
        navigation.TouchUp();
        now = 122;
        navigation.TouchDown();
        now = 123;
        navigation.TouchMove();
        now = 124;
        navigation.TouchUp();
        now = 127.999;
        Require(navigation.MainActive, "Repeated touch did not renew the idle interval.");
        now = 128;
        Require(!navigation.MainActive, "Main did not return after the final release.");
    }

    public static void ForegroundScope()
    {
        var navigation = new BrowserMainNavigation(() => 0);
        navigation.SetContext("chrome.exe", new IntPtr(10), false, false);
        navigation.OpenMain();
        navigation.SetContext("chrome.exe", new IntPtr(11), false, false);
        Require(!navigation.MainActive, "Switching Chrome windows preserved an obsolete return.");
        navigation.OpenMain();
        navigation.SetContext("msedge.exe", new IntPtr(12), false, false);
        Require(!navigation.MainActive && !navigation.OpenMain(), "A different browser inherited Chrome navigation.");
        navigation.SetContext("chrome.exe", new IntPtr(11), false, false);
        Require(!navigation.MainActive, "Returning to Chrome revived the old main page.");
        navigation.OpenMain();
        navigation.SetContext(null, new IntPtr(11), false, false);
        Require(!navigation.MainActive && !navigation.OpenMain(), "Removing browser configuration left main active.");
        navigation.SetContext("chrome.exe", IntPtr.Zero, false, false);
        Require(!navigation.OpenMain(), "Unknown foreground window opened temporary main.");
    }

    public static void PlaybackAndFunctions()
    {
        var navigation = new BrowserMainNavigation(() => 0);
        foreach (bool playing in new[] { true, false })
        {
            navigation.SetContext("chrome.exe", new IntPtr(10), false, false);
            Require(navigation.OpenMain(), "Chrome entry was not eligible.");
            navigation.SetContext("chrome.exe", new IntPtr(10), playing, !playing);
            Require(!navigation.MainActive && !navigation.OpenMain(), "Playback or F page did not cancel browser main.");
            navigation.SetContext("chrome.exe", new IntPtr(10), false, false);
            Require(!navigation.MainActive, "Pause or F-page return revived the old main page.");
        }
    }

    public static void ViewPriority()
    {
        Require(ForegroundViewPriority.Select(false, false, true, false, true, false, true)
            == ForegroundViewSelection.DefaultMedia, "Browser entry failed to open the full media bar.");
        Require(ForegroundViewPriority.Select(false, false, true, false, true, false)
            == ForegroundViewSelection.ConfiguredApplication, "Expiry failed to restore browser shortcuts after pause.");
        Require(ForegroundViewPriority.Select(false, false, true, true, true, false, true)
            == ForegroundViewSelection.Lyrics, "Browser main displaced active playback.");
        Require(ForegroundViewPriority.Select(true, false, true, false, false, true, true)
            == ForegroundViewSelection.FunctionPage, "The idle override displaced the explicit F page.");
        Require(ForegroundViewPriority.Select(false, true, true, false, false, true, true)
            == ForegroundViewSelection.ExplorerDialog, "The idle override displaced an Explorer dialog.");
        Require(ForegroundViewPriority.Select(false, false, false, false, false, false, true)
            == ForegroundViewSelection.DefaultFunctions, "A stale override escaped its configured application.");
    }
}
