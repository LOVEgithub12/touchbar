using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;
using WindowsInput.Native;
using WindowsTouchBar.Commands;
using WindowsTouchBar.Components;
using WindowsTouchBar.Device.Hid;
using WindowsTouchBar.Events;
using WindowsTouchBar.Audio;
using WindowsTouchBar.Interop;
using WindowsTouchBar.Models;
using WindowsTouchBar.Lyrics;
using WindowsTouchBar.Views;

if (args.Length == 2 && args[0] == "--render-docs")
{
    DocumentationPreview.Render(Path.GetFullPath(args[1]));
    return 0;
}

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

if (args.Length == 2 && args[0] == "--preview-aurora-animation")
{
    RenderAuroraAnimation(Path.GetFullPath(args[1]));
    Console.WriteLine($"Rendered animated aurora frames to {Path.GetFullPath(args[1])}");
    return 0;
}

var checks = new (string Name, Action Run)[]
{
    ("Volume taps and repeats use system commands without key-down or key-up", CheckSystemVolumeCommands),
    ("Only standalone volume keys bypass the foreground keyboard path", CheckVolumeRouting),
    ("Untouched buttons do not release unrelated keys", CheckUntouchedRelease),
    ("Sliding into a button cannot start a second action", CheckSlideDoesNotPress),
    ("Player handoff releases the previous command", CheckPlayerHandoff),
    ("F-page toggle responds only to a completed touch", CheckFunctionPageToggle),
    ("Repeated press/release leaves no late key-downs", CheckRepeatedPress),
    ("Disposing a pressed button cancels repeats and releases its command", CheckDisposedPress),
    ("Missing image keeps the same visible text button", CheckMissingImage),
    ("Chinese dialog labels render as distinct characters", CheckChineseLabels),
    ("Zero Y range maps to a finite touch midpoint", CheckZeroYRange),
    ("Karaoke colors the completed prefix and keeps remaining text white", CheckKaraokeColors),
    ("Lyric taps cycle Aurora, Spectrum and Off; drags and stray releases do not", CheckBackgroundCycle),
    ("The live spectrum separates tones, keeps stereo power and clears during silence", CheckSpectrum),
    ("Spectrum particles remain scattered at different heights after the fill falls", CheckSpectrumParticleTrail),
    ("Aurora responds to supplied energy, decays in silence, and stays inside its area", CheckAuroraEnergy),
    ("Aurora separates bass and treble response; foreground selection preserves playback", CheckBandResponseAndViewPriority),
    ("Loopback gaps ease out instead of clearing audio energy", CheckAudioEnergyGapDecay),
    ("Quiet sustained vocals keep a visible center wave", CheckQuietVoiceVisibility),
    ("Rapid level changes produce a smooth visual envelope", CheckVisualEnvelopeSmoothing),
    ("Backlight transition mode is retried as full/on", CheckBacklightTransitionMode),
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

static void CheckSystemVolumeCommands()
{
    foreach (var key in new[] { VirtualKeyCode.VOLUME_DOWN, VirtualKeyCode.VOLUME_UP, VirtualKeyCode.VOLUME_MUTE })
    {
        var sent = new List<VirtualKeyCode>();
        var command = new KeyPressCommand(key, sent.Add);
        command.TouchUp();
        Require(sent.Count == 0, "Untouched release sent a volume command.");
        command.TouchDown();
        command.TouchUp();
        command.TouchUp();
        Require(sent.SequenceEqual(new[] { key }), "A volume tap did not send exactly one system command.");
        command.TouchDown();
        command.TouchDown();
        command.TouchDown();
        command.TouchUp();
        Require(sent.Count == 4 && sent.All(sentKey => sentKey == key),
            "Volume repeat ticks were lost, duplicated, or changed into another action.");
    }
    var muted = new List<VirtualKeyCode>();
    using var button = new Button(SKRect.Create(112, 56))
    {
        Command = new KeyPressCommand(VirtualKeyCode.VOLUME_MUTE, muted.Add)
    };
    button.TouchDown(default);
    button.TouchDown(default);
    button.TouchUp(default);
    button.TouchUp(default);
    Require(muted.Count == 1, "The mute button toggled more than once for one completed touch.");
}

static void CheckVolumeRouting()
{
    foreach (var key in new[] { VirtualKeyCode.VOLUME_DOWN, VirtualKeyCode.VOLUME_UP, VirtualKeyCode.VOLUME_MUTE })
    {
        var command = new KeyPressCommand(key) { ModifierKeyCodes = Array.Empty<VirtualKeyCode>() };
        Require(command.IsSystemVolumeCommand, "An unmodified volume key did not use the system path.");
        command.ModifierKeyCodes = new[] { VirtualKeyCode.CONTROL };
        Require(!command.IsSystemVolumeCommand, "A modified shortcut was redirected to system volume.");
        command.ModifierKeyCodes = null;
        command.KeyCodes = new[] { key, VirtualKeyCode.VK_A };
        Require(!command.IsSystemVolumeCommand, "A multi-key shortcut was redirected to system volume.");
    }
    foreach (var key in new[] { VirtualKeyCode.F1, VirtualKeyCode.VK_A, VirtualKeyCode.MEDIA_PLAY_PAUSE })
        Require(!new KeyPressCommand(key).IsSystemVolumeCommand, "A non-volume key changed its keyboard path.");
    Require(!new KeyPressCommand().IsSystemVolumeCommand, "An empty shortcut became a volume action.");

    foreach (var view in new TouchBarView[] { new MediaFunctionsView(2008, 60), new LyricsView(2008, 60) })
    {
        var buttons = view.Components.OfType<Button>().ToArray();
        var volumeButtons = buttons.Where(button => button.Command is KeyPressCommand command && command.IsSystemVolumeCommand).ToArray();
        Require(volumeButtons.Length == 3, "A media or lyrics volume button still uses foreground input.");
        Require(!volumeButtons[0].TouchDownInterval && volumeButtons.Skip(1).All(button => button.TouchDownInterval),
            "Mute must remain a single action; volume up/down must retain repeat.");
        foreach (var button in buttons) button.Dispose();
    }
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

static void CheckFunctionPageToggle()
{
    var media = new MediaFunctionsView(2008, 60);
    var entry = media.Components.OfType<PageToggleButton>().Single();
    Require(entry.X == 20 && entry.Width == 100, "The main media layout did not apply its selected 20px right shift.");
    var firstBrightnessButton = media.Components.OfType<MediaIconButton>().First();
    Require(firstBrightnessButton.X - (entry.X + entry.Width) == 20,
        "The F-page entry and screen-brightness controls are not separated by 20px.");

    bool entered = false;
    media.FunctionsPageRequested += () => entered = true;
    int centerX = (int)(entry.X + entry.Width / 2);
    media.ReceiveTouch(new TouchEventArgs(centerX, 30, TouchEventType.TouchDown));
    Require(!entered, "The F page changed before the entry touch was released.");
    media.ReceiveTouch(new TouchEventArgs(centerX, 30, TouchEventType.TouchUp));
    Require(entered, "Releasing the media-bar entry did not request the F page.");

    var functions = new DefaultFunctionsView(2008, 60);
    var buttons = functions.Components.OfType<Button>().ToArray();
    Require(buttons.Length == 13 && buttons[0] is PageToggleButton && buttons[0].Width == 100,
        "The F page is missing its 100px left-side return control.");
    Require(buttons.Skip(1).All(button => button.Width == 150) && buttons[1].X == 108,
        "The F1-F12 keys do not use the selected 150px layout after the 8px gap.");
    for (int index = 2; index < buttons.Length; index++)
        Require(buttons[index].X - (buttons[index - 1].X + buttons[index - 1].Width) == 8,
            "F1-F12 keys are not spaced 8px apart.");
    Require(buttons[^1].X + buttons[^1].Width <= 2008,
        "The F12 key extends beyond the panel.");

    bool returned = false;
    functions.MediaPageRequested += () => returned = true;
    var back = buttons[0];
    centerX = (int)(back.X + back.Width / 2);
    functions.ReceiveTouch(new TouchEventArgs(centerX, 30, TouchEventType.TouchDown));
    functions.ReceiveTouch(new TouchEventArgs(centerX, 30, TouchEventType.TouchUp));
    Require(returned, "Releasing the left-side return control did not request the media page.");

    foreach (var button in media.Components.OfType<Button>()) button.Dispose();
    foreach (var button in buttons) button.Dispose();
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

static void CheckDisposedPress()
{
    foreach (bool repeats in new[] { false, true })
    {
        var command = new CountingCommand();
        using var button = new Button(SKRect.Create(150, 56))
        {
            Command = command,
            TouchDownInterval = repeats
        };
        int completedTaps = 0;
        button.OnTouchUp += (_, _) => completedTaps++;
        button.TouchDown(default);
        Require(SpinWait.SpinUntil(() => command.DownCount >= (repeats ? 2 : 1), 2000),
            "The command did not start before its button was disposed.");
        button.Dispose();
        int downsAtDispose = command.DownCount;
        button.TouchDown(default);
        button.TouchUp(default);
        button.Dispose();
        Thread.Sleep(180);
        Require(command.DownCount == downsAtDispose && command.UpCount == 1 && !command.KeyHeld,
            "A disposed button repeated, restarted, or failed to release its held command.");
        Require(completedTaps == 0, "Disposing a pressed button fired a completed-tap action.");
    }
    var untouchedCommand = new CountingCommand();
    using var untouched = new Button(SKRect.Create(150, 56)) { Command = untouchedCommand };
    untouched.Dispose();
    Require(untouchedCommand.UpCount == 0, "Disposing an untouched button released an unrelated key.");
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
    for (int x = 616; x < 1588; x++)
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
    Require(Math.Abs((pixels.Min(point => point.X) + pixels.Max(point => point.X)) / 2 - 1102) < 4,
        "Lyrics are not visually centered in the expanded lyric area.");
    foreach (var button in view.Components.OfType<Button>()) button.Dispose();
}

static void CheckBackgroundCycle()
{
    var view = new LyricsView(2008, 60);
    int requests = 0;
    view.BackgroundChangeRequested += () => { requests++; view.CycleBackground(); };
    void Touch(int x, TouchEventType type) => view.ReceiveTouch(new TouchEventArgs(x, 28, type));
    void Tap() { Touch(1102, TouchEventType.TouchDown); Touch(1104, TouchEventType.TouchUp); }
    Touch(1102, TouchEventType.TouchUp);
    Touch(1102, TouchEventType.TouchDown);
    Touch(1180, TouchEventType.TouchMove);
    Touch(1102, TouchEventType.TouchUp);
    Touch(1102, TouchEventType.TouchDown);
    Touch(1605, TouchEventType.TouchUp);
    Require(requests == 0 && view.Background == LyricsBackground.Aurora, "A drag or stray release changed the background.");

    string directory = Path.Combine(Path.GetTempPath(), "TouchBarBackground-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string file = Path.Combine(directory, "visualizer-preference.json");
    Action<string> fail = message => throw new InvalidOperationException(message);
    try
    {
        File.WriteAllText(file, "{\"Enabled\":false}");
        Require(VisualizerPreference.Load(fail, file) == LyricsBackground.Off, "Legacy disabled setting was lost.");
        foreach (var expected in new[] { LyricsBackground.Spectrum, LyricsBackground.Off, LyricsBackground.Aurora })
        {
            Tap();
            Require(view.Background == expected && view.VisualizerEnabled == (expected != LyricsBackground.Off),
                $"Expected {expected} after tapping lyrics, got {view.Background}.");
            VisualizerPreference.Save(view.Background, fail, file);
            Require(VisualizerPreference.Load(fail, file) == expected, "Background mode was not retained on reload.");
        }
        Require(requests == 3, "Each tap should dispatch exactly one change.");
    }
    finally
    {
        File.Delete(file);
        Directory.Delete(directory);
        foreach (var button in view.Components.OfType<Button>()) button.Dispose();
    }
}

static void CheckSpectrum()
{
    const int rate = 48000;
    var analyzer = new AudioSpectrumAnalyzer(rate, 2);
    void Feed(int frames, params (double Frequency, double Amplitude)[] tones)
    {
        for (int i = 0; i < frames; i++)
        {
            float sample = (float)tones.Sum(t => t.Amplitude * Math.Sin(2 * Math.PI * t.Frequency * i / rate));
            analyzer.Add(sample, 0);
            analyzer.Add(-sample, 1);
        }
    }
    Feed(8192, (117.1875, .13), (6000, .036));
    float[] values = analyzer.Latest.ToArray();
    Require(values.Length == 512 && values.All(float.IsFinite), "No valid spectrum was produced.");
    Require(values[95..118].Max() > .60f && values[420..440].Max() > .60f && values[190..330].Max() < .06f,
        "The spectrum did not isolate bass and treble from phase-opposed channels.");

    var renderer = new SpectrumBackgroundRenderer();
    using var bitmap = new SKBitmap(972, 56);
    using var canvas = new SKCanvas(bitmap);
    void Draw(ReadOnlySpan<float> bins, double elapsed)
    {
        canvas.Clear(SKColors.Black);
        renderer.Draw(canvas, bitmap.Width, bitmap.Height, bins, 1, elapsed);
    }
    for (int i = 0; i < 24; i++) Draw(values, 1.0 / 60);
    Require(renderer.HasEnergy && bitmap.GetPixel(204, 42).Green > 150, "Bass did not paint a bright filled peak.");
    Feed(8192);
    Require(analyzer.Latest.ToArray().All(value => value == 0), "Silence retained FFT peaks.");
    Draw(analyzer.Latest.Span, .016);
    Require(renderer.HasEnergy, "Spectrum cut straight to black without a release.");
    for (int i = 0; i < 24; i++) Draw(analyzer.Latest.Span, .1);
    Require(!renderer.HasEnergy && bitmap.Pixels.All(p => p == SKColors.Black), "The spectrum kept moving in silence.");

    var view = new LyricsView(2008, 60) { Background = LyricsBackground.Spectrum };
    view.SetSnapshot(new LyricsSnapshot(true, true, false, "song", "artist", "让晚风轻轻掠过你的身旁", "", .43, 40, 200, true));
    view.SetAudioEnergy(new AudioEnergyFrame(.7f, .45f, .3f, .7f, true, "test", 8192, 0) { Spectrum = values });
    using var strip = new SKBitmap(2008, 60);
    using var display = new SKCanvas(strip);
    for (int i = 0; i < 12; i++)
    {
        view.Clear(display);
        view.Draw(display);
        Thread.Sleep(12);
    }
    var spectrumPixel = strip.GetPixel(820, 42);
    Require(spectrumPixel.Green > 150, "Spectrum was not composited into the lyric page.");
    view.Background = LyricsBackground.Off;
    view.Clear(display);
    view.Draw(display);
    Require(strip.GetPixel(820, 42) == SKColors.Black, "Switching off left background pixels on the lyric page.");
    foreach (var button in view.Components.OfType<Button>()) button.Dispose();
}

static void CheckSpectrumParticleTrail()
{
    var renderer = new SpectrumBackgroundRenderer();
    using var bitmap = new SKBitmap(972, 56);
    using var canvas = new SKCanvas(bitmap);
    var music = Enumerable.Range(0, AudioSpectrumAnalyzer.BinCount)
        .Select(i => .22f + .41f * MathF.Pow(MathF.Abs(MathF.Sin(i * .037f)), 4)).ToArray();
    void Draw(ReadOnlySpan<float> bins)
    {
        canvas.Clear(SKColors.Black);
        renderer.Draw(canvas, bitmap.Width, bitmap.Height, bins, 1, 1.0 / 60);
    }
    for (int i = 0; i < 24; i++) Draw(music);
    for (int i = 0; i < 15; i++) Draw(ReadOnlySpan<float>.Empty);

    // By now the fill is near the floor. Count separate bright areas well above it.
    var litCells = new HashSet<(int X, int Y)>();
    for (int y = 3; y < 44; y++)
        for (int x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).Green >= 105) litCells.Add((x / 8, y / 5));
    Require(litCells.Count > 50 && litCells.Select(cell => cell.Y).Distinct().Count() >= 4,
        "Visible residue did not persist across different heights after the spectrum fell.");
    for (int i = 0; i < 48; i++) Draw(ReadOnlySpan<float>.Empty);
    Require(!renderer.HasEnergy && bitmap.Pixels.All(p => p == SKColors.Black),
        "Particles remained visible after the audio stopped.");
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

static void CheckBandResponseAndViewPriority()
{
    static float RenderRibbonScale(float bass, float mid, float treble, float overall)
    {
        var renderer = new AuroraRibbonRenderer();
        using var bitmap = new SKBitmap(944, 72, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Translate(8, 8);
        renderer.SetEnergy(bass, mid, treble, overall);
        for (int frame = 0; frame < 60; frame++)
        {
            canvas.Clear(SKColors.Black);
            renderer.Draw(canvas, 928, 56, 1.0 / 60);
        }
        return renderer.CurrentRibbonHeightScale;
    }

    float bassScale = RenderRibbonScale(0.88f, 0.18f, 0.08f, 0.92f);
    float trebleScale = RenderRibbonScale(0.03f, 0.18f, 0.88f, 0.92f);
    Require(bassScale > trebleScale + 1.5f,
        $"Bass must produce broader ribbon movement than treble (bass {bassScale:F2}, treble {trebleScale:F2}).");

    var easing = new AuroraRibbonRenderer();
    using (var bitmap = new SKBitmap(944, 72, SKColorType.Rgba8888, SKAlphaType.Opaque))
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Translate(8, 8);
        easing.SetEnergy(0.9f, 0.2f, 0.1f, 0.9f);
        canvas.Clear(SKColors.Black);
        easing.Draw(canvas, 928, 56, 1.0 / 60);
        float firstStep = easing.CurrentRibbonHeightScale;
        float prior = firstStep;
        for (int frame = 0; frame < 45; frame++)
        {
            canvas.Clear(SKColors.Black);
            easing.Draw(canvas, 928, 56, 1.0 / 60);
            float current = easing.CurrentRibbonHeightScale;
            Require(current + 0.001f >= prior, "The bass envelope jumped backward while the input was steady.");
            prior = current;
        }
        Require(firstStep < prior - 0.5f, "The bass envelope snapped to its target instead of easing in.");

        float peak = prior;
        easing.SetEnergy(0, 0, 0, 0);
        for (int frame = 0; frame < 12; frame++)
        {
            canvas.Clear(SKColors.Black);
            easing.Draw(canvas, 928, 56, 1.0 / 60);
            float current = easing.CurrentRibbonHeightScale;
            Require(current <= prior + 0.001f, "The bass envelope rose while its input was silent.");
            prior = current;
        }
        Require(prior > 1.8f && prior < peak,
            "The bass envelope dropped to its minimum within a few display frames.");
        for (int frame = 0; frame < 240; frame++)
        {
            canvas.Clear(SKColors.Black);
            easing.Draw(canvas, 928, 56, 1.0 / 60);
        }
        Require(easing.CurrentRibbonHeightScale < 1.5f,
            "The bass envelope did not settle after sustained silence.");
    }
    var starA = AuroraRibbonRenderer.GetParticlePosition(17, 928, 56);
    var starB = AuroraRibbonRenderer.GetParticlePosition(17, 928, 56);
    Require(starA == starB, "A star particle's position changes between animation frames.");

    Require(ForegroundViewPriority.Select(false, false, true, true, true, false) == ForegroundViewSelection.Lyrics,
        "A browser stole the view while foobar was actively playing.");
    Require(ForegroundViewPriority.Select(false, false, true, false, true, false) == ForegroundViewSelection.ConfiguredApplication,
        "A configured browser view did not take over after playback paused.");
    Require(ForegroundViewPriority.Select(false, true, true, false, true, false) == ForegroundViewSelection.ExplorerDialog,
        "A configured application displaced an Explorer dialog.");
    Require(ForegroundViewPriority.Select(true, true, true, true, true, false) == ForegroundViewSelection.FunctionPage,
        "The explicit F-key page lost priority to a foreground application.");
}

static void CheckAudioEnergyGapDecay()
{
    var sample = new AudioEnergyFrame(0.9f, 0.7f, 0.5f, 0.8f, true, "device", 48000, 10);
    var grace = sample.FadeForAge(0.15);
    Require(grace == sample, "A brief capture gap changed the held audio frame inside the grace period.");

    var first = sample.FadeForAge(0.35);
    var second = sample.FadeForAge(0.65);
    var third = sample.FadeForAge(0.95);
    Require(first.Bass < grace.Bass && first.Bass > second.Bass && second.Bass > third.Bass && third.Bass > 0,
        "Stale audio energy did not decay continuously through a capture gap.");
    Require(sample.FadeForAge(3).Level < 0.003f,
        "A long capture gap kept the visualization active indefinitely.");
}

static void CheckQuietVoiceVisibility()
{
    const int sampleRate = 48000;
    const int blockFrames = 480;
    var analyzer = new AudioBandAnalyzer(sampleRate, 2);
    (float Bass, float Mid, float Treble, float Level) voice = default;
    int sampleIndex = 0;
    for (int block = 0; block < 120; block++)
    {
        for (int frame = 0; frame < blockFrames; frame++, sampleIndex++)
        {
            float sample = MathF.Sin(MathF.Tau * 440 * sampleIndex / sampleRate) * 0.0015f;
            analyzer.Add(sample, 0);
            analyzer.Add(sample, 1);
        }
        voice = analyzer.ReadAndReset();
    }
    Require(voice.Mid > 0.05f && voice.Level > 0.05f,
        $"A sustained quiet vocal was compressed below a useful range (mid {voice.Mid:F4}, level {voice.Level:F4}).");

    var renderer = new AuroraRibbonRenderer();
    using var bitmap = new SKBitmap(944, 72, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using var canvas = new SKCanvas(bitmap);
    canvas.Translate(8, 8);
    for (int frame = 0; frame < 300; frame++)
    {
        renderer.SetEnergy(voice.Bass, voice.Mid, voice.Treble, voice.Level);
        canvas.Clear(SKColors.Black);
        renderer.Draw(canvas, 928, 56, 1.0 / 60);
        if (frame >= 90)
            Require(renderer.HasEnergy && renderer.CurrentRibbonHeightScale > 1.65f,
            "A continuous quiet vocal lost its center-wave amplitude during playback.");
    }

    renderer.SetEnergy(0, 0, 0, 0);
    for (int frame = 0; frame < 300; frame++)
    {
        canvas.Clear(SKColors.Black);
        renderer.Draw(canvas, 928, 56, 1.0 / 60);
    }
    Require(!renderer.HasEnergy && bitmap.Pixels.All(pixel => pixel == SKColors.Black),
        "The quiet-vocal visibility floor kept animating after the signal became silent.");
}

static void CheckVisualEnvelopeSmoothing()
{
    var renderer = new AuroraRibbonRenderer();
    using var bitmap = new SKBitmap(944, 72, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using var canvas = new SKCanvas(bitmap);
    canvas.Translate(8, 8);
    float previous = renderer.CurrentRibbonHeightScale;
    float maximumStep = 0;
    float minimum = float.MaxValue;
    float maximum = float.MinValue;
    for (int frame = 0; frame < 180; frame++)
    {
        bool strong = (frame / 3) % 2 == 0;
        renderer.SetEnergy(
            strong ? 0.88f : 0.12f,
            strong ? 0.45f : 0.18f,
            strong ? 0.62f : 0.16f,
            strong ? 0.78f : 0.24f);
        canvas.Clear(SKColors.Black);
        renderer.Draw(canvas, 928, 56, 1.0 / 30);
        float current = renderer.CurrentRibbonHeightScale;
        maximumStep = Math.Max(maximumStep, Math.Abs(current - previous));
        minimum = Math.Min(minimum, current);
        maximum = Math.Max(maximum, current);
        previous = current;
    }
    Require(maximumStep < 0.45f,
        $"Rapid audio changes caused a visible frame jump of {maximumStep:F2} scale units.");
    Require(maximum - minimum > 0.25f,
        "The visual envelope was smoothed so heavily that it stopped following the music.");

    renderer.SetEnergy(0, 0, 0, 0);
    for (int frame = 0; frame < 240; frame++)
    {
        canvas.Clear(SKColors.Black);
        renderer.Draw(canvas, 928, 56, 1.0 / 30);
    }
    Require(!renderer.HasEnergy && bitmap.Pixels.All(pixel => pixel == SKColors.Black),
        "The smoother did not release fully after sustained silence.");
}

static void CheckBacklightTransitionMode()
{
    Require(TouchBarBacklightService.IsSupportedMode(2) && TouchBarBacklightService.IsTransitionMode(2),
        "The in-progress full/dim mode was rejected as an invalid HID response.");
    Require(TouchBarBacklightService.RequiresFullOn(2) && TouchBarBacklightService.RequiresFullOn(3) &&
        TouchBarBacklightService.RequiresFullOn(4) && !TouchBarBacklightService.RequiresFullOn(1),
        "A transition/dim/off backlight state did not request full/on, or full/on was rewritten.");
    Require(!TouchBarBacklightService.IsSupportedMode(0) && !TouchBarBacklightService.IsSupportedMode(5),
        "An unknown backlight state was accepted.");
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
            var lyricButton = view.Components.OfType<Button>().Single(button => button.X == 616 && button.Width == width - 1036);
            var mediaBackgrounds = view.Components.OfType<Button>().Where(button => !ReferenceEquals(button, lyricButton)).ToArray();
            Require(mediaBackgrounds.All(button => button.BackgroundBottomInset == 6 && button.Height == 56),
                "Lyrics media backgrounds did not keep their top edge while shortening only the bottom.");
            // Let the production attack easing settle, without changing its clock.
            for (int frame = 0; frame < 8; frame++) { using var warmup = RenderScene(view); Thread.Sleep(20); }
            using var actual = RenderScene(view);
            for (int x = 0; x < width; x++)
                if (x < 616 || x >= width - 420)
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
            Require(Enumerable.Range(616, width - 1036).Any(x => Enumerable.Range(0, 60).Any(y => HasRibbon(x, y))),
                "Full-width center ribbon is missing while the visualizer is active.");
            double EdgeColorEnergy(int startX, int endX)
            {
                double total = 0;
                int count = 0;
                for (int x = startX; x < endX; x++)
                    for (int y = 0; y < 60; y++)
                    {
                        var pixel = actual.GetPixel(x, y);
                        total += Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)) -
                            Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
                        count++;
                    }
                return count == 0 ? 0 : total / count;
            }
            double leftEdgeEnergy = EdgeColorEnergy(616, 640);
            double leftInteriorEnergy = EdgeColorEnergy(676, 700);
            double rightEdgeEnergy = EdgeColorEnergy(width - 444, width - 420);
            double rightInteriorEnergy = EdgeColorEnergy(width - 484, width - 460);
            Require(leftEdgeEnergy < leftInteriorEnergy * 0.8,
                $"Left ribbon edge was not softly blended ({leftEdgeEnergy:F2} vs {leftInteriorEnergy:F2}).");
            Require(rightEdgeEnergy < rightInteriorEnergy * 0.8,
                $"Right ribbon edge was not softly blended ({rightEdgeEnergy:F2} vs {rightInteriorEnergy:F2}).");
            int sideDustPixels = 0;
            int brightSideDustPixels = 0;
            for (int x = 0; x < width; x++)
                if (x < 616 || x >= width - 420)
                    for (int y = 0; y < 60; y++)
                        if (HasRibbon(x, y))
                        {
                            Require(y < 58, $"Side dust crossed the media-button lower edge at {x},{y}.");
                            sideDustPixels++;
                            var pixel = actual.GetPixel(x, y);
                            if (Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)) >= 140)
                                brightSideDustPixels++;
                        }
            Require(sideDustPixels > 20, "The left/right star-dust transition is missing while the visualizer is active.");
            Require(brightSideDustPixels > 5, "The left/right star dust is too dim to read as visible pulses.");
            int whitePixels = 0;
            for (int x = 616; x < width - 420; x++)
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
            Require(mediaBackgrounds.All(button => button.BackgroundBottomInset == 0),
                "Hiding the visualizer left the shortened media-button background in place.");
            using var hidden = RenderScene(view);
            for (int x = 0; x < width; x++)
                if (x < 616 || x >= width - 420)
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

static void RenderAuroraAnimation(string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    var view = new LyricsView(2008, 60);
    view.SetSnapshot(new LyricsSnapshot(true, true, false, "夜色与远方", "演示歌手",
        "让晚风轻轻掠过你的身旁", "沿着星光去看远方", 0.45, 30, 180, true));

    const int frameCount = 72;
    const double framesPerSecond = 24;
    const double beatPeriod = 60.0 / 96;
    for (int frame = 0; frame < frameCount; frame++)
    {
        double time = frame / framesPerSecond;
        double beat = time / beatPeriod;
        int beatIndex = (int)Math.Floor(beat);
        double beatPhase = beat - beatIndex;
        double accent = new[] { 1.0, 0.38, 0.82, 0.5 }[beatIndex % 4];
        double kick = accent * Math.Exp(-beatPhase * 7.2);
        bool backbeat = beatIndex % 4 is 1 or 3;
        double snare = (backbeat ? 0.72 : 0.12) * Math.Exp(-beatPhase * 5.8);
        double halfBeat = time / (beatPeriod * 0.5);
        double halfBeatPhase = halfBeat - Math.Floor(halfBeat);
        double hiHat = (halfBeat % 2 < 1 ? 0.34 : 0.5) * Math.Exp(-halfBeatPhase * 13);
        float bass = (float)Math.Clamp(0.055 + kick * 0.88, 0, 1);
        float mid = (float)Math.Clamp(0.08 + kick * 0.2 + snare * 0.54 + 0.08 * (0.5 + 0.5 * Math.Sin(time * 1.2)), 0, 1);
        float treble = (float)Math.Clamp(0.045 + hiHat * 0.58 + snare * 0.12, 0, 1);
        float level = (float)Math.Clamp(0.07 + bass * 0.44 + mid * 0.34 + treble * 0.16, 0, 1);
        view.SetAudioEnergy(
            bass, mid, treble, level);

        using var bitmap = RenderScene(view);
        string path = Path.Combine(outputDirectory, $"frame-{frame:D3}.png");
        using (var stream = File.Create(path))
            if (!bitmap.Encode(stream, SKEncodedImageFormat.Png, 100))
                throw new IOException($"Could not encode preview frame {frame}.");
        Thread.Sleep(1000 / (int)framesPerSecond);
    }

    foreach (var button in view.Components.OfType<Button>()) button.Dispose();
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
        Require(lyricButtons[4].X == 616 && lyricButtons[4].Width == width - 1036 &&
            lyricButtons[4].X + lyricButtons[4].Width == lyricButtons[5].X,
            "Lyrics must fill the center gap to the mute control's left edge.");
        var mediaControls = paused.Components.OfType<Button>().ToArray();
        Require(mediaControls.Length == 11 && mediaControls[0] is PageToggleButton && mediaControls[0].X == 20 && mediaControls[0].Width == 100 &&
            mediaControls.Skip(1).All(button => button.Width == 150),
            "Pause did not restore the full-size main control layout at its selected 20px offset.");
        if (width == 2008)
            Require(Math.Abs(width - (mediaControls[^1].X + mediaControls[^1].Width) - 47) < 1,
                "The selected main-bar shift did not balance the left and right outer margins as previewed.");
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
