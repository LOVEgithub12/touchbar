using SkiaSharp;

namespace WindowsTouchBar.Components;

// Native-resolution silhouettes strengthen volume and lighting controls.
// Transport controls keep their original font icons through Button.Draw,
// including playback-state updates, on both layouts.
internal sealed class MediaIconButton : Button
{
    public float IconSize { get; set; } = 43.2f;

    public MediaIconButton(SKRect size) : base(size) { }

    public override void Draw(SKCanvas canvas)
    {
        if (!Visible) return;
        if (Text is not ("\uE74F" or "\uE993" or "\uE995" or "\uE706" or "\uED3A" or "\uED39"))
        {
            base.Draw(canvas);
            return;
        }

        canvas.Save();
        canvas.ClipRect(SKRect.Create(X, Y, Width, Height));
        using var background = new SKPaint { IsAntialias = true, Color = FillColor };
        canvas.DrawRoundRect(SKRect.Create(X, Y, Width, Height), CornerRadius.X, CornerRadius.Y, background);
        canvas.Translate(MathF.Round(X + Width / 2), MathF.Round(Y + Height / 2));
        canvas.Scale(IconSize / 36);
        using var ink = new SKPaint
        {
            IsAntialias = true, Color = ForeColor ?? SKColors.White,
            StrokeWidth = 3.2f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        };
        switch (Text)
        {
            case "\uE74F":
            case "\uE993":
            case "\uE995":
                DrawSpeaker(canvas, ink, Text, FillColor);
                break;
            case "\uE706":
                canvas.DrawCircle(0, 0, 6.5f, ink);
                ink.Style = SKPaintStyle.Stroke;
                for (int ray = 0; ray < 8; ray++)
                {
                    double angle = ray * Math.PI / 4;
                    canvas.DrawLine((float)Math.Cos(angle) * 11, (float)Math.Sin(angle) * 11,
                        (float)Math.Cos(angle) * 16, (float)Math.Sin(angle) * 16, ink);
                }
                break;
            case "\uED3A":
            case "\uED39":
                DrawKeyboardLight(canvas, ink, Text == "\uED39");
                break;
        }
        canvas.Restore();
    }

    private static void DrawSpeaker(SKCanvas canvas, SKPaint ink, string icon, SKColor backgroundColor)
    {
        using var body = new SKPath();
        body.MoveTo(-17, -6); body.LineTo(-10, -6); body.LineTo(-1, -14);
        body.LineTo(-1, 14); body.LineTo(-10, 6); body.LineTo(-17, 6); body.Close();
        canvas.DrawPath(body, ink);
        ink.Style = SKPaintStyle.Stroke;
        if (icon == "\uE74F")
        {
            // A clearly visible mute slash, with a gap where it crosses the body.
            using var gap = new SKPaint { IsAntialias = true, Color = backgroundColor, Style = SKPaintStyle.Stroke, StrokeWidth = 6.5f, StrokeCap = SKStrokeCap.Round };
            canvas.DrawLine(-17, -17, 16, 17, gap);
            canvas.DrawLine(-17, -17, 16, 17, ink);
            return;
        }
        using var inner = new SKPath();
        inner.MoveTo(5, -7); inner.CubicTo(10, -3, 10, 3, 5, 7);
        canvas.DrawPath(inner, ink);
        if (icon == "\uE995")
        {
            using var outer = new SKPath();
            outer.MoveTo(10, -13); outer.CubicTo(19, -6, 19, 6, 10, 13);
            canvas.DrawPath(outer, ink);
        }
    }

    private static void DrawKeyboardLight(SKCanvas canvas, SKPaint ink, bool brighter)
    {
        canvas.DrawRoundRect(SKRect.Create(-17, 8, 34, 5), 1.5f, 1.5f, ink);
        ink.Style = SKPaintStyle.Stroke;
        using var dome = new SKPath();
        dome.MoveTo(-6, 4); dome.CubicTo(-6, -4, 6, -4, 6, 4);
        canvas.DrawPath(dome, ink);
        float outer = brighter ? 16 : 12;
        for (int ray = 0; ray < 5; ray++)
        {
            double angle = Math.PI + ray * Math.PI / 4;
            canvas.DrawLine((float)Math.Cos(angle) * 9, 2 + (float)Math.Sin(angle) * 9,
                (float)Math.Cos(angle) * outer, 2 + (float)Math.Sin(angle) * outer, ink);
        }
    }
}
