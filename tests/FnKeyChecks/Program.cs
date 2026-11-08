using System.Diagnostics;
using WindowsTouchBar.Helpers;

var checks = new (string Name, Action Run)[]
{
    ("Standalone Fn pulses alternate pressed and released", StandalonePulses),
    ("Ordinary keyboard activity does not toggle Fn and renews the safety interval", OrdinaryActivity),
    ("Long holds beyond eight seconds remain active", LongHold),
    ("An inferred press clears after sixty seconds of inactivity", StalePress),
    ("Manual reset clears a missed release", ManualReset)
};

int failed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS: " + check.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL: " + check.Name + "\n" + error); }
}
Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed.");
return failed == 0 ? 0 : 1;

static void StandalonePulses()
{
    var state = new FnInferenceState();
    long now = Stopwatch.Frequency;
    Require(state.ObserveActivity(now, false) == FnInferenceTransition.Pressed && state.Pressed,
        "Standalone pulse did not infer Fn down");
    Require(state.ObserveActivity(now + Stopwatch.Frequency, false) == FnInferenceTransition.Released && !state.Pressed,
        "Second standalone pulse did not infer Fn up");
}

static void OrdinaryActivity()
{
    var state = new FnInferenceState();
    long now = Stopwatch.Frequency;
    state.ObserveActivity(now, false);
    Require(state.ObserveActivity(now + 5 * Stopwatch.Frequency, true) == FnInferenceTransition.None && state.Pressed,
        "Ordinary key activity changed inferred Fn state");
    Require(state.RecoverIfInactive(now + (5 + FnInferenceState.InactivityReleaseSeconds - 1) * Stopwatch.Frequency) ==
        FnInferenceTransition.None, "Activity did not renew the safety interval");
}

static void LongHold()
{
    var state = new FnInferenceState();
    long now = Stopwatch.Frequency;
    state.ObserveActivity(now, false);
    Require(state.RecoverIfInactive(now + 9 * Stopwatch.Frequency) == FnInferenceTransition.None && state.Pressed,
        "A valid hold longer than eight seconds was cleared");
}

static void StalePress()
{
    var state = new FnInferenceState();
    long now = Stopwatch.Frequency;
    state.ObserveActivity(now, false);
    long beforeExpiry = now + (FnInferenceState.InactivityReleaseSeconds - 1) * Stopwatch.Frequency;
    Require(state.RecoverIfInactive(beforeExpiry) == FnInferenceTransition.None && state.Pressed,
        "Safety reset happened before the inactivity interval elapsed");
    Require(state.RecoverIfInactive(now + FnInferenceState.InactivityReleaseSeconds * Stopwatch.Frequency) ==
        FnInferenceTransition.InactivityReleased && !state.Pressed, "Stale inferred press was not cleared");
}

static void ManualReset()
{
    var state = new FnInferenceState();
    state.ObserveActivity(Stopwatch.Frequency, false);
    Require(state.Reset() == FnInferenceTransition.Released && !state.Pressed,
        "Manual recovery did not clear an inferred press");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
