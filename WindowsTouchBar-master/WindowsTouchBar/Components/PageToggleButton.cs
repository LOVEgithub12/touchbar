using SkiaSharp;
using SkiaSharp.Elements;

namespace WindowsTouchBar.Components;

internal sealed class PageToggleButton : Button
{
    private readonly Text subtitle;

    public PageToggleButton(SKRect size, string title, string subtitle, string fontFamily = "Microsoft YaHei UI")
        : base(size)
    {
        bool isFunctionEntry = fontFamily != "Segoe MDL2 Assets";
        Text = title;
        FontFamily = fontFamily;
        FontSize = isFunctionEntry ? 29 : 24;
        TextCenterY = isFunctionEntry ? size.Height * 0.39f : null;
        ForeColor = SKColors.White;
        FillColor = new SKColor(38, 38, 40);
        DownFillColor = new SKColor(85, 85, 90);
        BorderWidth = 0;
        CornerRadius = new SKPoint(9, 9);

        this.subtitle = new Text(subtitle)
        {
            FontFamily = "Microsoft YaHei UI",
            FontSize = isFunctionEntry ? 16 : 12,
            ForeColor = SKColors.White,
            Center = new SKPoint(size.Width / 2, size.Height - (isFunctionEntry ? 10 : 12))
        };
    }

    public override void Draw(SKCanvas canvas)
    {
        if (!Visible) return;

        base.Draw(canvas);
        canvas.Save();
        canvas.Translate(X, Y);
        subtitle.Draw(canvas);
        canvas.Restore();
    }
}
