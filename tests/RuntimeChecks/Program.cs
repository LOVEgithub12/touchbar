using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;
using WindowsTouchBar.Commands;
using WindowsTouchBar.Components;
using WindowsTouchBar.Device.Hid;
using WindowsTouchBar.Models;
using WindowsTouchBar.Lyrics;
using WindowsTouchBar.Views;

// Explicit, read-only live diagnostic: no USB is opened and no player command is sent.
if (args.Length == 3 && args[0] == "--preview-foobar")
{
    using var service = new FoobarLyricsService(Path.GetFullPath(args[1]), Console.WriteLine);
    service.Start();
    if (!SpinWait.SpinUntil(() => service.Current.Active, 4000))
        throw new InvalidOperationException("No fresh active foobar2000 playback state was received.");
    var snapshot = service.Current;
    TouchBarView preview;
    if (snapshot.ShowLyrics) { var lyrics = new LyricsView(2008, 60); lyrics.SetSnapshot(snapshot); preview = lyrics; }
    else { var main = new MediaFunctionsView(2008, 60); main.SetSnapshot(snapshot); preview = main; }
    RenderAndCheck(preview, Path.GetFullPath(args[2]));
    Console.WriteLine(JsonSerializer.Serialize(snapshot));
    return 0;
}

var checks = new (string Name, Action Run)[]
{
    ("Untouched buttons do not release unrelated keys", CheckUntouchedRelease),
    ("Sliding into a button cannot start a second action", CheckSlideDoesNotPress),
    ("Player handoff releases the previous command", CheckPlayerHandoff),
    ("Repeated press/release leaves no late key-downs", CheckRepeatedPress),
    ("Missing image keeps the same visible text button", CheckMissingImage),
    ("Chinese dialog labels render as distinct characters", CheckChineseLabels),
    ("Zero Y range maps to a finite touch midpoint", CheckZeroYRange),
    ("Karaoke colors the completed prefix and keeps remaining text white", CheckKaraokeColors),
    ("Aurora responds to supplied energy, decays in silence, and stays inside its area", CheckAuroraEnergy),
    ("Lyric-area aurora stays contained and clips long lyrics", CheckLongLyrics),
    ("Configured layouts render and fit both panel widths", CheckLayouts)
};

