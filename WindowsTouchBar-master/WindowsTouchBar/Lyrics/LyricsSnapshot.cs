using System.Text.Json.Serialization;

namespace WindowsTouchBar.Lyrics;

internal sealed record LyricsSnapshot(
    bool Active, bool IsPlaying, bool IsPaused, string Title, string Artist,
    string CurrentLine, string NextLine, double Progress, double PositionSeconds,
    double DurationSeconds, bool HasLyrics)
{
    public bool ShowLyrics => Active && IsPlaying && !IsPaused;
    // Normalized grapheme progress for this sampled frame. The animator uses
    // TimingDocument to evaluate every word boundary on the render clock.
    public double HighlightRate { get; init; }
    public double HighlightLimit { get; init; } = 1;
    public bool HasWordTiming { get; init; }
    [JsonIgnore] public LrcDocument? TimingDocument { get; init; }
    [JsonIgnore] public double? ObservedAtSeconds { get; init; }
    [JsonIgnore] public double? SourcePositionSeconds { get; init; }
    [JsonIgnore] public string TrackKey { get; init; } = "";
    public static LyricsSnapshot Inactive { get; } = new(false, false, false, "", "", "", "", 0, 0, 0, false);
}

internal sealed class FoobarPlaybackState
{
    public int Version { get; set; }
    public long TimestampUtcMs { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Path { get; set; }
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsPaused { get; set; }
    public string? LyricsDirectory { get; set; }
    public string? EmbeddedLyrics { get; set; }
}
