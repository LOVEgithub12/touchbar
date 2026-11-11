using SkiaSharp;

namespace WindowsTouchBar.Lyrics;

// Reuses source-video ribbon frames on a steady interpolated clock; audio only
// modulates the surrounding shapes and brightness. No samples are retained.
internal sealed class AuroraRibbonRenderer
{
    private const int ParticleCount = 50;
    private const int SideParticleCount = 32;
    private const int FrameCount = 101;
    private const int FrameColumns = 10;
    private const int FrameWidth = 900;
    private const int FrameHeight = 11;
    private const float FrameRate = 12;
    private static readonly Lazy<SKBitmap?> RibbonFrames = new(LoadRibbonFrames);
    private static readonly SKColor[] ParticleColors =
    {
        new(111, 242, 255), new(98, 170, 255), new(111, 255, 211),
        new(189, 130, 255), new(255, 133, 211), new(255, 213, 126)
    };
    private readonly float[] bands = new float[3];
    private readonly float[] targets = new float[3];
    private float level;
    private float targetLevel;
    private double ribbonClock;
    private float bassDrive;
    private float midDrive;
    private float trebleDrive;
    private float levelDrive;

    internal float CurrentRibbonHeightScale => 1.45f + bassDrive * 2.25f + midDrive * 0.95f;

    public bool HasEnergy => level > 0.0008f || bands.Any(value => value > 0.0008f);

    public void SetEnergy(float bass, float mid, float treble, float overall)
    {
        targets[0] = Clean(bass);
        targets[1] = Clean(mid);
        targets[2] = Clean(treble);
        targetLevel = Clean(overall);
    }

    public void Reset()
    {
        Array.Clear(bands, 0, bands.Length);
        Array.Clear(targets, 0, targets.Length);
        level = targetLevel = 0;
        ribbonClock = 0;
        bassDrive = midDrive = trebleDrive = levelDrive = 0;
    }

