using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace WindowsTouchBar.Lyrics;

// Video-traced contour bank. Each anchor stores the measured silhouette and
// the 512-bin silhouette descriptor computed by rhythm-review/generate_atlas_v3.py.
internal sealed class SpectrumAtlas
{
    internal const int ShapeBands = 32;
    internal const int BroadBands = 16;
    public static readonly SpectrumAtlas? Shared = Load();

    public int Count { get; }
    public int Width { get; }
    public float[] Profiles { get; }
    public float[] Shape { get; }
    public float[] Broad { get; }
    public float[] Level { get; }
    public float[] Change { get; }
    public float[] Density { get; }
    public float[] Peak { get; }

    private SpectrumAtlas(int count, int width, float[] profiles, float[] shape,
        float[] broad, float[] level, float[] change, float[] density, float[] peak)
    {
        Count = count;
        Width = width;
        Profiles = profiles;
        Shape = shape;
        Broad = broad;
        Level = level;
        Change = change;
        Density = density;
        Peak = peak;
    }

    public ReadOnlySpan<float> Profile(int anchor) =>
        Profiles.AsSpan(anchor * Width, Width);

    public int Select(ReadOnlySpan<float> bins, float currentLevel, float currentChange, int previous)
    {
        if (bins.Length == 0 || Count == 0) return -1;
        Span<float> targetShape = stackalloc float[ShapeBands];
        Span<float> targetBroad = stackalloc float[BroadBands];
        BuildDescriptor(bins, targetShape, targetBroad);

        int best = previous >= 0 && previous < Count ? previous : 0;
        float bestScore = float.MaxValue;
        for (int anchor = 0; anchor < Count; anchor++)
        {
            float shapeDistance = 0;
            int at = anchor * ShapeBands;
            for (int i = 0; i < ShapeBands; i++)
            {
                float delta = Shape[at + i] - targetShape[i];
                shapeDistance += delta * delta;
            }
            float broadDistance = 0;
            at = anchor * BroadBands;
            for (int i = 0; i < BroadBands; i++)
            {
                float delta = Broad[at + i] - targetBroad[i];
                broadDistance += delta * delta;
            }

            float levelDelta = Level[anchor] - currentLevel;
            float score = .78f * shapeDistance + .22f * broadDistance + .34f * levelDelta * levelDelta;
            score += .04f * MathF.Abs(Change[anchor] - currentChange);
            if (currentLevel > .42f && Density[anchor] < .14f) score += .22f;
            if (currentLevel > .66f && Peak[anchor] < .30f) score += .12f;
            if (previous >= 0 && previous < Count)
            {
                float continuity = 0;
                at = anchor * ShapeBands;
                int old = previous * ShapeBands;
                for (int i = 0; i < ShapeBands; i++) continuity += Shape[at + i] * Shape[old + i];
                score += .09f * (1 - continuity);
            }

            if (score < bestScore) { bestScore = score; best = anchor; }
        }
        return best;
    }

    public static void BuildDescriptor(ReadOnlySpan<float> bins, Span<float> shape, Span<float> broad)
    {
        Vectorize(bins, shape);
        Vectorize(bins, broad);
    }

    internal static float MeasureLevel(ReadOnlySpan<float> bins)
    {
        if (bins.IsEmpty) return 0;
        float sum = 0;
        foreach (float value in bins) sum += Math.Clamp(Finite(value), 0, 1);
        return Math.Clamp((sum / bins.Length - .045f) / .50f, 0, 1);
    }

