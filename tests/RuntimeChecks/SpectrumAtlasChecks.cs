using System.Reflection;
using SkiaSharp;
using WindowsTouchBar.Lyrics;

internal static class SpectrumAtlasChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void DescriptorParity()
    {
        var atlas = SpectrumAtlas.Shared ?? throw new InvalidOperationException("Atlas did not load.");
        var shape = new float[32];
        var broad = new float[16];
        for (int anchor = 0; anchor < atlas.Count; anchor++)
        {
            var profile = atlas.Profile(anchor);
            SpectrumAtlas.BuildDescriptor(profile, shape, broad);
            Require(shape.Zip(atlas.Shape.Skip(anchor * 32).Take(32), (a, b) => Math.Abs(a - b)).Max() < 2e-5f,
                $"Generator/runtime shape mismatch at anchor {anchor}.");
            Require(broad.Zip(atlas.Broad.Skip(anchor * 16).Take(16), (a, b) => Math.Abs(a - b)).Max() < 2e-5f,
                $"Generator/runtime broad mismatch at anchor {anchor}.");
            Require(Math.Abs(SpectrumAtlas.MeasureLevel(profile) - atlas.Level[anchor]) < 2e-5f,
                $"Generator/runtime level mismatch at anchor {anchor}.");
            Require(atlas.Change[anchor] >= 0 && atlas.Change[anchor] <= .4f,
                "Atlas onset is not a 60 Hz positive level difference.");
            Require(Math.Abs(profile.ToArray().Max() - atlas.Peak[anchor]) < 1e-6f, "Peak uses a different height scale.");
        }
        // A bin exactly at a 16-bin block boundary must not leak into its neighbour.
        // The old 512 -> 1024 interpolation did leak energy across this boundary.
        var bins = new float[512];
        bins[15] = 1;
        SpectrumAtlas.BuildDescriptor(bins, shape, broad);
        Require(Math.Abs(shape[0] - 1) < 1e-6f && shape.Skip(1).All(v => Math.Abs(v) < 1e-6f),
            "Descriptor resampling leaked energy across a native 512-bin boundary.");
        var first = (float[])shape.Clone();
        SpectrumAtlas.BuildDescriptor(bins, shape, broad);
        Require(first.SequenceEqual(shape), "Repeated descriptor changed.");
        foreach (var input in new[] { Array.Empty<float>(), new[] { .3f }, new[] { float.NaN, float.PositiveInfinity, .4f }, new float[512] })
        {
            SpectrumAtlas.BuildDescriptor(input, shape, broad);
            Require(shape.All(float.IsFinite) && broad.All(float.IsFinite), "Short/invalid input produced non-finite descriptors.");
        }
    }

    public static void SoftPeaks()
    {
        var method = typeof(SpectrumBackgroundRenderer).GetMethod("SoftSurface", BindingFlags.Static | BindingFlags.NonPublic)!;
        float Compress(float value) => (float)method.Invoke(null, new object[] { value })!;
        float previous = Compress(.64f);
        for (int i = 641; i <= 1000; i++)
        {
            float value = Compress(i / 1000f);
            Require(value > previous && value <= .895f, "Distinct loud peaks collapsed into a horizontal cap.");
            previous = value;
        }
        Require(previous > .88f, "Peak compression lost too much height.");
        var atlas = SpectrumAtlas.Shared!;
        int anchor = Enumerable.Range(0, atlas.Count).MaxBy(a => atlas.Profile(a).ToArray().Max());
        float[] bins = atlas.Profile(anchor).ToArray();
        var renderer = new SpectrumBackgroundRenderer();
        using var bitmap = new SKBitmap(972, 56);
        using var canvas = new SKCanvas(bitmap);
        for (int frame = 0; frame < 180; frame++)
        {
            canvas.Clear(SKColors.Black);
            renderer.Draw(canvas, 972, 56, bins, 1, 1.0 / 60);
        }
        // Isolate convergence at a fixed live drive after the normal audio warmup.
        typeof(SpectrumBackgroundRenderer).GetField("rhythmLevel", Private)!.SetValue(renderer, 1f);
        typeof(SpectrumBackgroundRenderer).GetField("rhythmKick", Private)!.SetValue(renderer, 0f);
        var advance = typeof(SpectrumBackgroundRenderer).GetMethod("AdvanceAtlasSpectrum", Private)!;
        for (int frame = 0; frame < 120; frame++)
            advance.Invoke(renderer, new object[] { 972, 971f, 1f / 60, 0f });
        float[] Field(string name) => (float[])typeof(SpectrumBackgroundRenderer).GetField(name, Private)!.GetValue(renderer)!;
        float[] target = Field("clusterTarget"), current = Field("clusterHeight");
        Require(target.Max() > .80f && current.Max() <= .895f, "Loud contour lost its height or exceeded the safe ceiling.");
        Require(target.Zip(current, (a, b) => Math.Abs(a - b)).Max() < 1e-5f,
            "Steady peaks shrink because the temporal state is being compressed repeatedly.");
    }

    public static void FrameCadence()
    {
        float[] bins = Enumerable.Range(0, 512).Select(i => .1f + .55f * MathF.Pow(MathF.Sin(i * .043f), 2)).ToArray();
        float[] Render(int frames, double dt)
        {
            var renderer = new SpectrumBackgroundRenderer();
            using var bitmap = new SKBitmap(972, 56);
            using var canvas = new SKCanvas(bitmap);
            for (int i = 0; i < frames; i++)
            {
                canvas.Clear(SKColors.Black);
                renderer.Draw(canvas, 972, 56, bins, 1, dt);
            }
            return (float[])typeof(SpectrumBackgroundRenderer).GetField("clusterHeight", Private)!.GetValue(renderer)!;
        }
        Require(Render(60, 1.0 / 60).Zip(Render(30, 1.0 / 30), (a, b) => Math.Abs(a - b)).Max() < 1e-6f,
            "Equal elapsed time produced a different spectrum at 30 and 60 fps.");
    }

    public static void ParticleFlight()
    {
        var renderer = new SpectrumBackgroundRenderer();
        using var bitmap = new SKBitmap(972, 56);
        using var canvas = new SKCanvas(bitmap);
        float[] bins = Enumerable.Range(0, 512).Select(i => .18f + .5f * MathF.Pow(MathF.Sin(i * .037f), 2)).ToArray();
        object Field(string name) => typeof(SpectrumBackgroundRenderer).GetField(name, Private)!.GetValue(renderer)!;
        var surface = typeof(SpectrumBackgroundRenderer).GetMethod("VisibleSurfaceAt", Private)!;
        var threshold = typeof(SpectrumBackgroundRenderer).GetMethod("BodyThreshold", Private)!;
        int births = 0;
        for (int frame = 0; frame < 180; frame++)
        {
            canvas.Clear(SKColors.Black);
            renderer.Draw(canvas, 972, 56, bins, 1, 1.0 / 60);
            Array particles = (Array)Field("particles");
            for (int i = 0; i < (int)Field("particleCount"); i++)
            {
                object p = particles.GetValue(i)!;
                float Value(string name) => (float)p.GetType().GetField(name)!.GetValue(p)!;
                if (Value("Age") != 0) continue;
                float x = Value("X"), y = Value("Y");
                float origin = (float)surface.Invoke(renderer, new object[] { x })!;
                float gate = (float)threshold.Invoke(renderer, new object[] { (int)MathF.Round(x * 971) })!;
                Require(origin >= gate && y - origin >= .0019f && y - origin <= .0101f,
                    "Particle was emitted above an invisible valley or away from the current surface.");
                Require(Value("Hold") > 0, "Particle has no apex dwell.");
                births++;
            }
        }
        Require(births > 100, "No meaningful particle emission was exercised.");

        // Follow one isolated particle through the production drawing path.
        // Read its bright-core centroid; this also catches a delayed launch or
        // a right-side lift applied before the ascent starts.
        Array flight = (Array)Field("particles");
        object particle = flight.GetValue(0)!;
        void Set(string name, object value) => particle.GetType().GetField(name)!.SetValue(particle, value);
        Set("X", .6f); Set("Y", .12f); Set("Lift", .45f); Set("Fall", .5f);
        Set("Hold", .30f); Set("Life", 1.5f); Set("Radius", .7f); Set("Light", 1f); Set("Tint", .5f); Set("Layer", 3);
        typeof(SpectrumBackgroundRenderer).GetField("particleCount", Private)!.SetValue(renderer, 1);
        typeof(SpectrumBackgroundRenderer).GetField("strongest", Private)!.SetValue(renderer, 0f);
        Array.Clear((float[])Field("clusterHeight"));
        float Centroid(float age)
        {
            Set("Age", age);
            flight.SetValue(particle, 0);
            canvas.Clear(SKColors.Black);
            renderer.Draw(canvas, 972, 56, bins, 1, 0);
            double weight = 0, weighted = 0;
            for (int y = 0; y < 56; y++)
                for (int x = 576; x < 590; x++)
                {
                    int value = bitmap.GetPixel(x, y).Green;
                    if (value < 24) continue;
                    weight += value; weighted += y * value;
                }
            Require(weight > 0, $"Particle was invisible at age {age}.");
            return (float)(weighted / weight);
        }
        float launch = Centroid(.006f), rising = Centroid(.06f), apex = Centroid(.115f);
        float hold = Centroid(.25f), falling = Centroid(.70f);
        Require(launch > 43f && rising < launch - 5f && apex < rising - 1f,
            "Particle did not launch promptly from its surface and rise.");
        Require(Math.Abs(apex - hold) < .1f && falling > hold + 3f,
            "Particle did not dwell at its apex before descending.");
    }
}
