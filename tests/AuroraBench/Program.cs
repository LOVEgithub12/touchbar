using System.Diagnostics;
using SkiaSharp;
using WindowsTouchBar.Helpers;
using WindowsTouchBar.Lyrics;
using WindowsTouchBar.Views;

using var bitmap = new SKBitmap(2008, 60, SKColorType.Rgba8888, SKAlphaType.Opaque);
using var canvas = new SKCanvas(bitmap);
using var paint = new SKPaint { IsAntialias = true, TextSize = 34 };
var renderer = new AuroraRibbonRenderer();
renderer.SetEnergy(.8f, .6f, .4f, .7f);
const string shortText = "让晚风轻轻掠过你的身旁";
const string longText = "如果有一天我们再次走过那条熟悉的街道请记得那些曾经一起追逐的星光和温柔岁月永远都留在心里";
Bench("Aurora production at lyric area width", () => renderer.Draw(canvas, 928, 56, .016));
foreach (string text in new[] { shortText, longText })
{
    string label = text == shortText ? "short" : "long";
    Bench("Construct text layout " + label, () => { using var layout = new LyricsTextLayout(text); });
    using var layout = new LyricsTextLayout(text);
    Bench("Cached layout measure and draw " + label, () => { layout.MeasureText(paint); layout.MeasureBounds(paint); layout.Draw(canvas, 15, 40, paint); });
    var view = new LyricsView(2008, 60);
    view.SetSnapshot(new LyricsSnapshot(true, true, false, "song", "artist", text, "", .8, 40, 200, true));
    view.SetAudioEnergy(.8f, .6f, .4f, .7f);
    Bench("LyricsView effect on " + label, () => { view.Clear(canvas); view.Draw(canvas); });
    view.VisualizerEnabled = false;
    Bench("LyricsView effect off " + label, () => { view.Clear(canvas); view.Draw(canvas); });
}

void Bench(string label, Action draw)
{
    for (int i = 0; i < 50; i++) draw();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var samples = new double[500];
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < samples.Length; i++)
    {
        long start = Stopwatch.GetTimestamp();
        draw();
        samples[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Array.Sort(samples);
    Console.WriteLine($"{label}: mean={samples.Average():F3}ms p50={samples[250]:F3}ms p95={samples[475]:F3}ms max={samples[^1]:F3}ms allocated={allocated / 500}B/frame");
}
