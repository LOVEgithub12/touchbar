using System.Diagnostics;

namespace WindowsTouchBar.Lyrics;

// The display owns a monotonic clock and the complete immutable timing document.
// Crossing a word/line boundary never waits for the next 250 ms player poll.
internal sealed class LyricsFrameAnimator
{
    private readonly Func<double> now;
    private readonly LyricsPlaybackClock playback = new();
    private LyricsSnapshot snapshot = LyricsSnapshot.Inactive;

    public LyricsFrameAnimator(Func<double>? clock = null) =>
        now = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

    public void Update(LyricsSnapshot value)
    {
        double time = now();
        playback.Update(value.SourcePositionSeconds ?? value.PositionSeconds, value.IsPlaying,
            string.IsNullOrEmpty(value.TrackKey) ? value.Title + "\0" + value.Artist : value.TrackKey,
            value.ObservedAtSeconds ?? time, time);
        snapshot = value;
    }

    public LyricsSnapshot Current
    {
        get
        {
            double position = playback.PositionAt(now());
            if (snapshot.DurationSeconds > 0) position = Math.Min(position, snapshot.DurationSeconds);
            var frame = snapshot.TimingDocument?.At(position, snapshot.DurationSeconds);
            if (frame is null) return snapshot with { PositionSeconds = position };
            return snapshot with
            {
                PositionSeconds = position,
                CurrentLine = frame.CurrentLine, NextLine = frame.NextLine, Progress = frame.Progress,
                HighlightRate = snapshot.IsPlaying ? frame.HighlightRate : 0,
                HighlightLimit = frame.HighlightLimit, HasWordTiming = frame.HasWordTiming
            };
        }
    }
}
