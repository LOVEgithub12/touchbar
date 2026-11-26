using SkiaSharp;
using WindowsTouchBar.Audio;

namespace WindowsTouchBar.Lyrics;

// A grounded live spectrum, fine textured edges and audio-launched glints.
// Edge grain is stationary in spectrum space; it never invents a new peak.
internal sealed class SpectrumBackgroundRenderer
{
    private const int Bins = AudioSpectrumAnalyzer.ReferenceBinCount;
    private const float Ceiling = .85f;
    private const float BodyAttackSeconds = .060f;
    private const float BodyReleaseSeconds = .140f;
    private static readonly SKBitmap BodyTexture = CreateBodyTexture();
    private readonly float[] height = new float[Bins];
    private readonly Crest[] crests;
    private readonly Spark[] sparks;
    private float maximum;
    public bool HasEnergy { get; private set; }

    public SpectrumBackgroundRenderer()
    {
        var random = new Random(2918);
        var cells = new List<Crest>();
        for (int layer = 0; layer < 2; layer++)
        {
            // The filmed markers occupy irregular cells, with visible gaps.
            // Keep cell positions stable: random motion would detach them
            // from the local crest that produced them.
            for (float x = layer == 0 ? 0 : 2.3f; x < Bins - 1;
                x += layer == 0 ? 2.8f + 2.8f * (float)random.NextDouble() : 5f + 6f * (float)random.NextDouble())
                cells.Add(new Crest
                {
                    X = Math.Clamp(x + ((float)random.NextDouble() - .5f) * .5f, 0, Bins - 1),
                    Hold = layer == 0 ? .200f + .120f * (float)random.NextDouble() : .120f + .120f * (float)random.NextDouble(),
                    Gravity = layer == 0 ? .40f + .40f * (float)random.NextDouble() : .75f + .55f * (float)random.NextDouble(),
                    Brightness = layer == 0 ? .85f + .15f * (float)random.NextDouble() : .72f + .24f * (float)random.NextDouble(),
                    Radius = .95f + .60f * (float)random.NextDouble(),
                    Elongation = .8f + 1.5f * (float)random.NextDouble(),
                    Tint = (float)random.NextDouble()
                });
        }
        crests = cells.ToArray();
        var flights = new List<Spark>();
        for (float x = 1; x < Bins - 1; x += 5f + 7f * (float)random.NextDouble())
            flights.Add(new Spark
            {
                X = x, Lift = .080f + .095f * (float)random.NextDouble(),
                Hold = .19f + .13f * (float)random.NextDouble(),
                Gravity = .9f + .65f * (float)random.NextDouble(),
                Radius = .8f + .6f * (float)random.NextDouble(),
                Stretch = .8f + 1.2f * (float)random.NextDouble(),
                Drift = -1.8f + 3.6f * (float)random.NextDouble(),
                Trigger = .026f + .024f * (float)random.NextDouble(), Age = 10
            });
        sparks = flights.ToArray();
    }

    public void Reset()
    {
        Array.Clear(height);
        for (int i = 0; i < crests.Length; i++)
        {
            crests[i].Peak = crests[i].Y = crests[i].Age = crests[i].Opacity = 0;
        }
        for (int i = 0; i < sparks.Length; i++)
        {
            sparks[i].Y = sparks[i].Start = sparks[i].Previous = sparks[i].Rise = sparks[i].Opacity = 0;
            sparks[i].Age = 10;
        }
        maximum = 0;
        HasEnergy = false;
    }

