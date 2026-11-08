using System.Text;
using SkiaSharp;

namespace WindowsTouchBar.Helpers;

// SKCanvas.DrawText has no automatic Windows font fallback. Select a face that
// actually contains the lyric's glyphs instead of relying on the UI language.
internal static class LyricsFontResolver
{
    private static readonly object cacheLock = new();
    private static readonly Dictionary<string, string> selectedFamilies = new(StringComparer.Ordinal);
    private static readonly HashSet<string> installedFamilies = new(SKFontManager.Default.FontFamilies, StringComparer.OrdinalIgnoreCase);

    // The caller owns the returned native typeface and should dispose it.
    public static SKTypeface CreateTypeface(string text, SKFontStyleWeight weight = SKFontStyleWeight.SemiBold)
    {
        string family;
        lock (cacheLock)
        {
            if (!selectedFamilies.TryGetValue(text, out family!))
            {
                family = SelectFamily(text, weight);
                // Lyrics change continuously; do not retain a library's worth
                // of song text in a process-wide font cache.
                if (selectedFamilies.Count >= 256) selectedFamilies.Clear();
                selectedFamilies[text] = family;
            }
        }
        return Face(family, weight);
    }

    private static SKTypeface Face(string family, SKFontStyleWeight weight) =>
        SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

    private static string SelectFamily(string text, SKFontStyleWeight weight)
    {
        var characters = text.EnumerateRunes().Where(rune => !Rune.IsWhiteSpace(rune) && !Rune.IsControl(rune))
            .Select(rune => rune.Value).Distinct().ToArray();
        bool korean = characters.Any(value => value is >= 0xAC00 and <= 0xD7AF or >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F);
        bool japanese = characters.Any(value => value is >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0xFF66 and <= 0xFF9F);
        string[] preferred = korean
            ? new[] { "Malgun Gothic", "Yu Gothic UI", "Yu Gothic", "Microsoft YaHei UI", "Noto Sans SC", "MS Gothic", "Segoe UI" }
            : japanese
                ? new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Microsoft YaHei UI", "Malgun Gothic", "Noto Sans SC", "Segoe UI" }
                : new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans SC", "Yu Gothic UI", "Malgun Gothic", "MS Gothic", "Segoe UI" };
        string best = preferred.FirstOrDefault(installedFamilies.Contains) ?? SKTypeface.Default.FamilyName;
        int bestCount = -1;
        foreach (string candidate in preferred.Where(installedFamilies.Contains))
        {
            using var face = Face(candidate, weight);
            int covered = characters.Count(face.ContainsGlyph);
            if (covered == characters.Length) return face.FamilyName;
            if (covered > bestCount) { bestCount = covered; best = face.FamilyName; }
        }
        // A system-installed fallback may cover uncommon CJK characters that
        // are absent from the usual language fonts. Verify the whole line.
        foreach (int character in characters)
        {
            using var face = SKFontManager.Default.MatchCharacter(character);
            if (face is null) continue;
            int covered = characters.Count(face.ContainsGlyph);
            if (covered == characters.Length) return face.FamilyName;
            if (covered > bestCount) { bestCount = covered; best = face.FamilyName; }
        }
        return best;
    }
}
