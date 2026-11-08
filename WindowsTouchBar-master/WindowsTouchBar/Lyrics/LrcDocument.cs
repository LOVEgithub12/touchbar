using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowsTouchBar.Lyrics;

internal sealed class LrcDocument
{
    private static readonly Regex Timestamp = new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex WordTimestamp = new(@"<(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?>", RegexOptions.Compiled);
    private static readonly Regex Offset = new(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Metadata = new(@"^\[(ar|ti|al|by|re|ve|length|offset|id|hash|total):", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal sealed record Word(double Seconds, int CharacterIndex);
    internal sealed record Line(double Seconds, string Text, IReadOnlyList<Word> Words);
    internal sealed record Frame(string CurrentLine, string NextLine, double Progress)
    {
        public double HighlightRate { get; init; }
        public double HighlightLimit { get; init; } = 1;
        public bool HasWordTiming { get; init; }
    }
    public IReadOnlyList<Line> Lines { get; }
    public bool HasLyrics => Lines.Any(line => line.Text.Length != 0);
    public string Title { get; }
    public string Artist { get; }

    private LrcDocument(IReadOnlyList<Line> lines, string title, string artist)
    {
        Lines = lines; Title = title; Artist = artist;
    }

    public static LrcDocument Parse(string text)
    {
        if (text.Length > 2 * 1024 * 1024) throw new InvalidDataException("Lyrics exceed the size limit.");
        double offset = 0;
        foreach (Match match in Offset.Matches(text))
            if (long.TryParse(match.Groups[1].Value, out long milliseconds)) offset = Math.Clamp(milliseconds / 1000.0, -3600, 3600);
        string title = "", artist = "";
        var lines = new List<Line>();
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string source = raw.Trim().TrimStart('\uFEFF');
            if (source.StartsWith("[ti:", StringComparison.OrdinalIgnoreCase) && source.EndsWith(']')) title = source[4..^1].Trim();
            if (source.StartsWith("[ar:", StringComparison.OrdinalIgnoreCase) && source.EndsWith(']')) artist = source[4..^1].Trim();
            if (Metadata.IsMatch(source)) continue;
            var starts = new List<double>();
            int contentStart = 0;
            foreach (Match stamp in Timestamp.Matches(source))
            {
                if (stamp.Index != contentStart || !TryTime(stamp, out double time)) break;
                starts.Add(time); contentStart += stamp.Length;
            }
            if (starts.Count == 0) continue;
            string content = source[contentStart..];
            var words = new List<Word>();
            var plain = new StringBuilder();
            int previousEnd = 0;
            foreach (Match stamp in WordTimestamp.Matches(content))
            {
                plain.Append(content, previousEnd, stamp.Index - previousEnd);
                if (TryTime(stamp, out double time)) words.Add(new Word(time - offset, plain.Length));
                previousEnd = stamp.Index + stamp.Length;
            }
            plain.Append(content, previousEnd, content.Length - previousEnd);
            string label = plain.ToString();
            // Preserve timed empty lines: they clear the old lyric during an interlude.
            foreach (double start in starts)
            {
                double repeatOffset = start - starts[0];
                lines.Add(new Line(Math.Max(0, start - offset), label,
                    words.Select(word => word with { Seconds = word.Seconds + repeatOffset }).ToArray()));
            }
        }
        // Equal-time lines are usually original text plus its translation.
        var grouped = lines.OrderBy(line => line.Seconds).GroupBy(line => line.Seconds).Select(group =>
        {
            var unique = group.Where(line => line.Text.Length > 0).DistinctBy(line => line.Text).ToArray();
            if (unique.Length == 0) return new Line(group.Key, "", Array.Empty<Word>());
            if (unique.Length == 1) return unique[0];
            return new Line(group.Key, string.Join(" / ", unique.Select(line => line.Text)), Array.Empty<Word>());
        }).ToArray();
        return new LrcDocument(grouped, title, artist);
    }

    private static bool TryTime(Match match, out double seconds)
    {
        seconds = 0;
        if (!int.TryParse(match.Groups[1].Value, out int minutes) ||
            !int.TryParse(match.Groups[2].Value, out int remainder) || remainder > 59) return false;
        string fraction = match.Groups[3].Value;
        double partial = fraction.Length == 0 ? 0 : int.Parse(fraction, CultureInfo.InvariantCulture) / Math.Pow(10, fraction.Length);
        seconds = minutes * 60 + remainder + partial;
        return true;
    }

    public Frame At(double position, double duration)
    {
        if (Lines.Count == 0) return new Frame("", "", 0);
        int low = 0, high = Lines.Count - 1, current = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (Lines[middle].Seconds <= position) { current = middle; low = middle + 1; }
            else high = middle - 1;
        }
        if (current < 0) return new Frame("", Lines[0].Text, 0);
        var line = Lines[current];
        string next = current + 1 < Lines.Count ? Lines[current + 1].Text : "";
        double end = current + 1 < Lines.Count ? Lines[current + 1].Seconds : duration > line.Seconds ? duration : line.Seconds + 5;
        double progress = end > line.Seconds ? Math.Clamp((position - line.Seconds) / (end - line.Seconds), 0, 1) : 1;
        double rate = end > position && end > line.Seconds ? 1 / (end - line.Seconds) : 0;
        double limit = 1;
        bool timedWords = line.Words.Count > 0 && line.Text.Length > 0 &&
            line.Words.Zip(line.Words.Skip(1), (first, second) => first.Seconds <= second.Seconds).All(ordered => ordered);
        if (timedWords)
        {
            // The renderer measures each displayed grapheme. Counting UTF-16
            // units would color emoji or combining accents at the wrong speed.
            int[] boundaries = StringInfo.ParseCombiningCharacters(line.Text);
            int TextElementIndex(int characterIndex)
            {
                if (characterIndex >= line.Text.Length) return boundaries.Length;
                int index = Array.BinarySearch(boundaries, Math.Max(0, characterIndex));
                return index >= 0 ? index : Math.Max(0, ~index - 1);
            }
            var points = line.Words;
            if (points[0].CharacterIndex > 0 && points[0].Seconds >= line.Seconds)
                points = new[] { new Word(line.Seconds, 0) }.Concat(points).ToArray();
            double completed = 0;
            rate = 0;
            limit = 0;
            for (int index = 0; index < points.Count; index++)
            {
                var word = points[index];
                if (position < word.Seconds) break;
                var following = index + 1 < points.Count ? points[index + 1] : new Word(end, line.Text.Length);
                int from = TextElementIndex(word.CharacterIndex), to = TextElementIndex(following.CharacterIndex);
                double fraction = following.Seconds > word.Seconds
                    ? Math.Clamp((position - word.Seconds) / (following.Seconds - word.Seconds), 0, 1) : 1;
                completed = from + Math.Max(0, to - from) * fraction;
                limit = to / (double)boundaries.Length;
                rate = following.Seconds > position && following.Seconds > word.Seconds
                    ? Math.Max(0, to - from) / (following.Seconds - word.Seconds) / boundaries.Length : 0;
            }
            progress = Math.Clamp(completed / boundaries.Length, 0, 1);
        }
        if (line.Text.Length == 0) { progress = 0; rate = 0; limit = 0; }
        return new Frame(line.Text, next, progress) { HighlightRate = rate, HighlightLimit = limit, HasWordTiming = timedWords };
    }
}
