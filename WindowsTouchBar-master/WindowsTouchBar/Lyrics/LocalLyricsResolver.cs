using System.Text;

namespace WindowsTouchBar.Lyrics;

internal sealed class LocalLyricsResolver
{
    private const long MaximumLyricsBytes = 2 * 1024 * 1024;
    private readonly Action<string> log;
    private string? lastSelectedSource;
    public LocalLyricsResolver(Action<string>? log = null) => this.log = log ?? (_ => { });

    public LrcDocument? Resolve(FoobarPlaybackState state)
    {
        // A sidecar belongs to the track path. Reject explicit conflicting metadata,
        // which can occur when several cue tracks share one audio file.
        string? audioPath = state.Path;
        if (audioPath is not null && Uri.TryCreate(audioPath, UriKind.Absolute, out var uri) && uri.IsFile) audioPath = uri.LocalPath;
        if (!string.IsNullOrWhiteSpace(audioPath) && System.IO.Path.IsPathRooted(audioPath))
        {
            var adjacent = Read(System.IO.Path.ChangeExtension(audioPath, ".lrc"));
            if (adjacent is not null && MetadataMatches(adjacent, state)) return Select(adjacent, System.IO.Path.ChangeExtension(audioPath, ".lrc"));
        }
        if (!string.IsNullOrWhiteSpace(state.EmbeddedLyrics))
        {
            var embedded = LrcDocument.Parse(state.EmbeddedLyrics);
            if (embedded.HasLyrics) return Select(embedded, "embedded lyrics: " + (audioPath ?? state.Title));
        }
        if (string.IsNullOrWhiteSpace(state.LyricsDirectory) || !Directory.Exists(state.LyricsDirectory)) return null;
        string title = Normalize(state.Title ?? ""), artist = Normalize(state.Artist ?? "");
        if (title.Length == 0) return null;
        var exactNames = new HashSet<string>(StringComparer.Ordinal) { title };
        if (artist.Length > 0) { exactNames.Add(artist + title); exactNames.Add(title + artist); }
        var fallbackNames = FilenameFallbackNames(state);
        var candidates = new List<(LrcDocument Document, bool ArtistInName, string Source)>();
        foreach (string file in Directory.EnumerateFiles(state.LyricsDirectory, "*.lrc", SearchOption.TopDirectoryOnly).Take(4096))
        {
            string filename = Normalize(System.IO.Path.GetFileNameWithoutExtension(file));
            bool exactName = exactNames.Contains(filename);
            if (!exactName && !fallbackNames.Contains(filename)) continue;
            var document = Read(file);
            // Alternative filename orders or title-only cache names are accepted
            // only when both LRC tags confirm the whole untagged audio filename.
            if (document is not null && (exactName ? MetadataMatches(document, state) : MatchesFilenameIdentity(document, state)))
                candidates.Add((document, artist.Length > 0 && filename != title, file));
        }
        var specific = candidates.Where(candidate => candidate.ArtistInName ||
            (artist.Length > 0 && Normalize(candidate.Document.Artist) == artist)).ToArray();
        if (specific.Length == 1) return Select(specific[0].Document, specific[0].Source);
        if (specific.Length > 1) return Select(SameContentOrNone(specific.Select(item => item.Document)), specific[0].Source);
        if (candidates.Count == 1) return Select(candidates[0].Document, candidates[0].Source);
        if (candidates.Count > 1) return Select(SameContentOrNone(candidates.Select(item => item.Document)), candidates[0].Source);
        return null;
    }

    private LrcDocument? Select(LrcDocument? document, string source)
    {
        if (document is not null && source != lastSelectedSource)
        {
            lastSelectedSource = source;
            log("Lyrics source: " + source);
        }
        return document;
    }

    private static LrcDocument? SameContentOrNone(IEnumerable<LrcDocument> candidates)
    {
        var documents = candidates.ToArray();
        var first = documents[0];
        return documents.All(document => document.Lines.Select(line => (line.Seconds, line.Text))
            .SequenceEqual(first.Lines.Select(line => (line.Seconds, line.Text)))) ? first : null;
    }

    private static bool MetadataMatches(LrcDocument document, FoobarPlaybackState state) =>
        ((string.IsNullOrWhiteSpace(document.Title) || string.IsNullOrWhiteSpace(state.Title) || Normalize(document.Title) == Normalize(state.Title)) &&
        (string.IsNullOrWhiteSpace(document.Artist) || string.IsNullOrWhiteSpace(state.Artist) || Normalize(document.Artist) == Normalize(state.Artist))) ||
        MatchesFilenameIdentity(document, state);

    private static bool IsFilenameFallback(FoobarPlaybackState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Artist) || string.IsNullOrWhiteSpace(state.Path) || string.IsNullOrWhiteSpace(state.Title)) return false;
        string audioPath = state.Path;
        if (Uri.TryCreate(audioPath, UriKind.Absolute, out var uri) && uri.IsFile) audioPath = uri.LocalPath;
        return Normalize(System.IO.Path.GetFileNameWithoutExtension(audioPath)) == Normalize(state.Title);
    }

    private static bool MatchesFilenameIdentity(LrcDocument document, FoobarPlaybackState state)
    {
        if (!IsFilenameFallback(state)) return false;
        string title = Normalize(document.Title), artist = Normalize(document.Artist), filename = Normalize(state.Title!);
        // Do not guess an artist from a title-only match. Require both metadata
        // fields and an exact whole-name match in either conventional order.
        return title.Length > 0 && artist.Length > 0 && (filename == artist + title || filename == title + artist);
    }

    private static HashSet<string> FilenameFallbackNames(FoobarPlaybackState state)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!IsFilenameFallback(state)) return names;
        string title = state.Title!;
        for (int index = 1; index < title.Length - 1; index++)
        {
            if (title[index] is not ('-' or '–' or '—' or '_')) continue;
            string left = Normalize(title[..index]), right = Normalize(title[(index + 1)..]);
            if (left.Length == 0 || right.Length == 0) continue;
            names.Add(left); names.Add(right); names.Add(right + left);
        }
        return names;
    }

    private LrcDocument? Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumLyricsBytes) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            string text;
            try { text = reader.ReadToEnd(); }
            catch (DecoderFallbackException)
            {
                // Many older foobar lyric downloads use the Windows Chinese code page.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                stream.Position = 0;
                using var legacy = new StreamReader(stream, Encoding.GetEncoding(936), true, 1024, true);
                text = legacy.ReadToEnd();
            }
            var document = LrcDocument.Parse(text);
            return document.HasLyrics ? document : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            log("Local lyrics: " + error.Message);
            return null;
        }
    }

    internal static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