    private static void Vectorize(ReadOnlySpan<float> source, Span<float> output)
    {
        Span<float> values = stackalloc float[output.Length];
        Span<float> sorted = stackalloc float[output.Length];
        for (int band = 0; band < output.Length; band++)
        {
            int start = band * source.Length / output.Length;
            int end = Math.Max(start + 1, (band + 1) * source.Length / output.Length);
            end = Math.Min(end, source.Length);
            float sum = 0;
            for (int i = start; i < end; i++) sum += Finite(source[i]);
            values[band] = sum / Math.Max(1, end - start);
            sorted[band] = values[band];
        }

        float[] sortable = sorted.ToArray();
        Array.Sort(sortable);
        for (int i = 0; i < sorted.Length; i++) sorted[i] = sortable[i];
        float median = Percentile(sorted, .5f);
        float mean = 0;
        for (int i = 0; i < values.Length; i++) mean += values[i];
        mean /= Math.Max(1, values.Length);
        float variance = 0;
        for (int i = 0; i < values.Length; i++)
        {
            float delta = values[i] - mean;
            variance += delta * delta;
        }
        float scale = MathF.Sqrt(variance / Math.Max(1, values.Length));
        scale = Math.Max(1e-5f, scale);
        float norm = 0;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = (values[i] - median) / scale;
            norm += output[i] * output[i];
        }
        norm = MathF.Sqrt(norm);
        if (norm < 1e-6f)
        {
            output.Clear();
            return;
        }
        for (int i = 0; i < output.Length; i++) output[i] /= norm;
    }

    private static float Percentile(ReadOnlySpan<float> sorted, float q)
    {
        if (sorted.Length == 0) return 0;
        float position = Math.Clamp(q, 0, 1) * (sorted.Length - 1);
        int left = (int)position;
        int right = Math.Min(left + 1, sorted.Length - 1);
        float fraction = position - left;
        return sorted[left] + (sorted[right] - sorted[left]) * fraction;
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;

    private static SpectrumAtlas? Load()
    {
        try
        {
            Assembly assembly = typeof(SpectrumAtlas).Assembly;
            string? name = assembly.GetManifestResourceNames()
                .FirstOrDefault(item => item.EndsWith("spectrum-atlas-v3.json", StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;
            using Stream? resource = assembly.GetManifestResourceStream(name);
            if (resource is null) return null;
            using var document = JsonDocument.Parse(resource);
            JsonElement root = document.RootElement;
            if (root.GetProperty("descriptor_space").GetString() != "traced-profile-512-v1" ||
                root.GetProperty("descriptor_bins").GetInt32() != 512)
                return null;
            int count = root.GetProperty("anchors").GetInt32();
            int width = root.GetProperty("width").GetInt32();
            float profileScale = root.GetProperty("profile_scale").GetSingle();
            byte[] profileBytes = Decompress(Text(root, "profiles"));
            if (count <= 0 || width <= 0 || profileBytes.Length != count * width || profileScale <= 0)
                return null;

            var profiles = new float[profileBytes.Length];
            for (int i = 0; i < profileBytes.Length; i++)
                profiles[i] = profileBytes[i] / profileScale;

            float[] shape = Decode(root, "shape");
            float[] broad = Decode(root, "broad");
            float[] level = Decode(root, "level");
            float[] change = Decode(root, "change");
            float[] density = Decode(root, "density");
            float[] peak = Decode(root, "peak");
            if (shape.Length != count * ShapeBands || broad.Length != count * BroadBands ||
                level.Length != count || change.Length != count || density.Length != count || peak.Length != count)
                return null;

            return new SpectrumAtlas(count, width, profiles, shape, broad, level, change, density, peak);
        }
        catch
        {
            return null;
        }
    }

    private static string Text(JsonElement root, string property) =>
        root.GetProperty(property).GetString() ?? throw new InvalidDataException(property);

    private static byte[] Decompress(string encoded)
    {
        using var input = new ZLibStream(new MemoryStream(Convert.FromBase64String(encoded)), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static float[] Decode(JsonElement root, string property) =>
        DecodeFloatBytes(Decompress(Text(root, property)));

    private static float[] DecodeFloatBytes(byte[] bytes)
    {
        var result = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, result, 0, result.Length * sizeof(float));
        return result;
    }
}
