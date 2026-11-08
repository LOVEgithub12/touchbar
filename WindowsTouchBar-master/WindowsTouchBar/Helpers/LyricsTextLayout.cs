using System.Globalization;
using System.Text;
using SkiaSharp;

namespace WindowsTouchBar.Helpers;

// Keep measuring, centering and karaoke clipping on the same font runs. A
// single fixed typeface cannot cover simplified Chinese and Hangul together.
internal sealed class LyricsTextLayout : IDisposable
{
    private sealed record Run(int Start, string Text, SKTypeface Face);
    private readonly List<Run> runs = new();
    private readonly Dictionary<string, SKTypeface> faces = new(StringComparer.Ordinal);
    private readonly string text;
    public string PrimaryFamily { get; }
    public bool HasMissingGlyphs { get; }

    public LyricsTextLayout(string text)
    {
        this.text = text;
        var primary = LyricsFontResolver.CreateTypeface(text);
        faces.Add(primary.FamilyName, primary);
        PrimaryFamily = primary.FamilyName;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        for (int index = 0; index < starts.Length; index++)
        {
            int start = starts[index], end = index + 1 < starts.Length ? starts[index + 1] : text.Length;
            string element = text[start..end];
            SKTypeface face = primary;
            if (!Covers(primary, element))
            {
                var fallback = LyricsFontResolver.CreateTypeface(element);
                if (faces.TryGetValue(fallback.FamilyName, out var existing)) { fallback.Dispose(); face = existing; }
                else { faces.Add(fallback.FamilyName, fallback); face = fallback; }
            }
            if (!Covers(face, element)) HasMissingGlyphs = true;
            if (runs.Count > 0 && ReferenceEquals(runs[^1].Face, face))
                runs[^1] = runs[^1] with { Text = runs[^1].Text + element };
            else runs.Add(new Run(start, element, face));
        }
    }

    private static bool Covers(SKTypeface face, string value) => value.EnumerateRunes()
        .All(rune => Rune.IsWhiteSpace(rune) || Rune.IsControl(rune) || face.ContainsGlyph(rune.Value));

    public float MeasureText(SKPaint paint) => MeasurePrefix(text.Length, paint);

    // utf16Length is a grapheme boundary supplied by the karaoke renderer.
    public float MeasurePrefix(int utf16Length, SKPaint paint)
    {
        var original = paint.Typeface;
        float width = 0;
        try
        {
            foreach (var run in runs)
            {
                int count = Math.Clamp(utf16Length - run.Start, 0, run.Text.Length);
                if (count == 0) break;
                paint.Typeface = run.Face;
                width += paint.MeasureText(run.Text[..count]);
                if (count < run.Text.Length) break;
            }
            return width;
        }
        finally { paint.Typeface = original; }
    }

    public SKRect MeasureBounds(SKPaint paint)
    {
        var original = paint.Typeface;
        var bounds = SKRect.Empty;
        float x = 0;
        bool any = false;
        try
        {
            foreach (var run in runs)
            {
                paint.Typeface = run.Face;
                var part = SKRect.Empty;
                float advance = paint.MeasureText(run.Text, ref part);
                if (!part.IsEmpty)
                {
                    part.Offset(x, 0);
                    bounds = any ? SKRect.Union(bounds, part) : part;
                    any = true;
                }
                x += advance;
            }
            return bounds;
        }
        finally { paint.Typeface = original; }
    }

    public void Draw(SKCanvas canvas, float x, float baseline, SKPaint paint)
    {
        var original = paint.Typeface;
        try
        {
            foreach (var run in runs)
            {
                paint.Typeface = run.Face;
                canvas.DrawText(run.Text, x, baseline, paint);
                x += paint.MeasureText(run.Text);
            }
        }
        finally { paint.Typeface = original; }
    }

    public void Dispose()
    {
        foreach (var face in faces.Values) face.Dispose();
        faces.Clear();
    }
}