    public void Draw(SKCanvas canvas, float width, float panelHeight, ReadOnlySpan<float> spectrum, float scale, double elapsed)
    {
        if (width < 2 || panelHeight < 4) return;
        float dt = double.IsFinite(elapsed) ? (float)Math.Clamp(elapsed, 0, .25) : 0;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0, 1) : 0;
        if (dt > 0) Advance(spectrum, scale, dt);
        if (!HasEnergy) return;
        float floor = panelHeight - 2f;
        float safeTop = Math.Max(7, panelHeight / 7f);
        float amplitude = Math.Min(floor, (floor - safeTop) / Ceiling);
        int save = canvas.Save();
        canvas.ClipRect(new SKRect(0, safeTop, width, panelHeight - 1));
        using var body = new SKPath();
        using var outline = new SKPath();
        body.MoveTo(0, floor);
        float lastX = 0, lastY = floor - amplitude * height[0];
        outline.MoveTo(lastX, lastY);
        body.LineTo(lastX, lastY);
        for (int i = 1; i < Bins; i++)
        {
            float x = i * (width - 1f) / (Bins - 1), y = floor - amplitude * height[i];
            float dx = x - lastX, dy = y - lastY;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            // Subdivide steep flanks as well as summits. A vertical needle
            // otherwise has only two smooth straight sides at native size.
            int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(dy) / Math.Max(.6f, panelHeight / 56f)));
            for (int j = 1; j <= steps; j++)
            {
                float t = j / (float)steps, px = lastX + dx * t, py = lastY + dy * t;
                float localHeight = height[i - 1] + (height[i] - height[i - 1]) * t;
                float strength = Math.Clamp(localHeight / .10f, 0, 1);
                float grain = EdgeGrain(i - 1 + t, localHeight * 54) * strength;
                px += dy / Math.Max(.001f, length) * grain * .85f * width / 1024f;
                py -= dx / Math.Max(.001f, length) * grain * 1.15f * panelHeight / 56f;
                px = Math.Clamp(px, 0, width - 1);
                py = Math.Clamp(py, safeTop + .35f, floor);
                body.LineTo(px, py); outline.LineTo(px, py);
            }
            lastX = x; lastY = y;
        }
        body.LineTo(width - 1, floor);
        body.Close();

        int textureSave = canvas.Save();
        canvas.ClipPath(body, antialias: true);
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
        canvas.DrawBitmap(BodyTexture, new SKRect(0, floor - amplitude, width, floor), paint);
        canvas.RestoreToCount(textureSave);

        // A narrow soft edge reproduces the luminous tips seen in the video.
        using var glowFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(.35f, width / 1600f));
        using var edgeGlow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(.8f, width / 850f), Color = new SKColor(182, 255, 221, 90),
            MaskFilter = glowFilter };
        canvas.DrawPath(outline, edgeGlow);

        using var crestPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(.4f, width / 1500f), StrokeJoin = SKStrokeJoin.Round, Color = new SKColor(254, 255, 252, 95) };
        canvas.DrawPath(outline, crestPaint);

        // The reference has a thin connecting floor, including in deep valleys.
        // Its visibility follows audio and it disappears when playback is silent.
        using var baseline = new SKPaint { IsAntialias = true, StrokeWidth = .65f,
            Color = new SKColor(239, 237, 249, (byte)(190 * Math.Clamp(maximum / .08f, 0, 1))) };
        canvas.DrawLine(0, floor, width - 1, floor, baseline);

        using var dot = new SKPaint { IsAntialias = true };
        using var glint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        foreach (var crest in crests)
        {
            float opacity = crest.Opacity;
            if (opacity <= .035f) continue;
            float x = crest.X * (width - 1) / (Bins - 1);
            float y = floor - amplitude * crest.Y;
            dot.Color = new SKColor((byte)(245 + 10 * crest.Tint), (byte)(232 + 18 * crest.Tint), 255, (byte)(255 * opacity));
            float radiusX = crest.Radius * width / 1024f;
            float velocity = 2 * crest.Gravity * Math.Max(0, crest.Age - crest.Hold);
            float fallTrail = Math.Clamp(velocity / 48, 0, .04f) * amplitude;
            // Subpixel dots disappeared on the 56 px physical display.
            float radiusY = Math.Max(.72f * panelHeight / 56f, crest.Radius * amplitude / 78f * crest.Elongation);
            if (fallTrail > .12f)
            {
                glint.StrokeWidth = Math.Max(.6f, radiusX * 1.15f);
                glint.Color = dot.Color.WithAlpha((byte)(175 * opacity));
                canvas.DrawLine(x, y - fallTrail, x, y, glint);
            }
            canvas.DrawOval(x, y, radiusX, radiusY, dot);
            dot.Color = new SKColor(255, 255, 247, (byte)(235 * opacity));
            canvas.DrawOval(x - radiusX * .13f, y - radiusY * .20f, radiusX * .43f, radiusY * .5f, dot);
        }
        foreach (var spark in sparks)
        {
            if (spark.Opacity <= .035f) continue;
            float x = (spark.X + spark.Drift * Math.Min(spark.Age, .8f)) * (width - 1) / (Bins - 1);
            float y = floor - amplitude * spark.Y;
            float rx = spark.Radius * width / 1024f;
            float ry = Math.Max(.72f, spark.Stretch * .68f) * panelHeight / 56f;
            glint.StrokeWidth = Math.Max(.65f, rx);
            glint.Color = new SKColor(235, 218, 255, (byte)(170 * spark.Opacity));
            canvas.DrawLine(x, y - ry * 1.3f, x + spark.Drift * .15f, y + ry, glint);
            dot.Color = new SKColor(255, 250, 255, (byte)(255 * spark.Opacity));
            canvas.DrawOval(x, y, rx, ry, dot);
        }
        canvas.RestoreToCount(save);
    }

    private void Advance(ReadOnlySpan<float> spectrum, float scale, float dt)
    {
        // Follow musical accents over several display frames. The former
        // sub-frame attack/release turned small FFT changes into visible flashes.
        // A slower release bridges those changes without inventing a beat clock.
        float rise = 1 - MathF.Exp(-dt / BodyAttackSeconds);
        float fall = 1 - MathF.Exp(-dt / BodyReleaseSeconds);
        maximum = 0;
        for (int i = 0; i < Bins; i++)
        {
            float sample = 0;
            if (!spectrum.IsEmpty)
            {
                float pos = i * (spectrum.Length - 1f) / (Bins - 1);
                int a = (int)pos, b = Math.Min(a + 1, spectrum.Length - 1);
                float left = float.IsFinite(spectrum[a]) ? spectrum[a] : 0;
                float right = float.IsFinite(spectrum[b]) ? spectrum[b] : 0;
                sample = Math.Max(0, left + (right - left) * (pos - a)) * scale;
            }
            float target = SoftSurface(sample);
            height[i] += (target - height[i]) * (target > height[i] ? rise : fall);
            if (height[i] < .0001f) height[i] = 0;
            maximum = Math.Max(maximum, height[i]);
        }
        bool residue = false;
        for (int i = 0; i < crests.Length; i++)
        {
            ref Crest c = ref crests[i];
            float local = Surface(c.X);
            c.Age += dt;
            float fallTime = Math.Max(0, c.Age - c.Hold);
            float held = Math.Max(0, c.Peak - c.Gravity * fallTime * fallTime);
            if (local >= held)
            {
                // A new marker is caught by its actual local wave crest.
                c.Peak = c.Y = local;
                c.Age = 0;
            }
            else c.Y = held;
            // A passing wave can cover/uncover a marker in adjacent frames.
            // Ease that visibility change instead of switching its light on/off.
            float separation = c.Y - local;
            float opacity = c.Brightness * Math.Clamp((separation - .002f) / .020f, 0, 1)
                * Math.Clamp((c.Y - .015f) / .045f, 0, 1);
            float opacitySeconds = opacity > c.Opacity ? .060f : .160f;
            c.Opacity += (opacity - c.Opacity) * (1 - MathF.Exp(-dt / opacitySeconds));
            residue |= c.Y > .025f;
        }
        for (int i = 0; i < sparks.Length; i++)
        {
            ref Spark s = ref sparks[i];
            float local = Surface(s.X);
            s.Age += dt;
            s.Rise = s.Rise * MathF.Exp(-dt / .12f) + Math.Max(0, local - s.Previous);
            s.Previous = local;
            // Only a local audio rise can launch a particle. Constant tones
            // and empty frequency gaps cannot become a random particle clock.
            if (local > .035f && s.Rise > s.Trigger && s.Age > .24f)
            {
                s.Start = local;
                s.Age = 0;
                s.Rise = 0;
            }
            // The rising wave can otherwise overtake a particle launched at
            // the very start of an attack. Carry its launch point with that
            // same crest briefly, then let it hold and fall independently.
            if (s.Age < .12f) s.Start = Math.Max(s.Start, local);
            float lift = s.Lift * Math.Clamp(s.Start / .20f, 0, 1);
            float fallTime = Math.Max(0, s.Age - s.Hold);
            s.Y = Math.Clamp(s.Start + lift * (1 - MathF.Exp(-s.Age / .050f)) - s.Gravity * fallTime * fallTime, 0, Ceiling - .025f);
            float target = Math.Clamp((s.Y - local - .012f) / .045f, 0, 1)
                * Math.Clamp((s.Y - .025f) / .05f, 0, 1);
            s.Opacity += (target - s.Opacity) * (1 - MathF.Exp(-dt / (target > s.Opacity ? .060f : .160f)));
            residue |= s.Y > .025f;
        }
        HasEnergy = maximum > .001f || residue;
        if (!HasEnergy) Reset();
    }

    private float Surface(float position)
    {
        int a = Math.Clamp((int)position, 0, Bins - 1), b = Math.Min(a + 1, Bins - 1);
        return height[a] + (height[b] - height[a]) * (position - a);
    }

    private static float EdgeGrain(float x, float y)
    {
        x /= 1.7f; y /= 1.15f;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx *= fx * (3 - 2 * fx); fy *= fy * (3 - 2 * fy);
        static float At(int a, int b)
        {
            uint n = unchecked((uint)(a * 374761393 + b * 668265263 + 17029));
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 65535) / 32767.5f - 1;
        }
        float low = At(ix, iy) + (At(ix + 1, iy) - At(ix, iy)) * fx;
        float high = At(ix, iy + 1) + (At(ix + 1, iy + 1) - At(ix, iy + 1)) * fx;
        return low + (high - low) * fy;
    }

    private static float SoftSurface(float value)
    {
        const float knee = .78f;
        return value <= knee ? Math.Max(0, value) : knee + (Ceiling - knee) * (1 - MathF.Exp(-(value - knee) / (Ceiling - knee)));
    }

    private static SKBitmap CreateBodyTexture()
    {
        // Only stationary colour/brightness statistics are reproduced here.
        // Geometry and motion always come from the supplied live spectrum.
        const int w = 1024, h = 138;
        var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var pixels = new SKColor[w * h];
        SKColor[] palette = { new(242, 239, 250), new(235, 244, 248), new(225, 251, 246),
            new(213, 254, 245), new(196, 255, 243), new(174, 255, 239), new(163, 253, 232), new(170, 255, 233) };
        float[] stops = { 0, .1168f, .2336f, .3504f, .4672f, .584f, .68f, 1 };
        var random = new Random(17029);
        byte Channel(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
        for (int y = 0; y < h; y++)
        {
            float level = (h - 1f - y) / (h - 1), rowGrain = ((float)random.NextDouble() - .5f) * 2.5f;
            int s = 0;
            while (s < stops.Length - 2 && level > stops[s + 1]) s++;
            float f = (level - stops[s]) / (stops[s + 1] - stops[s]);
            var a = palette[s]; var b = palette[s + 1];
            for (int x = 0; x < w; x++)
            {
                float ripple = MathF.Sin(y * 1.7f + .8f * MathF.Sin(x * .011f + y * .03f));
                float grain = ((float)random.NextDouble() - .5f) * 3.5f + rowGrain;
                float pastel = 4.8f * MathF.Sin(y * .39f + x * .005f) + 3.4f * ripple;
                pixels[y * w + x] = new SKColor(Channel(a.Red + (b.Red - a.Red) * f + pastel + grain),
                    Channel(a.Green + (b.Green - a.Green) * f - pastel * .65f + grain),
                    Channel(a.Blue + (b.Blue - a.Blue) * f + pastel * .45f + grain));
            }
        }
        bitmap.Pixels = pixels;
        bitmap.SetImmutable();
        return bitmap;
    }

    private struct Crest
    {
        public float X, Hold, Gravity, Brightness, Radius, Elongation, Tint, Peak, Y, Age, Opacity;
    }
    private struct Spark
    {
        public float X, Lift, Hold, Gravity, Radius, Stretch, Drift, Trigger;
        public float Start, Y, Age, Previous, Rise, Opacity;
    }
}
