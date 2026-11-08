using System.Text.Json;
using WindowsTouchBar.Lyrics;
using WindowsTouchBar.Commands;

if (args.Length == 2 && args[0] == "--resolve-state")
{
    var state = JsonSerializer.Deserialize<FoobarPlaybackState>(File.ReadAllText(args[1]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("No foobar2000 state was found.");
    var resolver = new LocalLyricsResolver(Console.WriteLine);
    var document = resolver.Resolve(state);
    Console.WriteLine(JsonSerializer.Serialize(new { state.Title, Found = document is not null, LyricTitle = document?.Title, LyricArtist = document?.Artist, TimedLines = document?.Lines.Count }));
    return document is null ? 1 : 0;
}

if (args.Length == 2 && args[0] == "--inspect")
{
    int files = 0, timed = 0, lines = 0, enhanced = 0;
    var invalid = new List<string>();
    foreach (string path in Directory.EnumerateFiles(args[1], "*.lrc"))
    {
        files++;
        var document = LrcDocument.Parse(File.ReadAllText(path));
        if (document.HasLyrics) timed++; else invalid.Add(Path.GetFileName(path));
        lines += document.Lines.Count;
        enhanced += document.Lines.Count(line => line.Words.Count > 0);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { Files = files, TimedDocuments = timed, TimedLines = lines, EnhancedLines = enhanced, NoTimedLyrics = invalid }));
    return 0;
}

var checks = new (string Name, Action Run)[]
{
    ("LRC timestamps, offset, repeated chorus and blank interludes", ParseAndSeek),
    ("Enhanced word timing keeps Chinese text and progress", Enhanced),
    ("Karaoke timing follows graphemes and stops at timed boundaries", KaraokeTiming),
    ("Rendered karaoke crosses word and line boundaries between player polls", FrameAnimation),
    ("Exact matching rejects wrong artists and ambiguous files", Resolve),
    ("Playback pause, seek, end and stale snapshots", Snapshot),
    ("Service tolerates torn JSON and clears a stopped player", Service),
    ("Same-track refresh retains lyrics during file rewrites", Refresh),
    ("Rapid playback taps retain order in a bounded queue", PlaybackQueue)
};
int failed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS: " + check.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL: " + check.Name + "\n" + error); }
}
Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed. No USB, hardware, or external player files were accessed.");
return failed == 0 ? 0 : 1;

