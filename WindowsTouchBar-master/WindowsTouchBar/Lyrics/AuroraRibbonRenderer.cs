using SkiaSharp;

namespace WindowsTouchBar.Lyrics;

// Original small CPU renderer: the curves are a visual treatment of measured
// output energy, not a simulated spectrum. No audio samples are retained here.
internal sealed class AuroraRibbonRenderer
{
    private readonly float[] bands = new float[3];
    private readonly float[] targets = new float[3];
    private float level;
    private float targetLevel;
    private double phase;
    public bool HasEnergy => level > 0.003f || bands.Any(value => value > 0.003f);

    public void SetEnergy(float bass, float mid, float treble, float overall)
    {
        targets[0] = Clean(bass); targets[1] = Clean(mid); targets[2] = Clean(treble);
        targetLevel = Clean(overall);
    }

    private static float Clean(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    public void Reset()
    {
        Array.Clear(bands, 0, bands.Length);
        Array.Clear(targets, 0, targets.Length);
        level = targetLevel = 0;
    }

    public void Draw(SKCanvas canvas, float width, float height, double elapsedSeconds)
    {
        if (width <= 0 || height <= 0) return;
        const float ribbonWidthScale = 1.52f;
        const float opacityScale = 1.32f;
        double dt = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 0.1) : 0;
        level = Follow(level, targetLevel, dt);
        for (int i = 0; i < 3; i++) bands[i] = Follow(bands[i], targets[i], dt);
        if (!HasEnergy) return;
        phase += dt * (0.32 + 1.3 * level) * Math.Min(1, level * 10);

        canvas.Save();
        canvas.ClipRect(SKRect.Create(width, height));
        for (int band = 0; band < 3; band++)
        {
            float inputStrength = Math.Clamp(bands[band] * 0.78f + level * 0.22f, 0, 1);
            if (inputStrength <= 0.003f) continue;
            float strength = Math.Clamp(MathF.Pow(inputStrength, 0.88f) * 1.12f + 0.08f, 0, 1);
            using var path = new SKPath();
            using var crest = new SKPath();
            const int segments = 72;
            for (int edge = 0; edge < 2; edge++)
                for (int step = 0; step <= segments; step++)
                {
                    float u = (edge == 0 ? step : segments - step) / (float)segments;
                    float envelope = 0.35f + 0.65f * MathF.Pow(Math.Max(0, MathF.Sin(u * MathF.PI)), 0.55f);
                    double angle = u * (6.0 + band * 0.7) + phase * (band == 1 ? -0.85 : 1) + band * 2.1;
                    float wave = (float)(Math.Sin(angle) * 0.72 + Math.Sin(angle * 1.66 + band) * 0.28);
                    float center = height * (0.28f + band * 0.22f) + wave * height * (0.18f + strength * 0.16f) * envelope;
                    float halfWidth = (1.6f + strength * 6.2f) * envelope * ribbonWidthScale;
                    float x = u * width;
                    float y = center + (edge == 0 ? -halfWidth : halfWidth);
                    if (edge == 0 && step == 0) { path.MoveTo(x, y); crest.MoveTo(x, y); }
                    else path.LineTo(x, y);
                    if (edge == 0 && step > 0) crest.LineTo(x, y);
                }
            path.Close();
            float alphaValue = (110 + strength * 140) * MathF.Min(1, strength * 12) * opacityScale;
            byte alpha = (byte)Math.Clamp(alphaValue, 0, 235);
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
                new[] { new SKColor(165, 83, 255, alpha), new SKColor(65, 145, 255, alpha), new SKColor(49, 235, 214, alpha) },
                new[] { 0f, 0.56f, 1f }, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { IsAntialias = true, Shader = shader };
            // Broader layered outlines brighten the selected treatment without
            // adding a software Gaussian blur on the narrow USB panel.
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 8;
            paint.Color = SKColors.White.WithAlpha(24);
            canvas.DrawPath(path, paint);
            paint.StrokeWidth = 4;
            paint.Color = SKColors.White.WithAlpha(36);
            canvas.DrawPath(path, paint);
            paint.Style = SKPaintStyle.Fill;
            paint.Color = SKColors.White;
            canvas.DrawPath(path, paint);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1.1f;
            canvas.DrawPath(crest, paint);
        }
        // Keep the text band calm without introducing an opaque box.
        using var shade = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height),
            new[] { SKColors.Transparent, new SKColor(0, 0, 0, 55), new SKColor(0, 0, 0, 55), SKColors.Transparent },
            new[] { 0f, 0.32f, 0.72f, 1f }, SKShaderTileMode.Clamp);
        using var shadePaint = new SKPaint { Shader = shade };
        canvas.DrawRect(SKRect.Create(width, height), shadePaint);
        canvas.Restore();
    }

    private static float Follow(float value, float target, double dt) =>
        value + (target - value) * (float)(1 - Math.Exp(-dt / (target > value ? 0.085 : 0.32)));
}
