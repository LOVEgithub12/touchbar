using System.Diagnostics;

namespace WindowsTouchBar.Gaming;

// Accessed only under TouchBar's view lock. No process launch or input injection.
internal sealed class GameNavigation
{
    private readonly Func<double> clock;
    private GameDefinition? candidate;
    private int candidatePid;
    private IntPtr candidateWindow;
    private double candidateSince;
    private bool main, held;
    private double releasedAt;
    public GameDefinition? ActiveGame { get; private set; }
    public int ActivePid { get; private set; }
    public bool MainActive => main;
    public bool ForegroundMatches => candidate?.Id == ActiveGame?.Id && candidate is not null && candidatePid == ActivePid;
    public GameNavigation(Func<double>? clock = null) => this.clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    public void SetForeground(GameDefinition? game, int pid, IntPtr window)
    {
        if (candidate?.Id == game?.Id && candidatePid == pid && candidateWindow == window) return;
        candidate = game; candidatePid = pid; candidateWindow = window; candidateSince = clock();
        main = false;
    }
    public bool Tick()
    {
        string? oldId = ActiveGame?.Id; int oldPid = ActivePid; bool oldMain = main;
        if (candidate is not null && clock() - candidateSince >= 2)
        { ActiveGame = candidate; ActivePid = candidatePid; }
        else if (candidate is null && clock() - candidateSince >= 3)
        { ActiveGame = null; ActivePid = 0; main = false; }
        if (main && !held && clock() - releasedAt >= 4) main = false;
        return oldId != ActiveGame?.Id || oldPid != ActivePid || oldMain != main;
    }
    public bool OpenMain()
    {
        if (!ForegroundMatches) return false;
        main = true; releasedAt = clock(); return true;
    }
    public void CancelMain() => main = false;
    public void TouchDown() { held = true; releasedAt = clock(); }
    public void TouchMove() { held = true; releasedAt = clock(); }
    public void TouchUp() { held = false; releasedAt = clock(); }
}
