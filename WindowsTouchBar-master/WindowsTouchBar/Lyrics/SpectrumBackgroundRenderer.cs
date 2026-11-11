using SkiaSharp;
using WindowsTouchBar.Audio;

namespace WindowsTouchBar.Lyrics;

internal sealed class SpectrumBackgroundRenderer
{
    private const int Bins = AudioSpectrumAnalyzer.BinCount;
    private const int HistoryLength = 11;
    private const double StepSeconds = 1.0 / 60;
    private static readonly float Rise = 1 - MathF.Exp(-(float)StepSeconds / .026f);
    private static readonly float[] Fall = Enumerable.Range(0, Bins).Select(i =>
    {
        float hz = 32 * MathF.Pow(16500f / 32, (i + .5f) / Bins);
        float mix = Math.Clamp(MathF.Log2(hz / 220) / MathF.Log2(470f / 220), 0, 1);
        return 1 - MathF.Exp(-(float)StepSeconds / (.075f + .025f * (1 - mix)));
    }).ToArray();
    private readonly float[] heights = new float[Bins];
    private readonly float[] history = new float[HistoryLength * Bins];
    private readonly Particle[] particles = new Particle[2048];
    private int historyIndex, particleCount;
    private float strongest;
    private double remainder;
    private uint seed = 0x741cdb3d;
    private static readonly SKColor[] FillColors =
    {
        new(114, 239, 218), new(164, 244, 228), new(204, 250, 242),
        new(241, 237, 255), new(255, 248, 255)
    };
    private static readonly float[] FillStops = { 0, .32f, .6f, .83f, 1 };
    public bool HasEnergy { get; private set; }

    public void Reset()
    {
        Array.Clear(heights);
        Array.Clear(history);
        historyIndex = particleCount = 0;
        strongest = 0;
        remainder = 0;
        seed = 0x741cdb3d;
        HasEnergy = false;
    }