int failures = 0;
foreach (var check in checks)
{
    try
    {
        check.Run();
        Console.WriteLine($"PASS: {check.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {check.Name}{Environment.NewLine}{error}");
    }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed. No USB device was opened and no keyboard input was sent.");
return failures == 0 ? 0 : 1;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void CheckUntouchedRelease()
{
    var command = new CountingCommand();
    using var button = new Button(SKRect.Create(150, 56)) { Command = command };
    button.TouchUp(default);
    Require(command.UpCount == 0, "An untouched button sent KeyUp.");
    button.TouchDown(default);
    button.TouchDown(default);
    button.TouchUp(default);
    button.TouchUp(default);
    Require(command.DownCount == 1 && command.UpCount == 1, "A normal press was duplicated.");
}

static void CheckSlideDoesNotPress()
{
    var command = new CountingCommand();
    using var button = new Button(SKRect.Create(144, 56)) { Command = command };
    button.TouchMove(default);
    button.TouchUp(default);
    Require(command.DownCount == 0 && command.UpCount == 0, "Sliding into a button fired an accidental press.");
    button.TouchDown(default);
    button.TouchMove(default);
    button.TouchUp(default);
    Require(command.DownCount == 1 && command.UpCount == 1, "An ordinary tap/slide/release was duplicated.");
}

static void CheckPlayerHandoff()
{
    var view = new MediaFunctionsView(2008, 60);
    view.SetSnapshot(LyricsSnapshot.Inactive);
    var play = view.Components.OfType<Button>().ElementAt(5);
    var command = new CountingCommand();
    play.Command = command;
    play.TouchDown(default);
    view.SetSnapshot(new LyricsSnapshot(true, true, false, "song", "artist", "line", "next", 0, 0, 10, true));
    Require(command.DownCount == 1 && command.UpCount == 1 && !command.KeyHeld,
        "The old media key remained held when foobar became the playback target.");
    foreach (var button in view.Components.OfType<Button>()) button.Dispose();
}

static void CheckRepeatedPress()
{
    ThreadPool.GetMinThreads(out int workerThreads, out int completionThreads);
    ThreadPool.SetMinThreads(Math.Max(4, workerThreads), completionThreads);
    var command = new CountingCommand();
    using var button = new Button(SKRect.Create(150, 56))
    {
        Command = command,
        TouchDownInterval = true
    };
    button.TouchDown(default);
    Require(SpinWait.SpinUntil(() => command.DownCount >= 2, 2000), "Held button did not repeat.");
    button.TouchUp(default);
    int downsAfterRelease = command.DownCount;
    Thread.Sleep(120);
    Require(command.DownCount == downsAfterRelease && !command.KeyHeld, "KeyDown occurred after releasing a held button.");

    // Alternate short and immediate presses so old repeat workers overlap later presses.
    for (int i = 0; i < 150; i++)
    {
        button.TouchDown(default);
        if (i % 3 == 0) Thread.Sleep(1);
        button.TouchUp(default);
    }
    downsAfterRelease = command.DownCount;
    Thread.Sleep(180);
    Require(command.DownCount == downsAfterRelease && !command.KeyHeld, "An old repeat worker sent a late KeyDown.");
    Require(command.UpCount == 151, "Release events did not match the completed presses.");
}

static void CheckMissingImage()
{
    using var expected = new Button(SKRect.Create(250, 56)) { Text = "Search" };
    using var missing = new Button(SKRect.Create(250, 56))
    {
        ImagePath = "Resources/this-image-does-not-exist.png",
        Text = "Search"
    };
    using var expectedBitmap = RenderButton(expected);
    using var missingBitmap = RenderButton(missing);
    Require(expectedBitmap.Pixels.SequenceEqual(missingBitmap.Pixels), "Missing image changed the text fallback or rendering.");
    Require(missingBitmap.Pixels.Distinct().Count() > 3, "Text fallback produced a blank button.");
}

static SKBitmap RenderButton(Button button)
{
    var bitmap = new SKBitmap(250, 60, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Black);
    button.Draw(canvas);
    return bitmap;
}

static void CheckChineseLabels()
{
    var labels = new[] { "确定", "取消", "应用" };
    var rendered = new List<SKColor[]>();
    foreach (string label in labels)
    {
        using var button = new Button(SKRect.Create(250, 56)) { Text = label };
        using var bitmap = RenderButton(button);
        Require(bitmap.Pixels.Any(p => p.Red > 200 && p.Green > 200 && p.Blue > 200),
            $"The Chinese label {label} has no visible text.");
        Require(rendered.All(previous => !previous.SequenceEqual(bitmap.Pixels)),
            "Different two-character Chinese labels rendered identically as missing-glyph boxes.");
        rendered.Add(bitmap.Pixels);
    }
}

static void CheckZeroYRange()
{
    var report = new TouchReport(50, 100, 0, 0, true);
    Require(double.IsFinite(report.GetYInPercentage()) && report.GetYInPercentage() == 0.5,
        "A zero Y range did not map to the midpoint.");
    Require(report.GetXInPercentage() == 0.5, "X scaling changed unexpectedly.");
    var ordinary = new TouchReport(25, 100, 15, 60, true);
    Require(ordinary.GetYInPercentage() == 0.25, "A nonzero Y range no longer scales normally.");
}

static void CheckKaraokeColors()
{
    var view = new LyricsView(2008, 60) { VisualizerEnabled = false };
    var frame = new LyricsSnapshot(true, true, false, "song", "artist", "也许我错过幸福的时分", "not displayed", 0.45, 40, 200, true);
    view.SetSnapshot(frame);
    using var bitmap = new SKBitmap(2008, 60, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using var canvas = new SKCanvas(bitmap);
    view.Clear(canvas); view.Draw(canvas);
    var colored = new List<SKPoint>();
    var white = new List<SKPoint>();
    for (int x = 640; x < 1568; x++)
        for (int y = 0; y < 60; y++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Blue > 120 && pixel.Blue > pixel.Red + 40) colored.Add(new SKPoint(x, y));
            if (pixel.Red > 230 && pixel.Green > 230 && pixel.Blue > 230) white.Add(new SKPoint(x, y));
        }
    Require(colored.Count > 30 && white.Count > 30, "Completed and remaining glyph colors are not both visible.");
    Require(colored.Max(point => point.X) <= white.Min(point => point.X) + 1, "Highlight is not confined to the sung prefix.");
    var pixels = colored.Concat(white).ToArray();
    Require(pixels.Max(point => point.Y) - pixels.Min(point => point.Y) <= 35,
        "Lyrics occupy more than a single line.");
    Require(Math.Abs((pixels.Min(point => point.X) + pixels.Max(point => point.X)) / 2 - 1104) < 4,
        "Lyrics are not visually centered in the expanded lyric area.");
    foreach (var button in view.Components.OfType<Button>()) button.Dispose();
}

static void CheckAuroraEnergy()
{
    var aurora = new AuroraRibbonRenderer();
    using var bitmap = new SKBitmap(944, 72, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using var canvas = new SKCanvas(bitmap);
    canvas.Translate(8, 8);
    void Draw(double dt = 1.0 / 60)
    {
        canvas.Clear(SKColors.Black);
        aurora.Draw(canvas, 928, 56, dt);
    }
    Draw();
    Require(!aurora.HasEnergy && bitmap.Pixels.All(pixel => pixel == SKColors.Black), "Silence generated simulated audio activity.");
    aurora.SetEnergy(0.8f, 0.55f, 0.35f, 0.72f);
    for (int frame = 0; frame < 60; frame++) Draw();
    Require(aurora.HasEnergy && bitmap.Pixels.Count(pixel => pixel.Blue > 20) > 500,
        "Measured energy did not produce visible ribbons.");
    var firstFrame = bitmap.Pixels;
    Draw(0.1);
    Require(!firstFrame.SequenceEqual(bitmap.Pixels), "The active ribbon frame did not evolve.");
    for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            if (x < 8 || x >= 936 || y < 8 || y >= 64)
                Require(bitmap.GetPixel(x, y) == SKColors.Black, "Aurora escaped its clip rectangle.");
    aurora.SetEnergy(0, 0, 0, 0);
    for (int frame = 0; frame < 240; frame++) Draw();
    Require(!aurora.HasEnergy && bitmap.Pixels.All(pixel => pixel == SKColors.Black),
        "Background kept animating after four seconds of silence.");
    aurora.Reset();
    aurora.SetEnergy(float.NaN, float.PositiveInfinity, -1, float.NegativeInfinity);
    Draw(double.NaN);
    Require(!aurora.HasEnergy && bitmap.Pixels.All(pixel => pixel == SKColors.Black),
        "Invalid energy or frame time created nonfinite/visible geometry.");
    aurora.SetEnergy(100, 100, 100, 100);
    Draw(0.1);
    Require(aurora.HasEnergy, "Out-of-range positive audio values were not safely clamped.");
    aurora.Reset();
    Draw();
    Require(!aurora.HasEnergy && bitmap.Pixels.All(pixel => pixel == SKColors.Black), "Reset left stale ribbons visible.");
}

static void CheckLongLyrics()
{
    string artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts");
    Directory.CreateDirectory(artifacts);
    foreach (int width in new[] { 2008, 2170 })
    {
        var baseline = new LyricsView(width, 60) { VisualizerEnabled = false };
        baseline.SetSnapshot(new LyricsSnapshot(true, true, false, "song", "artist", "", "", 0, 40, 200, true));
        using var reference = RenderScene(baseline);
        foreach (var sample in new[] {
            (Name: "short", Line: "让晚风轻轻掠过你的身旁", Progress: 0.45),
            (Name: "long-chinese", Line: "如果有一天我们再次走过那条熟悉的街道请记得那些曾经一起追逐的星光和温柔岁月永远都留在心里", Progress: 0.8),
            (Name: "long-english", Line: "When the night is falling I will follow every distant star until the morning brings your gentle voice back home again", Progress: 0.8) })
        {
            var view = new LyricsView(width, 60);
            view.SetSnapshot(new LyricsSnapshot(true, true, false, "song", "artist", sample.Line, "", sample.Progress, 40, 200, true));
            view.SetAudioEnergy(0.82f, 0.62f, 0.43f, 0.78f);
            // Let the production attack easing settle, without changing its clock.
            for (int frame = 0; frame < 8; frame++) { using var warmup = RenderScene(view); Thread.Sleep(20); }
            using var actual = RenderScene(view);
            for (int x = 0; x < width; x++)
                if (x < 640 || x >= width - 440)
                    for (int y = 0; y < 60; y++)
                    {
                        var before = reference.GetPixel(x, y);
                        var after = actual.GetPixel(x, y);
                        bool wasWhite = before.Red == 255 && before.Green == 255 && before.Blue == 255;
                        bool nowWhite = after.Red == 255 && after.Green == 255 && after.Blue == 255;
                        Require(wasWhite == nowWhite,
                            $"{sample.Name} moved or covered a white control glyph at {x},{y}.");
                    }
            bool HasRibbon(int x, int y)
            {
                var p = actual.GetPixel(x, y);
                return Math.Max(p.Red, Math.Max(p.Green, p.Blue)) - Math.Min(p.Red, Math.Min(p.Green, p.Blue)) > 10;
            }
            Require(Enumerable.Range(640, width - 1080).Any(x => Enumerable.Range(0, 60).Any(y => HasRibbon(x, y))),
                "Lyric-area ribbon is missing while the visualizer is active.");
            for (int x = 0; x < width; x++)
                if (x < 640 || x >= width - 440)
                    for (int y = 0; y < 60; y++)
                        Require(!HasRibbon(x, y), $"Visualizer escaped the lyric area at {x},{y}.");
            int whitePixels = 0;
            for (int x = 640; x < width - 440; x++)
                for (int y = 0; y < 60; y++)
                {
                    var pixel = actual.GetPixel(x, y);
                    if (pixel.Red > 230 && pixel.Green > 230 && pixel.Blue > 230) whitePixels++;
                }
            Require(whitePixels > 30, $"{sample.Name} hid all unsung text at the requested progress.");
            using var image = SKImage.FromBitmap(actual);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(artifacts, $"aurora-{sample.Name}-{width}.png"));
            png.SaveTo(stream);
            view.VisualizerEnabled = false;
            using var hidden = RenderScene(view);
            for (int x = 0; x < width; x++)
                if (x < 640 || x >= width - 440)
                    for (int y = 0; y < 60; y++)
                        Require(hidden.GetPixel(x, y) == reference.GetPixel(x, y),
                            $"Hidden visualizer or long lyric altered controls at {x},{y}.");
            var withoutEffect = new LyricsView(width, 60) { VisualizerEnabled = false };
            withoutEffect.SetSnapshot(new LyricsSnapshot(true, true, false, "song", "artist", sample.Line, "", sample.Progress, 40, 200, true));
            using var clean = RenderScene(withoutEffect);
            Require(hidden.Pixels.SequenceEqual(clean.Pixels),
                $"Hiding the visualizer left background or shadow residue for {sample.Name}.");
            foreach (var button in withoutEffect.Components.OfType<Button>()) button.Dispose();
            foreach (var button in view.Components.OfType<Button>()) button.Dispose();
        }
        foreach (var button in baseline.Components.OfType<Button>()) button.Dispose();
    }
}

static SKBitmap RenderScene(TouchBarView view)
{
    var bitmap = new SKBitmap(view.Width, view.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using var canvas = new SKCanvas(bitmap);
    view.Clear(canvas); view.Draw(canvas);
    return bitmap;
}

static void CheckLayouts()
{
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    options.Converters.Add(new JsonStringEnumConverter());
    var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings.json")), options)
        ?? throw new InvalidOperationException("Settings could not be loaded.");
    string artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts");
    Directory.CreateDirectory(artifacts);

    foreach (int width in new[] { 2008, 2170 })
    {
        foreach (var config in settings.Views ?? Enumerable.Empty<Settings.View>())
        {
            TouchBarView view = config.Type switch
            {
                ViewType.Function => new DefaultFunctionsView(width, 60),
                ViewType.Media => new MediaFunctionsView(width, 60),
                ViewType.Custom => new CustomFunctionsView(width, 60),
                _ => throw new InvalidOperationException($"Unexpected configured view: {config.Type}")
            };
            view.ConfigureView(config);
            string label = config.Application ?? config.Type.ToString().ToLowerInvariant();
            RenderAndCheck(view, Path.Combine(artifacts, $"{label}-{width}.png"));
        }

        var dialog = new ExplorerDialogView(width, 60);
        dialog.CreateDialog(new[]
        {
            new ForegroundButton { Title = "确定", Handle = IntPtr.Zero },
            new ForegroundButton { Title = "取消", Handle = IntPtr.Zero },
            new ForegroundButton { Title = "应用", Handle = IntPtr.Zero }
        });
        RenderAndCheck(dialog, Path.Combine(artifacts, $"dialog-{width}.png"));

        var lyrics = new LyricsView(width, 60);
        lyrics.SetSnapshot(new LyricsSnapshot(true, true, false, "夜色与远方", "演示歌手",
            "让晚风轻轻掠过你的身旁", "沿着星光去看远方", 0.45, 30, 180, true));
        RenderAndCheck(lyrics, Path.Combine(artifacts, $"lyrics-{width}.png"));
        foreach (var sample in new[] {
            (Name: "korean", Line: "거짓말 I'm so sorry but I love you"),
            (Name: "japanese", Line: "君の名前を呼んでいる ありがとう"),
            (Name: "mixed", Line: "你好 안녕하세요 こんにちは Hello") })
        {
            var multilingual = new LyricsView(width, 60);
            multilingual.SetSnapshot(new LyricsSnapshot(true, true, false, "song", "artist", sample.Line, "", 0.45, 30, 180, true));
            RenderAndCheck(multilingual, Path.Combine(artifacts, $"lyrics-{sample.Name}-{width}.png"));
        }
        var paused = new MediaFunctionsView(width, 60);
        paused.SetSnapshot(new LyricsSnapshot(true, false, true, "Song title", "Artist",
            "A long lyric with enough words to test that the text stays within the center area and never covers playback controls or brightness buttons", "下一句歌词", 0.6, 45, 180, true));
        var lyricButtons = lyrics.Components.OfType<Button>().ToArray();
        var mainButtons = paused.Components.OfType<Button>().Where(button => button.Command is not null).ToArray();
        Require(lyricButtons.Length == 8 && !lyricButtons.Any(button => button.Text is "\uE706" or "\uED39" or "\uED3A"),
            "Lyrics page still contains screen brightness or keyboard backlight controls.");
        Require(lyricButtons[4].X == 640 && lyricButtons[4].Width == width - 1080 &&
            lyricButtons[4].X + lyricButtons[4].Width == lyricButtons[5].X - 20,
            "Expanded lyrics must keep their left edge at 640 and stop 20px before mute.");
        Require(paused.Components.OfType<Button>().Count() == 10 && paused.Components.OfType<Button>().All(button => button.Width == 150),
            "Pause did not restore the full-size main control layout.");
        Require(mainButtons.Take(3).All(button => button.Width >= 140 && button.Command is FoobarPlaybackCommand),
            "Main-page playback controls are too small or no longer target the paused player.");
        RenderAndCheck(paused, Path.Combine(artifacts, $"media-paused-{width}.png"));
    }
    Console.WriteLine($"Rendered layout artifacts: {artifacts}");
}

static void RenderAndCheck(TouchBarView view, string output)
{
    var buttons = view.Components.OfType<Button>().ToArray();
    Require(buttons.Length > 0, $"{view.Type} has no buttons.");
    foreach (var button in buttons)
    {
        Require(button.Width > 0 && button.Height > 0 && button.X >= 0 && button.Y >= 0 &&
            button.X + button.Width <= view.Width + 0.1 && button.Y + button.Height <= view.Height + 0.1,
            $"{view.Type} button {button.Text} falls outside the panel: {button.X},{button.Y},{button.Width},{button.Height}.");
    }
    using var bitmap = new SKBitmap(view.Width, view.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using var canvas = new SKCanvas(bitmap);
    view.Clear(canvas);
    view.Draw(canvas);
    Require(bitmap.Pixels.Distinct().Count() > 3, $"{view.Type} did not render visible controls.");
    using var image = SKImage.FromBitmap(bitmap);
    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(output);
    png.SaveTo(stream);
    foreach (var button in buttons) button.Dispose();
}

sealed class CountingCommand : ITouchCommand
{
    private int downs;
    private int ups;
    private int keyHeld;
    public int DownCount => Volatile.Read(ref downs);
    public int UpCount => Volatile.Read(ref ups);
    public bool KeyHeld => Volatile.Read(ref keyHeld) != 0;
    public void TouchDown()
    {
        Interlocked.Increment(ref downs);
        Volatile.Write(ref keyHeld, 1);
    }
    public void TouchUp()
    {
        Interlocked.Increment(ref ups);
        Volatile.Write(ref keyHeld, 0);
    }
}