static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void ParseAndSeek()
{
    var lrc = LrcDocument.Parse("[ti:中文歌]\n[offset:500]\n[00:01.5][00:10.000]你好\n[00:04.00]\n[00:06:25]再见\n[00:66.00]invalid");
    Require(lrc.Title == "中文歌" && lrc.Lines.Count == 4, "Metadata/multiple times/invalid seconds parsing failed.");
    Require(lrc.At(0.9, 15).CurrentLine == "" && lrc.At(1, 15).CurrentLine == "你好", "Positive offset must advance the lyric.");
    Require(lrc.At(4, 15).CurrentLine == "", "Timed blank line did not clear the lyric.");
    Require(lrc.At(6, 15).CurrentLine == "再见" && lrc.At(10, 15).CurrentLine == "你好", "Seek/repeated chorus lookup failed.");
    Require(lrc.At(1.1, 15).CurrentLine == "你好", "Backward seek failed.");
    var translation = LrcDocument.Parse("[00:01]hello\n[00:01]你好\n[00:01]hello");
    Require(translation.At(1, 5).CurrentLine == "hello / 你好", "Same-time translation/duplicates failed.");
}
static void Enhanced()
{
    var lrc = LrcDocument.Parse("[00:10]<00:10>你<00:11>好<00:12>\n[00:15]下一句");
    Require(lrc.At(10.5, 20).CurrentLine == "你好", "Enhanced timestamps were visible in lyric text.");
    Require(Math.Abs(lrc.At(10.5, 20).Progress - 0.25) < 0.001, "Word progress was not weighted by characters.");
    Require(lrc.At(12.1, 20).Progress == 1, "Terminal word timestamp was ignored.");
}
static void KaraokeTiming()
{
    var lrc = LrcDocument.Parse("[00:10]<00:10>你<00:11>好<00:12>\n[00:15]下一句");
    var current = lrc.At(10.5, 20);
    Require(current.HasWordTiming && Math.Abs(current.HighlightRate - 0.5) < 0.001 && current.HighlightLimit == 0.5,
        "The UI cannot smoothly advance to the next word boundary.");
    Require(lrc.At(12.1, 20).Progress == 1 && lrc.At(12.1, 20).HighlightRate == 0,
        "Color continued advancing after a terminal timestamp during the gap.");
    Require(Math.Abs(lrc.At(10.2, 20).Progress - 0.1) < 0.001, "Seeking backward retained completed text.");
    var unicode = LrcDocument.Parse("[00:00]<00:00>😀<00:01>e\u0301<00:02>中<00:03>\n[00:05]下一句");
    Require(Math.Abs(unicode.At(0.5, 8).Progress - 1.0 / 6) < 0.001 &&
        Math.Abs(unicode.At(1.5, 8).Progress - 0.5) < 0.001,
        "Emoji or combining accents were counted as multiple displayed characters.");
    var prefix = LrcDocument.Parse("[00:00]你好<00:02>\n[00:05]下一句");
    Require(Math.Abs(prefix.At(1, 8).Progress - 0.5) < 0.001, "Untimed prefix before a terminal marker stayed uncolored.");
    var plain = LrcDocument.Parse("[00:00]你好\n[00:02]世界");
    Require(!plain.At(1, 3).HasWordTiming && plain.At(1, 3).Progress == 0.5,
        "A line-only approximation was presented as real word timing.");
    Require(plain.At(2.5, 3).Progress == 0.5, "Final short line ignored the known track duration.");
    var state = new FoobarPlaybackState { TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), IsPlaying = true, IsPaused = true, PositionSeconds = 10.5, DurationSeconds = 20 };
    var paused = FoobarLyricsService.BuildSnapshot(state, lrc, DateTimeOffset.UtcNow);
    Require(paused.Progress == 0.25 && paused.HighlightRate == 0 && paused.HasWordTiming,
        "Paused karaoke retained an advancing highlight rate.");
}
static void FrameAnimation()
{
    double time = 0;
    var document = LrcDocument.Parse("[00:00]<00:00>甲<00:00.16>乙<00:00.32>丙<00:00.48>丁<00:00.64>\n[00:00.80]<00:00.80>戊<00:00.96>己<00:01.12>");
    var animation = new LyricsFrameAnimator(() => time);
    LyricsSnapshot Packet(double sourceTime, bool playing = true) =>
        new(true, playing, !playing, "song", "artist", "甲乙丙丁", "戊己", 0, sourceTime, 2, true)
        { TimingDocument = document, SourcePositionSeconds = sourceTime, ObservedAtSeconds = sourceTime, TrackKey = "song" };
    animation.Update(Packet(0));
    double previousProgress = 0;
    for (int frame = 1; frame < 39; frame++)
    {
        time = frame / 60d;
        if (frame % 15 == 0) animation.Update(Packet(time));
        var current = animation.Current;
        double expected = Math.Min(1, time / 0.64);
        Require(Math.Abs(current.Progress - expected) < 1e-6, "A word boundary stalled or jumped between the 250 ms packets.");
        Require(current.Progress > previousProgress, "Active sung text froze while awaiting another player packet.");
        previousProgress = current.Progress;
    }
    time = 0.75; animation.Update(Packet(time));
    time = 0.82;
    Require(animation.Current.CurrentLine == "戊己" && animation.Current.Progress > 0,
        "A new lyric line waited for the next player packet.");
    time = 0.9;
    animation.Update(Packet(0.9, false));
    double frozen = animation.Current.Progress;
    time = 5;
    Require(animation.Current.Progress == frozen, "Paused color kept moving.");
    animation.Update(Packet(0.1) with { ObservedAtSeconds = time });
    Require(animation.Current.CurrentLine == "甲乙丙丁" && Math.Abs(animation.Current.Progress - 0.15625) < 1e-6,
        "Resume/backward seek did not immediately reset the timing document position.");
    time = 6;
    var old = Packet(0) with { ObservedAtSeconds = 5, PositionSeconds = 0.75, TrackKey = "new-track" };
    animation.Update(old);
    double stalePosition = animation.Current.PositionSeconds;
    time = 7; animation.Update(old);
    Require(animation.Current.PositionSeconds == stalePosition, "Repeated stale JSON extended animation beyond its original prediction horizon.");
}

