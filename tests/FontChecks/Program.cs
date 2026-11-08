using SkiaSharp;
using WindowsTouchBar.Helpers;

var samples = new[]
{
    ("Korean", "늦은 밤 비가 내려와 너 없는 하루는 길어 빌어"),
    ("Japanese", "君の声が聞こえる ありがとう、さようなら。"),
    ("Chinese", "也许我错过幸福的时分"),
    ("Latin and Korean", "I'm so sorry but I love you 다 거짓말"),
    ("Mixed CJK", "中文歌词 日本語の歌詞 한국어 가사 English"),
    ("Japanese punctuation", "「逢いたい」って、言えなくて… ずっと。")
};
int failed = 0;
foreach (var (name, text) in samples)
{
    using var layout = new LyricsTextLayout(text);
    using var paint = new SKPaint { TextSize = 34, IsAntialias = true };
    using var bitmap = new SKBitmap(1300, 60);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Black);
    paint.Color = SKColors.White;
    layout.Draw(canvas, 10, 40, paint);
    bool valid = !layout.HasMissingGlyphs && layout.MeasureText(paint) > 0 && !layout.MeasureBounds(paint).IsEmpty &&
        Math.Abs(layout.MeasurePrefix(text.Length, paint) - layout.MeasureText(paint)) < 0.001;
    if (!valid) { failed++; Console.WriteLine($"FAIL: {name}: {layout.PrimaryFamily} contains missing glyphs or invalid geometry"); }
    else Console.WriteLine($"PASS: {name}: {layout.PrimaryFamily}, all glyphs present and geometry consistent");
}
Console.WriteLine($"{samples.Length - failed}/{samples.Length} installed-font checks passed. No fonts were installed or downloaded.");
return failed == 0 ? 0 : 1;
