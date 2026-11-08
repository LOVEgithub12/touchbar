using System.Diagnostics;

namespace WindowsTouchBar.Helpers;

internal enum FnInferenceTransition
{
    None,
    Pressed,
    Released,
    InactivityReleased
}

internal sealed class FnInferenceState
{
    internal const int InactivityReleaseSeconds = 60;
    private static readonly long InactivityReleaseTicks = Stopwatch.Frequency * InactivityReleaseSeconds;
    private readonly object gate = new();
    private bool pressed;
    private long lastActivityTimestamp = long.MinValue;

    public bool Pressed
    {
        get { lock (gate) return pressed; }
    }

    public FnInferenceTransition ObserveActivity(long timestamp, bool ordinaryKeyActivityNear)
    {
        lock (gate)
        {
            lastActivityTimestamp = timestamp;
            if (ordinaryKeyActivityNear) return FnInferenceTransition.None;

            pressed = !pressed;
            return pressed ? FnInferenceTransition.Pressed : FnInferenceTransition.Released;
        }
    }

    public FnInferenceTransition RecoverIfInactive(long timestamp)
    {
        lock (gate)
        {
            if (!pressed || lastActivityTimestamp == long.MinValue ||
                timestamp - lastActivityTimestamp < InactivityReleaseTicks)
                return FnInferenceTransition.None;

            pressed = false;
            return FnInferenceTransition.InactivityReleased;
        }
    }

    public FnInferenceTransition Reset()
    {
        lock (gate)
        {
            if (!pressed) return FnInferenceTransition.None;
            pressed = false;
            return FnInferenceTransition.Released;
        }
    }
}
