namespace WindowsTouchBar.Views;

internal enum ForegroundViewSelection
{
    FunctionPage,
    ExplorerDialog,
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
        bool defaultMedia)
    {
        if (functionPageActive) return ForegroundViewSelection.FunctionPage;
        if (explorerDialogActive) return ForegroundViewSelection.ExplorerDialog;
        if (playerPlaying) return ForegroundViewSelection.Lyrics;
        if (configuredApplication) return ForegroundViewSelection.ConfiguredApplication;
        if (lyricsActive) return ForegroundViewSelection.Lyrics;
        return defaultMedia ? ForegroundViewSelection.DefaultMedia : ForegroundViewSelection.DefaultFunctions;
    }
}