    public void Advance(double elapsedSeconds)
    {
        double dt = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 0.1) : 0;
        level = Follow(level, targetLevel, dt, 0.06, 0.28);
        for (int i = 0; i < bands.Length; i++)
        {
            double release = i switch { 0 => 0.32, 1 => 0.24, _ => 0.16 };
            bands[i] = Follow(bands[i], targets[i], dt, 0.06, release);
        }
        // AudioBandAnalyzer already provides a fast envelope; smooth its compressed
        // output once here so cascading band envelopes cannot peak after sound stops.
        bassDrive = Follow(bassDrive, EnergyDrive(targets[0], 0.48f), dt, 0.24, 0.55);
        midDrive = Follow(midDrive, EnergyDrive(targets[1], 0.48f), dt, 0.24, 0.6);
        trebleDrive = Follow(trebleDrive, EnergyDrive(targets[2], 0.48f), dt, 0.24, 0.65);
        levelDrive = Follow(levelDrive, EnergyDrive(targetLevel, 0.5f), dt, 0.24, 0.55);
        if (HasEnergy) ribbonClock += dt;
    }

    public void Draw(SKCanvas canvas, float width, float height, double elapsedSeconds)
    {
        if (width <= 0 || height <= 0) return;
        Advance(elapsedSeconds);
        if (!HasEnergy) return;

        var frames = RibbonFrames.Value;
        if (frames is null) return;

        float motion = Math.Clamp(levelDrive * 0.2f + bassDrive * 0.36f + midDrive * 0.23f + trebleDrive * 0.21f, 0, 1);
        float framePosition = (float)(ribbonClock * FrameRate % FrameCount);
        int frame = (int)framePosition;
        int nextFrame = (frame + 1) % FrameCount;
        byte frameMix = (byte)((framePosition - frame) * 255);
        const float profileSpan = 1.14f;
        float profileHeight = CurrentRibbonHeightScale;
        float baseline = height * 0.5f;
        float destinationWidth = width * profileSpan;
        float destinationHeight = FrameHeight * profileHeight;
        var destination = SKRect.Create(
            (width - destinationWidth) / 2,
            baseline - destinationHeight / 2,
            destinationWidth,
            destinationHeight);
        canvas.Save();
        canvas.ClipRect(SKRect.Create(width, height));
        canvas.SaveLayer(SKRect.Create(width, height), null);
        using var ribbonPaint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High,
            BlendMode = SKBlendMode.Plus,
            Color = SKColors.White.WithAlpha((byte)(255 - frameMix))
        };
        canvas.DrawBitmap(frames, GetFrameRect(frame), destination, ribbonPaint);
        if (frameMix > 0)
        {
            ribbonPaint.Color = SKColors.White.WithAlpha(frameMix);
            canvas.DrawBitmap(frames, GetFrameRect(nextFrame), destination, ribbonPaint);
        }
        float edgeFadeWidth = Math.Min(56, width * 0.1f);
        float edgeFadeStop = edgeFadeWidth / width;
        using (var edgeFade = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(width, 0),
            new[] { SKColors.Transparent, SKColors.White, SKColors.White, SKColors.Transparent },
            new[] { 0f, edgeFadeStop, 1 - edgeFadeStop, 1f }, SKShaderTileMode.Clamp))
        using (var edgeMask = new SKPaint { Shader = edgeFade, BlendMode = SKBlendMode.DstIn })
            canvas.DrawRect(SKRect.Create(width, height), edgeMask);
        canvas.Restore();
        DrawBassRibbon(canvas, width, height, baseline);
        DrawTallTrebleWaves(canvas, width, height, baseline);
        DrawParticles(canvas, width, height, motion);
        canvas.Restore();
    }

    private static SKRect GetFrameRect(int frame) => SKRect.Create(
        (frame % FrameColumns) * FrameWidth,
        (frame / FrameColumns) * FrameHeight,
        FrameWidth,
        FrameHeight);

    public void DrawSideDust(SKCanvas canvas, float totalWidth, float height, float centerLeft, float centerRight)
    {
        if (totalWidth <= 0 || height <= 0 || centerLeft <= 0 || centerRight >= totalWidth) return;

        canvas.Save();
        canvas.ClipRect(SKRect.Create(0, height - 8, totalWidth, 6));
        using var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var core = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (int side = 0; side < 2; side++)
        {
            float start = side == 0 ? 0 : centerRight;
            float end = side == 0 ? centerLeft : totalWidth;
            for (int index = 0; index < SideParticleCount; index++)
            {
                float u = (float)((index * 0.61803398875 + 0.11) % 1);
                float x = start + (end - start) * (0.035f + u * 0.93f);
                float innerDistance = side == 0 ? centerLeft - x : x - centerRight;
                float transition = SmoothStep(innerDistance / 115f);
                float yFraction = (float)((index * 0.754877666 + 0.23) % 1);
                float pulse = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin((float)(ribbonClock * (1.6 + index % 5 * 0.13) + index * 2.41)));
                float audioLift = 0.86f + levelDrive * 0.14f;
                float y = height - 3 - yFraction * 5;
                float radius = index % 7 == 0 ? 1.8f : 1.3f;
                SKColor color = ParticleColors[(index + side * 2) % ParticleColors.Length];
                byte alpha = (byte)Math.Clamp((72 + pulse * (92 + levelDrive * 32 + trebleDrive * 18)) * transition * audioLift, 0, 245);
                if (alpha < 2) continue;

                glow.Color = color.WithAlpha((byte)(alpha * 0.55f));
                core.Color = color.WithAlpha(alpha);
                canvas.DrawCircle(x, y, radius * 2.7f, glow);
                canvas.DrawCircle(x, y, radius, core);
            }
        }
        canvas.Restore();
    }

    private static SKBitmap? LoadRibbonFrames()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", "aurora-ribbon-frames.png");
        return File.Exists(path) ? SKBitmap.Decode(path) : null;
    }

    private void DrawTallTrebleWaves(SKCanvas canvas, float width, float height, float baseline)
    {
        float voiceDrive = Math.Max(trebleDrive, midDrive * 0.78f);
        if (voiceDrive < 0.025f) return;

        SKColor[] colors = { new(190, 226, 255), new(132, 232, 255), new(220, 207, 255) };
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        for (int strand = 0; strand < colors.Length; strand++)
        {
            var path = new SKPath();
            bool centerWave = strand == 1;
            float phase = (float)(ribbonClock * 1.05 + strand * 1.83);
            float centerY = centerWave ? baseline : height * (strand == 0 ? 0.19f : 0.81f);
            float amplitude = centerWave
                ? height * (0.14f + trebleDrive * 0.31f + midDrive * 0.16f)
                : height * (0.025f + voiceDrive * 0.035f);
            paint.StrokeWidth = centerWave ? 0.82f : 0.42f;
            for (int step = 0; step <= 96; step++)
            {
                float u = step / 96f;
                float x = u * width;
                float cycles = centerWave ? 1.22f : 1.7f + strand * 0.18f;
                float envelope = 0.78f + 0.22f * MathF.Sin(u * MathF.PI);
                float fine = MathF.Sin(u * MathF.Tau * cycles + phase) * amplitude * envelope;
                float shimmerDrive = trebleDrive + midDrive * 0.28f;
                float shimmer = MathF.Sin(u * MathF.Tau * 7.2f + phase * 1.4f) * shimmerDrive * (centerWave ? 0.9f : 0.25f);
                float y = Math.Clamp(centerY + fine + shimmer, 1.5f, height - 1.5f);
                if (step == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            byte alpha = centerWave ? (byte)(42 + 78 * voiceDrive) : (byte)(11 + 18 * voiceDrive);
            using var edgeFade = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width, 0),
                new[] { colors[strand].WithAlpha(0), colors[strand].WithAlpha(alpha),
                    colors[strand].WithAlpha(alpha), colors[strand].WithAlpha(0) },
                new[] { 0f, 0.045f, 0.955f, 1f }, SKShaderTileMode.Clamp);
            paint.Shader = edgeFade;
            canvas.DrawPath(path, paint);
            paint.Shader = null;
            path.Dispose();
        }
    }

    private void DrawBassRibbon(SKCanvas canvas, float width, float height, float baseline)
    {
        if (bassDrive < 0.01f && midDrive < 0.01f && levelDrive < 0.025f) return;

        const int segments = 72;
        float sway = bassDrive * height * 0.17f + midDrive * height * 0.13f + levelDrive * height * 0.035f;
        float halfWidth = 0.8f + bassDrive * height * 0.15f + midDrive * height * 0.075f + levelDrive * height * 0.02f;
        float phase = (float)(ribbonClock * 0.42);
        using var path = new SKPath();
        for (int step = 0; step <= segments; step++)
        {
            float u = step / (float)segments;
            float envelope = MathF.Pow(Math.Max(0, MathF.Sin(u * MathF.PI)), 0.72f);
            float x = u * width;
            const float cycles = 1.55f;
            float center = baseline + MathF.Sin(u * MathF.Tau * cycles + phase) * sway * envelope;
            float y = center - halfWidth * envelope;
            if (step == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        for (int step = segments; step >= 0; step--)
        {
            float u = step / (float)segments;
            float envelope = MathF.Pow(Math.Max(0, MathF.Sin(u * MathF.PI)), 0.72f);
            float x = u * width;
            const float cycles = 1.55f;
            float center = baseline + MathF.Sin(u * MathF.Tau * cycles + phase) * sway * envelope;
            path.LineTo(x, center + halfWidth * envelope);
        }
        path.Close();

        byte alpha = (byte)Math.Clamp(18 + levelDrive * 20 + bassDrive * 38 + midDrive * 24, 0, 104);
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(width, height),
            new[] { new SKColor(255, 93, 220, alpha), new SKColor(142, 109, 255, alpha), new SKColor(63, 219, 255, alpha) },
            new[] { 0f, 0.52f, 1f }, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawPath(path, paint);
    }

    private void DrawParticles(SKCanvas canvas, float width, float height, float motion)
    {
        using var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var core = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (int index = 0; index < ParticleCount; index++)
        {
            var position = GetParticlePosition(index, width, height);
            float u = position.X / width;
            float edgeFade = SmoothStep(Math.Min(u, 1 - u) / 0.06f);
            float twinkle = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin((float)(ribbonClock * (1.3 + index % 7 * 0.11) + index * 1.91)));
            float moteRadius = index % 7 == 0 ? 1.35f : 0.8f;
            float radius = moteRadius * (0.92f + twinkle * 0.16f);
            SKColor color = ParticleColors[index % ParticleColors.Length];
            byte alpha = (byte)Math.Clamp((0.22f + levelDrive * 0.3f + trebleDrive * 0.42f + motion * 0.15f) *
                (0.34f + twinkle * 0.66f) * edgeFade * 255, 0, 255);
            if (alpha == 0) continue;

            glow.Color = color.WithAlpha((byte)(alpha * 0.5f));
            core.Color = color.WithAlpha(alpha);
            canvas.DrawCircle(position.X, position.Y, radius * 2.6f, glow);
            canvas.DrawCircle(position.X, position.Y, radius, core);
        }
    }

    internal static SKPoint GetParticlePosition(int index, float width, float height)
    {
        float u = (float)((index * 0.61803398875 + 0.17) % 1);
        float vertical = (float)((index * 0.754877666 + 0.19) % 1) * 2 - 1;
        return new SKPoint(u * width, height * (0.08f + (vertical + 1) * 0.42f));
    }

    private static float Clean(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static float EnergyDrive(float value, float reference) =>
        MathF.Pow(Math.Clamp(value / reference, 0, 1), 0.6f);

    private static float SmoothStep(float value)
    {
        float t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static float Follow(float value, float target, double dt, double attack, double release) =>
        value + (target - value) * (float)(1 - Math.Exp(-dt / (target > value ? attack : release)));
}
