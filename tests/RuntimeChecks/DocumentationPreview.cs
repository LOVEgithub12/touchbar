using System.Diagnostics;
using SkiaSharp;
using WindowsTouchBar.Audio;
using WindowsTouchBar.Components;
using WindowsTouchBar.Lyrics;
using WindowsTouchBar.Views;

// Documentation uses the production views, renderer and analyzer. No USB, audio
// endpoint, player state or keyboard is opened. All input below is synthetic.
internal static class DocumentationPreview
{
    public static void Render(string output)
    {
        Directory.CreateDirectory(output);
        var media = new MediaFunctionsView(2008, 60);
        var functions = new DefaultFunctionsView(2008, 60);
        Save(media, Path.Combine(output, "media.png"));
        Save(functions, Path.Combine(output, "function-keys.png"));
        var aurora = new LyricsView(2008, 60) { Background = LyricsBackground.Aurora };
        var spectrum = new LyricsView(2008, 60) { Background = LyricsBackground.Spectrum };
        var off = new LyricsView(2008, 60) { Background = LyricsBackground.Off };
        var analyzer = new AudioSpectrumAnalyzer(48000, 2);
        string[] modes = { "aurora", "spectrum" };
        foreach (string mode in modes) Directory.CreateDirectory(Path.Combine(output, mode + "-frames"));
        var clock = Stopwatch.StartNew();
        long sample = 0;
        const int warmup = 60, total = 360;
        for (int frame = 0; frame < warmup + total; frame++)
        {
            double time = frame / 60.0;
            double pulse = Math.Exp(-(time * 2.4 % 1) * 7);
            // Several changing harmonics plus bright upper partials, never a recording.
            for (int n = 0; n < 800; n++, sample++)
            {
                double t = sample / 48000.0;
                double pcm = .14 * (.3 + .7 * pulse) * Math.Sin(2 * Math.PI * 82.4 * t);
                for (int harmonic = 1; harmonic <= 20; harmonic++)
                {
                    double hz = 164.8 * harmonic;
                    double envelope = .018 * (1 + .6 * Math.Sin(time * (2 + harmonic * .13) + harmonic));
                    pcm += envelope / Math.Sqrt(harmonic) * Math.Sin(2 * Math.PI * hz * t);
                }
                pcm += .018 * (.4 + .6 * Math.Sin(time * 8) * Math.Sin(time * 8)) *
                    (Math.Sin(2 * Math.PI * 5400 * t) + .5 * Math.Sin(2 * Math.PI * 9100 * t));
                analyzer.Add((float)pcm, 0);
                analyzer.Add((float)(pcm * .93), 1);
            }
            var energy = new AudioEnergyFrame((float)(.35 + pulse * .5),
                (float)(.4 + .23 * Math.Sin(time * 3)), (float)(.35 + .2 * Math.Sin(time * 8)),
                .58f, true, "Synthetic documentation input", sample, time) { Spectrum = analyzer.Latest };
            var snapshot = new LyricsSnapshot(true, true, false, "Touch Bar demo", "Synthetic input",
                "让旋律点亮这一片星光", "", .15 + .65 * ((frame - warmup + total) % total) / total,
                30 + time, 180, true);
            foreach (var view in new[] { aurora, spectrum, off })
            {
                view.SetSnapshot(snapshot);
                view.SetAudioEnergy(energy);
            }
            // LyricsView deliberately measures real elapsed time, as on the device.
            double remaining = frame / 60.0 - clock.Elapsed.TotalSeconds;
            if (remaining > 0) Thread.Sleep(TimeSpan.FromSeconds(remaining));
            for (int i = 0; i < 2; i++)
            {
                var view = i == 0 ? aurora : spectrum;
                using var bitmap = Draw(view);
                if (frame >= warmup && (frame - warmup) % 3 == 0)
                    Write(bitmap, Path.Combine(output, modes[i] + "-frames", $"frame-{(frame - warmup) / 3:D3}.png"));
                if (frame == warmup + 180) Write(bitmap, Path.Combine(output, "lyrics-" + modes[i] + ".png"));
            }
        }
        Save(off, Path.Combine(output, "lyrics-off.png"));
        foreach (var view in new TouchBarView[] { media, functions, aurora, spectrum, off })
            foreach (var button in view.Components.OfType<Button>()) button.Dispose();
        Console.WriteLine("Rendered production views with synthetic audio to " + output);
    }

    private static SKBitmap Draw(TouchBarView view)
    {
        var bitmap = new SKBitmap(view.Width, view.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        view.Clear(canvas);
        view.Draw(canvas);
        return bitmap;
    }

    private static void Save(TouchBarView view, string path)
    {
        using var bitmap = Draw(view);
        Write(bitmap, path);
    }

    private static void Write(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        png.SaveTo(stream);
    }
}
