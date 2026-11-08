using SkiaSharp;
using SkiaSharp.Elements;

var text = new Text("确定") { FontSize = 25, ForeColor = SKColors.White };
Console.WriteLine($"Elements default family: {text.FontFamily ?? "<null>"}");
foreach (var family in new[] { text.FontFamily, "Microsoft YaHei UI" })
{
    using var face = SKTypeface.FromFamilyName(family);
    using var paint = new SKPaint { Typeface = face, TextSize = 25 };
    Console.WriteLine($"Requested={family ?? "<null>"}; actual={face.FamilyName}; glyphs 确定取消应用={string.Join(',', paint.GetGlyphs("确定取消应用"))}");
}

using var bitmap = new SKBitmap(1100, 130, SKColorType.Rgba8888, SKAlphaType.Opaque);
using var canvas = new SKCanvas(bitmap);
canvas.Clear(SKColors.Black);
foreach (var row in new[] { 0, 1 })
{
    foreach (var (label, index) in new[] { "确定", "取消", "应用" }.Select((s, i) => (s, i)))
    {
        var item = new Text(label) { FontSize = 25, ForeColor = SKColors.White };
        if (row == 1) item.FontFamily = "Microsoft YaHei UI";
        item.Center = new SKPoint(180 + 360 * index, 30 + row * 65);
        item.Draw(canvas);
    }
}
using var image = SKImage.FromBitmap(bitmap);
using var png = image.Encode(SKEncodedImageFormat.Png, 100);
var path = Path.Combine(AppContext.BaseDirectory, "default-vs-yahei.png");
using var stream = File.Create(path);
png.SaveTo(stream);
Console.WriteLine(path);