    private float RandomUnit()
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return (seed >> 8) * (1f / 16777216f);
    }

    private static float Unit(float x) => Math.Clamp(x, 0, 1);
    private static float Mix(float a, float b, float x) => a + (b - a) * x;

    private float HeightAt(float x)
    {
        float pos = x * (Bins - 1);
        int i = (int)pos;
        return Mix(heights[i], heights[Math.Min(i + 1, Bins - 1)], pos - i);
    }

    private void Advance(ReadOnlySpan<float> bins, float scale)
    {
        for (int i = 0; i < particleCount;)
        {
            ref Particle p = ref particles[i];
            p.Age += (float)StepSeconds;
            if (p.Age < p.Life) i++;
            else p = particles[--particleCount];
        }
        bool hasBins = bins.Length == heights.Length && float.IsFinite(scale);
        strongest = 0;
        float peakInput = 0;
        int current = historyIndex * Bins;
        for (int i = 0; i < Bins; i++)
        {
            float target = hasBins && float.IsFinite(bins[i]) ? Unit(bins[i] * scale) : 0;
            float value = heights[i];
            value += (target - value) * (target > value ? Rise : Fall[i]);
            history[current + i] = heights[i] = value;
            strongest = Math.Max(strongest, value);
            peakInput = Math.Max(peakInput, target);
        }
        int prior = (historyIndex + HistoryLength - 4) % HistoryLength * Bins;
        int earlier = (historyIndex + HistoryLength - 10) % HistoryLength * Bins;
        historyIndex = (historyIndex + 1) % HistoryLength;
        // Allow existing particles to finish in silence; do not seed another cloud.
        if (peakInput < .027f) return;

        const int zones = 88;
        for (int zone = 0; zone < zones; zone++)
        {
            int bin = (int)MathF.Round((zone + .5f) / zones * (Bins - 1));
            float now = heights[bin], previous = history[prior + bin], old = history[earlier + bin];
            float local = Math.Max(now, Math.Max(previous * .91f, old * .8f));
            if (local < .027f) continue;
            float flux = Math.Max(Math.Abs(now - previous), Math.Abs(previous - old));
            float density = Unit((local - .021f) * 3.15f + flux * 1.05f);
            for (int layer = 0; layer < 3; layer++)
            {
                if (particleCount == particles.Length) return;
                float rate = layer == 0 ? .37f : layer == 1 ? .235f : .073f;
                float support = layer == 2 ? Math.Clamp(local * 2.6f + flux * .85f, .20f, 1) : 1;
                if (RandomUnit() > rate * density * support) continue;
                float x = Math.Clamp((zone + RandomUnit()) / zones + (RandomUnit() - .5f) * .015f, .004f, .996f);
                float scatter = RandomUnit(), reach = RandomUnit(), r = RandomUnit();
                float delay = layer == 0 ? .027f + RandomUnit() * .092f :
                    layer == 1 ? .095f + RandomUnit() * .155f : .145f + RandomUnit() * .19f;
                float life = layer == 0 ? .14f + RandomUnit() * .15f :
                    layer == 1 ? .28f + RandomUnit() * .26f : .41f + RandomUnit() * .27f;
                float lift = layer == 0 ? .012f + scatter * .087f :
                    layer == 1 ? .043f + scatter * .20f : .14f + scatter * .32f;
                float y = Mix(HeightAt(x), local, RandomUnit()) + lift * (.60f + .4f * Unit(local * 2 + flux)) -
                    (layer == 2 ? .006f : reach * .076f);
                if (y > .965f || (y > .88f && RandomUnit() < (y - .88f) * 6)) continue;
                float size = layer == 0 ? .62f + r * .80f : layer == 1 ? .68f + r * .89f : .47f + r * .53f;
                float light = layer == 0 ? .74f + RandomUnit() * .26f :
                    layer == 1 ? .63f + RandomUnit() * .37f : .29f + RandomUnit() * .48f;
                particles[particleCount++] = new Particle
                {
                    X = x, Y = y, Delay = delay, Life = life, Radius = size, Light = light, Tint = RandomUnit(),
                    Lift = .003f + RandomUnit() * (layer == 2 ? .018f : .009f), Fall = .26f + RandomUnit() * .65f,
                    Layer = layer
                };
            }
        }
    }

    public void Draw(SKCanvas canvas, float width, float height, ReadOnlySpan<float> bins, float scale, double elapsed)
    {
        if (width <= 8 || height <= 6) return;
        remainder += double.IsFinite(elapsed) ? Math.Clamp(elapsed, 0, .1) : 0;
        while (remainder >= StepSeconds)
        {
            Advance(bins, scale);
            remainder -= StepSeconds;
        }
        HasEnergy = strongest > .008f || particleCount > 0;
        if (!HasEnergy) return;

        float floor = height - 2.4f, amplitude = height - 5;
        float step = (width - 1) / (Bins - 1);
        // A short taper joins the lyric area to the unchanged dust under the controls.
        float edge(float x) => Unit(Math.Min(x, width - 1 - x) / 10f);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.Save();
        canvas.ClipRect(SKRect.Create(width, height));
        if (strongest > .008f)
        {
            using var rim = new SKPath();
            rim.MoveTo(0, floor);
            for (int i = 1; i < Bins; i++)
                rim.LineTo(i * step, floor - heights[i] * edge(i * step) * amplitude);
            using var fill = new SKPath(rim);
            fill.Close();
            using var gradient = SKShader.CreateLinearGradient(new SKPoint(0, floor - amplitude), new SKPoint(0, floor),
                FillColors, FillStops, SKShaderTileMode.Clamp);
            paint.Shader = gradient;
            canvas.DrawPath(fill, paint);
            paint.Shader = null;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = .65f;
            paint.Color = new SKColor(212, 255, 243, 190);
            canvas.DrawPath(rim, paint);
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor(249, 244, 255, (byte)(175 * Unit(strongest * 3.5f)));
            canvas.DrawRect(SKRect.Create(4, floor, width - 8, .65f), paint);
        }
        for (int i = 0; i < particleCount; i++)
        {
            ref readonly Particle p = ref particles[i];
            float age = p.Age + (float)remainder, down = Math.Max(0, age - p.Delay);
            float y = p.Y + p.Lift * Unit(age / .065f) - p.Fall * down * (.53f + down * 2.2f);
            if (age >= p.Life || y < .03f) continue;
            float x = p.X * (width - 1), taper = edge(x);
            // Give the high-frequency side more vertical separation on the
            // 60-pixel panel. Compress against the top instead of pinning dots there.
            float right = Unit((p.X - .45f) / .43f);
            right *= right * (3 - 2 * right);
            float lift = (p.Layer == 0 ? .10f : p.Layer == 1 ? .22f : .31f) * right;
            y += Math.Max(0, .95f - y) * lift * Unit(y / .14f);
            float alpha = p.Light * Unit((y - HeightAt(p.X) - .009f) / .041f) * Unit(age / .029f) *
                MathF.Pow(Unit((p.Life - age) / (.50f * p.Life)), .79f) * Unit((y - .027f) / .052f);
            alpha = Unit(alpha * (1.18f + .20f * right)) * taper;
            if (alpha < .065f) continue;
            float minRadius = p.Layer == 2 ? .86f : 1.08f;
            float at = floor - y * taper * amplitude;
            float r = Math.Max(minRadius, p.Radius * (1.21f + .08f * right));
            SKColor color = p.Tint < .20f ? new SKColor(196, 255, 240) :
                p.Tint < .43f ? new SKColor(229, 212, 255) : new SKColor(255, 249, 255);
            paint.Color = color.WithAlpha((byte)(46 * alpha));
            canvas.DrawOval(x, at, r * 2.15f, r * 2.1f, paint);
            paint.Color = color.WithAlpha((byte)(255 * alpha));
            if (p.Tint > .69f) canvas.DrawRect(SKRect.Create(x - r * .72f, at - r * 1.18f, r * 1.44f, r * 2.36f), paint);
            else canvas.DrawOval(x, at, r, r * (p.Tint < .30f ? 1.28f : .9f), paint);
        }
        canvas.Restore();
    }

    private struct Particle
    {
        public float X, Y, Age, Delay, Life, Radius, Light, Tint, Lift, Fall;
        public int Layer;
    }
}
