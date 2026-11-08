using System.Text.Json;
using System.Diagnostics;

namespace WindowsTouchBar.Lyrics;

internal sealed class FoobarLyricsService : IDisposable
{
    private readonly string statePath;
    private readonly Action<string> log;
    private readonly LocalLyricsResolver resolver;
    private readonly CancellationTokenSource cancellation = new();
    private readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };
    private Task? worker;
    private FoobarPlaybackState? lastState;
    private string? lyricKey;
    private LrcDocument? document;
    private DateTime nextResolve = DateTime.MinValue;
    private string? lastError;
    private LyricsSnapshot current = LyricsSnapshot.Inactive;
    public event Action<LyricsSnapshot>? Published;
    public LyricsSnapshot Current => Volatile.Read(ref current);

    public FoobarLyricsService(string statePath, Action<string>? log = null)
    {
        this.statePath = statePath;
        this.log = log ?? (_ => { });
        resolver = new LocalLyricsResolver(this.log);
    }

    public void Start()
    {
        if (worker is not null) throw new InvalidOperationException("The lyric service has already started.");
        worker = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                ReadState();
                var state = lastState;
                var now = DateTimeOffset.UtcNow;
                if (state is null || !IsFresh(state, now) || (!state.IsPlaying && !state.IsPaused)) Publish(LyricsSnapshot.Inactive);
                else
                {
                    string key = string.Join('\0', state.Path, state.Title, state.Artist, state.LyricsDirectory, state.EmbeddedLyrics);
                    if (key != lyricKey || DateTime.UtcNow >= nextResolve)
                    {
                        // Clear immediately on a track change, but retain a valid
                        // current lyric if ESLyric is temporarily rewriting its file.
                        if (key != lyricKey) document = null;
                        lyricKey = key;
                        nextResolve = DateTime.UtcNow.AddSeconds(5);
                        try { document = resolver.Resolve(state) ?? document; }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                        {
                            if (lastError != error.Message) { lastError = error.Message; log("Lyrics source: " + error.Message); }
                        }
                    }
                    Publish(BuildSnapshot(state, document, DateTimeOffset.UtcNow));
                }
            }
            catch (Exception error)
            {
                if (lastError != error.Message) { lastError = error.Message; log("Lyrics: " + error.Message); }
                if (lastState is null || !IsFresh(lastState, DateTimeOffset.UtcNow)) Publish(LyricsSnapshot.Inactive);
            }
            try { await Task.Delay(250, cancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ReadState()
    {
        try
        {
            if (!File.Exists(statePath)) return;
            using var stream = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 2 * 1024 * 1024) throw new InvalidDataException("Playback state exceeds the size limit.");
            var state = JsonSerializer.Deserialize<FoobarPlaybackState>(stream, json);
            if (state is not null && state.Version == 1 && double.IsFinite(state.PositionSeconds) &&
                double.IsFinite(state.DurationSeconds) && state.PositionSeconds >= 0 && state.DurationSeconds >= 0)
                lastState = state;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // The existing JS panel writes a small file in place. A partial read
            // keeps the last complete snapshot, but never extends its freshness.
        }
    }

    internal static bool IsFresh(FoobarPlaybackState state, DateTimeOffset now)
    {
        long age = now.ToUnixTimeMilliseconds() - state.TimestampUtcMs;
        return age >= -1000 && age <= 3000;
    }

    internal static LyricsSnapshot BuildSnapshot(FoobarPlaybackState state, LrcDocument? lyrics, DateTimeOffset now)
    {
        if (!IsFresh(state, now) || (!state.IsPlaying && !state.IsPaused)) return LyricsSnapshot.Inactive;
        bool playing = state.IsPlaying && !state.IsPaused;
        double position = state.PositionSeconds;
        double age = Math.Max(0, (now.ToUnixTimeMilliseconds() - state.TimestampUtcMs) / 1000.0);
        if (playing) position += Math.Min(age, 0.75);
        if (state.DurationSeconds > 0) position = Math.Min(position, state.DurationSeconds);
        var frame = lyrics?.At(position, state.DurationSeconds);
        return new LyricsSnapshot(true, playing, state.IsPaused, state.Title ?? "", state.Artist ?? "",
            frame?.CurrentLine ?? "", frame?.NextLine ?? "", frame?.Progress ?? 0,
            position, state.DurationSeconds, lyrics?.HasLyrics ?? false)
        {
            HighlightRate = playing ? frame?.HighlightRate ?? 0 : 0,
            HighlightLimit = frame?.HighlightLimit ?? 0,
            HasWordTiming = frame?.HasWordTiming ?? false,
            TimingDocument = lyrics,
            // Keep the real source observation as well as the projected frame.
            // Re-reading the same JSON must not revive a stale playback clock.
            ObservedAtSeconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency -
                Math.Max(0, (DateTimeOffset.UtcNow - now).TotalSeconds) - age,
            SourcePositionSeconds = state.PositionSeconds,
            TrackKey = string.Join('\0', state.Path, state.Title, state.Artist)
        };
    }

    private void Publish(LyricsSnapshot snapshot)
    {
        if (snapshot == current) return;
        Volatile.Write(ref current, snapshot);
        Published?.Invoke(snapshot);
    }

    public void Dispose()
    {
        cancellation.Cancel();
        // The owner must dispose outside its render lock: a publication callback
        // can be waiting to acquire that lock.
        worker?.GetAwaiter().GetResult();
        cancellation.Dispose();
    }
}