static void Resolve()
{
    string root = Path.Combine(Path.GetTempPath(), "TouchBarLyricsChecks-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        string audio = Path.Combine(root, "song.flac");
        string cache = Path.Combine(root, "cache"); Directory.CreateDirectory(cache);
        var state = new FoobarPlaybackState { Path = audio, Title = "歌名", Artist = "甲", LyricsDirectory = cache };
        File.WriteAllText(Path.ChangeExtension(audio, ".lrc"), "[ti:另一首]\n[00:00]wrong sidecar");
        File.WriteAllText(Path.Combine(cache, "甲 - 歌名.lrc"), "[ar:甲]\n[ti:歌名]\n[00:00]正确歌词");
        File.WriteAllText(Path.Combine(cache, "歌名.lrc"), "[ar:乙]\n[00:00]wrong artist");
        var resolver = new LocalLyricsResolver();
        Require(resolver.Resolve(state)?.At(0, 2).CurrentLine == "正确歌词", "Wrong artist or sidecar was selected.");
        File.WriteAllText(Path.Combine(cache, "甲_歌名.lrc"), "[ar:甲]\n[00:00]different version");
        Require(resolver.Resolve(state) is null, "Ambiguous normalized filenames selected an arbitrary version.");
        state.EmbeddedLyrics = "[00:00]embedded";
        Require(resolver.Resolve(state)?.At(0, 2).CurrentLine == "embedded", "Timed embedded lyrics were not supported.");
        state.EmbeddedLyrics = null;
        File.WriteAllText(Path.ChangeExtension(audio, ".lrc"), "[00:00]sidecar");
        Require(resolver.Resolve(state)?.At(0, 2).CurrentLine == "sidecar", "Track-specific sidecar did not take precedence.");
        File.WriteAllText(Path.Combine(cache, "庄心妍-以后的以后.lrc"), "[00:33.81]风决定要走云怎么挽留");
        var untagged = new FoobarPlaybackState { Path = Path.Combine(root, "庄心妍-以后的以后.wav"), Title = "庄心妍-以后的以后", Artist = "", LyricsDirectory = cache };
        Require(resolver.Resolve(untagged)?.At(34, 200).CurrentLine == "风决定要走云怎么挽留", "Untagged WAV filename title did not resolve the matching local lyrics.");
        File.WriteAllText(Path.Combine(cache, "庄心妍-再见只是陌生人.lrc"), "[ti:再见只是陌生人]\n[ar:庄心妍]\n[00:26.01]爱过你就当你是错的人");
        untagged.Path = Path.Combine(root, "庄心妍-再见只是陌生人.wav"); untagged.Title = "庄心妍-再见只是陌生人";
        Require(resolver.Resolve(untagged)?.At(27, 251).CurrentLine == "爱过你就当你是错的人", "Tagged LRC was rejected for the exact untagged artist-title WAV filename.");
        untagged.Path = Path.Combine(root, "再见只是陌生人-庄心妍.wav"); untagged.Title = "再见只是陌生人-庄心妍";
        Require(resolver.Resolve(untagged)?.At(27, 251).CurrentLine == "爱过你就当你是错的人", "Reversed artist/title cache filename did not resolve with confirming LRC metadata.");
        File.WriteAllText(Path.Combine(cache, "再见只是陌生人.lrc"), "[ti:再见只是陌生人]\n[ar:别的歌手]\n[00:00]wrong cover");
        Require(resolver.Resolve(untagged)?.At(27, 251).CurrentLine == "爱过你就当你是错的人", "Filename fallback accepted a different artist's title-only lyric.");
        untagged.Artist = "别的歌手";
        Require(resolver.Resolve(untagged) is null, "Filename fallback overrode an explicit conflicting artist tag.");
        untagged.Artist = ""; untagged.Path = Path.Combine(root, "unrelated.wav");
        Require(resolver.Resolve(untagged) is null, "A real title tag was treated as a filename fallback.");
    }
    finally { Directory.Delete(root, true); }
}
static void Snapshot()
{
    var now = DateTimeOffset.UtcNow;
    var state = new FoobarPlaybackState { TimestampUtcMs = now.AddMilliseconds(-500).ToUnixTimeMilliseconds(), IsPlaying = true, PositionSeconds = 1, DurationSeconds = 10, Title = "song" };
    var lrc = LrcDocument.Parse("[00:01]one\n[00:02]two");
    Require(FoobarLyricsService.BuildSnapshot(state, lrc, now).PositionSeconds == 1.5, "Playing timeline interpolation failed.");
    Require(FoobarLyricsService.BuildSnapshot(state, lrc, now).ShowLyrics, "Playing track did not select the lyric page.");
    state.IsPaused = true;
    var paused = FoobarLyricsService.BuildSnapshot(state, lrc, now);
    Require(paused.Active && !paused.IsPlaying && paused.IsPaused && paused.PositionSeconds == 1, "Paused timeline advanced.");
    Require(!paused.ShowLyrics, "Paused track did not return to the main controls.");
    state.IsPaused = false;
    Require(FoobarLyricsService.BuildSnapshot(state, lrc, now).ShowLyrics, "Resuming did not restore the lyric page.");
    state.IsPaused = true;
    state.PositionSeconds = 2;
    Require(FoobarLyricsService.BuildSnapshot(state, lrc, now).CurrentLine == "two", "Seek did not immediately select the new line.");
    state.TimestampUtcMs = now.AddSeconds(-3.1).ToUnixTimeMilliseconds();
    Require(!FoobarLyricsService.BuildSnapshot(state, lrc, now).Active, "Stale heartbeat remained active.");
    state.TimestampUtcMs = now.ToUnixTimeMilliseconds(); state.IsPaused = false; state.IsPlaying = false;
    Require(!FoobarLyricsService.BuildSnapshot(state, lrc, now).Active, "Stopped player remained active.");
}
static void Service()
{
    string root = Path.Combine(Path.GetTempPath(), "TouchBarLyricsServiceChecks-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "state.json");
    try
    {
        var state = new FoobarPlaybackState { Version = 1, TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Title = "标题", IsPlaying = true, EmbeddedLyrics = "[00:00]第一句", DurationSeconds = 10 };
        File.WriteAllText(path, JsonSerializer.Serialize(state));
        using var service = new FoobarLyricsService(path);
        service.Start();
        Require(SpinWait.SpinUntil(() => service.Current.CurrentLine == "第一句", 2000), "Service did not load embedded lyrics.");
        File.WriteAllText(path, "{\"version\":");
        Thread.Sleep(350);
        Require(service.Current.Active && service.Current.CurrentLine == "第一句", "A torn JSON read discarded valid recent lyrics.");
        state.IsPlaying = false; state.TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        File.WriteAllText(path, JsonSerializer.Serialize(state));
        Require(SpinWait.SpinUntil(() => !service.Current.Active, 2000), "Stopped player did not clear active lyrics.");
    }
    finally { Directory.Delete(root, true); }
}

static void Refresh()
{
    string root = Path.Combine(Path.GetTempPath(), "TouchBarLyricsRefreshChecks-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string statePath = Path.Combine(root, "state.json"), lyricsPath = Path.Combine(root, "song.lrc");
    try
    {
        File.WriteAllText(lyricsPath, "[00:00]保持当前歌词");
        var state = new FoobarPlaybackState { Version = 1, Title = "song", IsPaused = true, Path = Path.Combine(root, "song.flac"), DurationSeconds = 100 };
        void Heartbeat() { state.TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); File.WriteAllText(statePath, JsonSerializer.Serialize(state)); }
        Heartbeat();
        using var service = new FoobarLyricsService(statePath);
        service.Start();
        Require(SpinWait.SpinUntil(() => service.Current.CurrentLine == "保持当前歌词", 2000), "Initial sidecar did not load.");
        File.WriteAllText(lyricsPath, ""); // The download component can truncate before rewriting.
        var deadline = DateTime.UtcNow.AddMilliseconds(5500);
        while (DateTime.UtcNow < deadline) { Heartbeat(); Thread.Sleep(100); }
        Require(service.Current.CurrentLine == "保持当前歌词", "A temporary empty file replaced the same track's valid lyrics.");
        state.Path = Path.Combine(root, "other.flac"); state.Title = "other"; Heartbeat();
        Require(SpinWait.SpinUntil(() => service.Current.Title == "other", 2000), "Track change was not received.");
        Require(service.Current.CurrentLine == "" && !service.Current.HasLyrics, "New track retained the previous track's lyrics.");
    }
    finally { Directory.Delete(root, true); }
}

static void PlaybackQueue()
{
    string root = Path.Combine(Path.GetTempPath(), "TouchBarPlaybackQueueChecks-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "commands.json");
    try
    {
        new FoobarPlaybackCommand("next", path).TouchDown();
        new FoobarPlaybackCommand("next", path).TouchDown();
        new FoobarPlaybackCommand("previous", path).TouchDown();
        using (var mailbox = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var entries = mailbox.RootElement.GetProperty("commands").EnumerateArray().ToArray();
            Require(entries.Select(item => item.GetProperty("action").GetString()).SequenceEqual(new[] { "next", "next", "previous" }), "Two taps before a bridge poll were overwritten or reordered.");
            Require(entries.Select(item => item.GetProperty("id").GetString()).Distinct().Count() == 3, "Taps must have independent command IDs.");
        }
        for (int i = 0; i < 40; i++) new FoobarPlaybackCommand("playpause", path).TouchDown();
        using (var mailbox = JsonDocument.Parse(File.ReadAllText(path)))
            Require(mailbox.RootElement.GetProperty("commands").GetArrayLength() == 16, "Mailbox grows without a bound.");
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = 1, commands = new[] { new { id = "expired", action = "next", timestampUtcMs = DateTimeOffset.UtcNow.AddSeconds(-4).ToUnixTimeMilliseconds() } } }));
        new FoobarPlaybackCommand("previous", path).TouchDown();
        using (var mailbox = JsonDocument.Parse(File.ReadAllText(path)))
            Require(mailbox.RootElement.GetProperty("commands").GetArrayLength() == 1, "Expired commands were retained.");
    }
    finally { Directory.Delete(root, true); }
}
