namespace WindowsTouchBar.Views;

internal enum ForegroundViewSelection
{
    FunctionPage,
    ExplorerDialog,
    GameMonitor,
    ConfiguredApplication,
    Lyrics,
    DefaultMedia,
    DefaultFunctions
}

internal static class ForegroundViewPriority
{
    public static ForegroundViewSelection Select(
        bool functionPageActive,
        bool explorerDialogActive,
        bool configuredApplication,
        bool playerPlaying,
        bool lyricsActive,
        bool defaultMedia,
        bool browserMainActive = false,
        bool gameActive = false,
        bool gameMainActive = false)
    {
        if (functionPageActive) return ForegroundViewSelection.FunctionPage;
        if (explorerDialogActive) return ForegroundViewSelection.ExplorerDialog;
        if (gameActive) return gameMainActive ? ForegroundViewSelection.DefaultMedia : ForegroundViewSelection.GameMonitor;
        if (playerPlaying) return ForegroundViewSelection.Lyrics;
        if (browserMainActive && configuredApplication) return ForegroundViewSelection.DefaultMedia;
        if (configuredApplication) return ForegroundViewSelection.ConfiguredApplication;
        if (lyricsActive) return ForegroundViewSelection.Lyrics;
        return defaultMedia ? ForegroundViewSelection.DefaultMedia : ForegroundViewSelection.DefaultFunctions;
    }
}
