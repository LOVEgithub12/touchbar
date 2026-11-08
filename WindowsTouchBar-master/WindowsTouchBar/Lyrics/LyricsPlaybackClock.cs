namespace WindowsTouchBar.Lyrics;

// Keeps the render clock continuous between player packets. Small timing errors
// change its rate briefly instead of snapping the highlight to each new sample.
internal sealed class LyricsPlaybackClock
{
    public const double MaximumPredictionSeconds = 0.75;
    public const double MaximumRateCorrection = 0.10;
    public const double SeekThresholdSeconds = 0.5;
    private bool initialized;
    private bool playing;
    private string trackKey = "";
    private double anchorPosition;
    private double anchorTime;
    private double correction;
    private double observationTime;

    // position is valid at observedAt. Both time arguments use the same
    // monotonic clock; now can be later when a publication waited for rendering.
    public void Update(double position, bool isPlaying, string key, double observedAt, double now)
    {
        if (!double.IsFinite(position) || position < 0 || !double.IsFinite(observedAt) || !double.IsFinite(now)) return;
        observedAt = Math.Min(observedAt, now);
        if (initialized && key == trackKey && observedAt < observationTime) return;
        double measuredNow = position + (isPlaying ? Math.Clamp(now - observedAt, 0, MaximumPredictionSeconds) : 0);
        double predictedNow = PositionAt(now);
        bool reset = !initialized || key != trackKey || isPlaying != playing || !isPlaying ||
            now - observationTime > MaximumPredictionSeconds || Math.Abs(measuredNow - predictedNow) >= SeekThresholdSeconds;

        anchorPosition = reset ? measuredNow : predictedNow;
        correction = reset ? 0 : measuredNow - predictedNow;
        anchorTime = now;
        observationTime = observedAt;
        playing = isPlaying;
        trackKey = key;
        initialized = true;
    }

    public double PositionAt(double now)
    {
        if (!initialized) return 0;
        if (!playing || !double.IsFinite(now)) return anchorPosition;
        // A stalled bridge cannot keep painting lyrics indefinitely. The service
        // separately marks a stale player inactive after its heartbeat timeout.
        double until = Math.Min(now, observationTime + MaximumPredictionSeconds);
        double elapsed = Math.Max(0, until - anchorTime);
        double adjustment = Math.Clamp(correction, -MaximumRateCorrection * elapsed, MaximumRateCorrection * elapsed);
        return Math.Max(0, anchorPosition + elapsed + adjustment);
    }
}
