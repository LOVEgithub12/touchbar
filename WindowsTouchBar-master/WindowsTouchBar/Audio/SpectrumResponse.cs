using System.Reflection;
using System.Text.Json;

namespace WindowsTouchBar.Audio;

// Keep the reference frequency axis, but derive height from current audio.
// Broad, slowly adapting frequency regions prevent a bass-heavy output mix
// from suppressing every treble peak. No individual bin is normalized to a
// fixed height; notes, gaps and transients retain their measured contrast.
internal sealed class SpectrumResponse
{
    private const int Bins = AudioSpectrumAnalyzer.ReferenceBinCount;
    private const int Regions = 9;
    private static readonly Calibration Model = Load();
    private readonly float[] regionalPeak = Enumerable.Range(0, Regions).Select(i => -24f - i * 4.5f).ToArray();
    private readonly float[] currentDb = new float[Bins];
    private readonly float[] measuredPeak = new float[Regions];

    public float FrequencyAt(int bin, float limit) => 32 * MathF.Pow(limit / 32, (Model.Axis[bin] + .5f) / 512);

    public float[] Read(float[][] decibels, float elapsed, float inputOffset, float inputRms, bool inputActive)
    {
        Array.Fill(measuredPeak, -120f);
        for (int i = 0; i < Bins; i++)
        {
            float modelBin = Math.Clamp((i + .5f) / 2 - .5f, 0, 511);
            float mid = Math.Clamp((modelBin - 100) / 110f, 0, 1);
            float high = Math.Clamp((modelBin - 240) / 110f, 0, 1);
            float Db(int w) => float.IsFinite(decibels[w][i]) ? Math.Clamp(decibels[w][i], -156, 24) : -156;
            // Undo the common input offset before regional adaptation. A bass
            // accent must not turn down an otherwise unchanged treble note.
            currentDb[i] = Db(0) * (1 - mid) * (1 - high) + Db(1) * mid * (1 - high) + Db(2) * high - inputOffset;
            for (int region = 0; region < Regions; region++)
                if (MathF.Abs(i - region * (Bins - 1f) / (Regions - 1)) < 85)
                    measuredPeak[region] = Math.Max(measuredPeak[region], currentDb[i]);
        }
        for (int region = 0; region < Regions; region++)
        {
            // Freeze sensitivity through silence and inaudible bands. The
            // absolute gate is evaluated BEFORE shared input attenuation.
            if (!inputActive || inputRms < .001f || measuredPeak[region] <= -72) continue;
            float target = Math.Clamp(measuredPeak[region], -80, 24);
            float seconds = target > regionalPeak[region] ? .8f : 6f;
            regionalPeak[region] += (target - regionalPeak[region]) * (1 - MathF.Exp(-elapsed / seconds));
        }

        var output = new float[Bins];
        // Quiet passages stay quiet even after a pause. This gate uses slow
        // input power, not a per-frame maximum or an invented beat clock.
        float quiet = MathF.Pow(Math.Clamp(inputRms / .02f, 0, 1), .7f);
        for (int i = 0; i < Bins; i++)
        {
            float position = i * (Regions - 1f) / (Bins - 1);
            int a = (int)position, b = Math.Min(a + 1, Regions - 1);
            float reference = regionalPeak[a] + (regionalPeak[b] - regionalPeak[a]) * (position - a);
            float presence = Math.Clamp((currentDb[i] + 90) / 18, 0, 1);
            float contrast = Math.Clamp((currentDb[i] - reference + 24) / 30, 0, 1.5f);
            // Give the central harmonic peaks enough vertical range on a
            // 56-pixel display. This broad, fixed sensitivity is applied to
            // measured energy only: absent notes and deep valleys stay zero.
            // It does not select a silhouette, move frequencies or add a clock.
            float axis = i / (Bins - 1f);
            float focus = SmoothStep(.20f, .40f, axis) * (1 - SmoothStep(.64f, .86f, axis));
            output[i] = 1.2f * contrast * contrast * presence * quiet * (1 + 1.1f * focus);
        }
        return output;
    }

    private static float SmoothStep(float low, float high, float value)
    {
        float t = Math.Clamp((value - low) / (high - low), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Calibration Load()
    {
        Assembly assembly = typeof(SpectrumResponse).Assembly;
        string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("spectrum-response.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(name)!;
        var model = JsonSerializer.Deserialize<Calibration>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (model.Version != 2 || model.Axis.Length != Bins ||
            model.Axis.Any(x => !float.IsFinite(x) || x < 0 || x >= 511))
            throw new InvalidDataException("Invalid spectrum frequency calibration.");
        return model;
    }

    private sealed class Calibration
    {
        public int Version { get; set; }
        public float[] Axis { get; set; } = Array.Empty<float>();
    }
}
