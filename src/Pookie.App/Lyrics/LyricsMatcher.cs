using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pookie.Lyrics;

public static partial class LyricsMatcher
{
    // Fold styled Unicode letters before parsing tags or building provider queries.
    private static string CompatibleText(string? text) => (text ?? "").Normalize(NormalizationForm.FormKC);
    public static string Normalize(string? text) => Whitespace().Replace(Punctuation().Replace(
        CompatibleText(text).ToLowerInvariant().Replace('ё', 'е'), " "), " ").Trim();
    public static string SearchTitle(string text) => FoldSpeedTags(Production().Replace(CompatibleText(text), "")).Trim();
    public static string WithoutParentheses(string text)
    {
        text = CompatibleText(text);
        // Remove nested groups from the inside out, retaining the title around them.
        while (Parentheses().IsMatch(text)) text = Parentheses().Replace(text, " ");
        return Whitespace().Replace(text, " ").Trim();
    }
    private static IEnumerable<LyricsQuery> MatchingQueries(LyricsIdentity identity) =>
        identity.Queries().Concat(identity.Queries(stripParentheses: true)).Distinct();
    internal static (string Artist, string Title)? SplitArtistTitle(string text)
    {
        // Separators in an edit credit or bracketed metadata cannot split artist/title.
        var suffix = SpeedTags(text).Select(tag => tag.Index).DefaultIfEmpty(text.Length).Min();
        var depth = 0;
        var scanned = 0;
        foreach (Match separator in ArtistSeparator().Matches(text))
        {
            if (separator.Index >= suffix) break;
            while (scanned < separator.Index)
            {
                var character = text[scanned++];
                if (character is '(' or '[' or '{') depth++;
                else if (character is ')' or ']' or '}') depth = Math.Max(0, depth - 1);
            }
            if (depth != 0) continue;
            var artist = text[..separator.Index].Trim();
            var title = text[(separator.Index + separator.Length)..].Trim();
            if (artist.Length > 0 && title.Length > 0) return (artist, title);
        }
        return null;
    }
    private static string NormalizeTitle(string? title) => Normalize(FoldSpeedTags(CompatibleText(title), canonical: true));
    private static string Versions(string text)
    {
        text = CompatibleText(text);
        // Names in "by Live/Remix" are credits, not recording-version flags.
        var versionText = FoldSpeedTags(text, canonical: true);
        return string.Join(',', Version().Matches(versionText.ToLowerInvariant()).Select(match => Whitespace().Replace(match.Value, " "))
            .Concat(SpeedTags(text).Select(_ => "spedup")).Distinct().Order());
    }

    private static string FoldSpeedTags(string text, bool canonical = false)
    {
        foreach (var tag in SpeedTags(text).Reverse())
        {
            var end = tag.Index + tag.Length;
            var separator = end < text.Length && text[end] is '(' or '[' or '{' ? " " : "";
            text = text.Remove(tag.Index, tag.Length).Insert(tag.Index, (canonical ? "spedup" : tag.Value) + separator);
        }
        return text;
    }

    private static IEnumerable<(int Index, int Length, string Value)> SpeedTags(string text)
    {
        var words = TagWord().Matches(text).Select(word => (Word: word, Value: FoldTagWord(word.Value)))
            .Where(word => word.Value.Length > 0).ToArray();
        var consumedUntil = 0;
        for (var index = 0; index < words.Length; index++)
        {
            var (word, value) = words[index];
            if (word.Index < consumedUntil) continue;
            var end = word.Index + word.Length;
            if (value is "speed" or "sped" && index + 1 < words.Length && words[index + 1].Value == "up" &&
                IsTagSpace(text[end..words[index + 1].Word.Index]))
            { value += " up"; end = words[index + 1].Word.Index + words[index + 1].Word.Length; }
            if (value is not ("speed" or "speedup" or "speed up" or "spedup" or "sped up")) continue;

            var start = word.Index;
            var effectPrefix = EffectPrefix().Match(text[..start]);
            if (effectPrefix.Success)
            {
                start = effectPrefix.Index;
                value = effectPrefix.Value + value;
            }
            var speedEnd = end;
            end += EffectSuffix().Match(text[end..]).Length;
            var credit = EditorCredit().Match(text[end..]);
            if (credit.Success && Normalize(credit.Groups["editor"].Value).Length > 0) end += credit.Length;
            value += text[speedEnd..end];

            var prefix = text[..start];
            var opening = prefix.LastIndexOfAny(['(', '[', '{']);
            var closing = prefix.LastIndexOfAny([')', ']', '}']);
            // A bracketed tag occupies its group; "(tg: speed)" is uploader metadata.
            if (opening > closing)
            {
                if (Normalize(prefix[(opening + 1)..]).Length != 0) continue;
                prefix = prefix[..opening];
            }
            // Only known effect chains and an optional editor credit can extend the suffix.
            if (Normalize(WithoutParentheses(prefix)).Length == 0 ||
                Normalize(WithoutParentheses(text[end..])).Length != 0) continue;
            foreach (var rune in text.AsSpan(end).EnumerateRunes())
            {
                if (!Rune.IsWhiteSpace(rune) && !IsTagMark(rune) && Rune.GetUnicodeCategory(rune) is not
                    (UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol)) break;
                end += rune.Utf16SequenceLength;
            }
            consumedUntil = end;
            yield return (start, end - start, value);
        }
    }

    private static bool IsTagMark(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format;

    private static bool IsTagSpace(string text)
    {
        foreach (var rune in text.EnumerateRunes()) if (!Rune.IsWhiteSpace(rune) && !IsTagMark(rune)) return false;
        return true;
    }

    private static string FoldTagWord(string text)
    {
        var folded = new StringBuilder();
        foreach (var rune in text.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (IsTagMark(rune)) continue;
            var lower = Rune.ToLowerInvariant(rune);
            // A deliberately small alphabet for speed-up tags, never applied to song/artist text.
            folded.Append(lower.Value switch
            {
                'ꜱ' or 'ѕ' => "s",
                'ᴘ' or 'р' or 'ρ' => "p",
                'ᴇ' or 'е' or 'ε' or 'є' => "e",
                'ᴅ' or 'ԁ' => "d",
                'ᴜ' or 'υ' or 'ս' => "u",
                _ => lower.ToString()
            });
        }
        return folded.ToString();
    }

    public static LyricsIdentity? SpedUpOriginal(LyricsIdentity identity)
    {
        // In "Artist - Speed", Speed is the entire song name, not a suffix.
        if (!SpeedTags(identity.Queries()[0].Title).Any()) return null;
        var title = SearchTitle(identity.Title);
        var tags = SpeedTags(title).ToArray();
        if (tags.Length == 0) return null;
        foreach (var tag in tags.Reverse()) title = title.Remove(tag.Index, tag.Length);
        title = EmptyGroup().Replace(title, " ");
        title = Whitespace().Replace(title, " ").Trim(' ', '-', '—', '–', '_', '+', '#', '|');
        return Normalize(WithoutParentheses(title)).Length > 0 ? identity with { Title = title } : null;
    }

    public static LyricsCandidate? ChooseSpedUpOriginal(LyricsIdentity identity, IEnumerable<LyricsCandidate> candidates)
    {
        var original = SpedUpOriginal(identity);
        if (original == null) return null;
        return candidates.DistinctBy(candidate => candidate.Id)
            .Select(candidate => (Candidate: candidate, Score: OriginalScore(original, candidate), Document: candidate.ToDocument()))
            .Where(item => item.Score >= 0 && !item.Document.Instrumental && !string.IsNullOrWhiteSpace(item.Document.PlainText))
            .OrderByDescending(item => CanScale(identity, item.Candidate, item.Document))
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.Duration)
            .ThenBy(item => item.Candidate.Id)
            .Select(item => item.Candidate).FirstOrDefault();
    }

    private static double OriginalScore(LyricsIdentity identity, LyricsCandidate candidate)
    {
        var targetKnown = identity.Duration > 0 && double.IsFinite(identity.Duration);
        var sourceKnown = candidate.Duration > 0 && double.IsFinite(candidate.Duration);
        if (!double.IsFinite(candidate.Duration) || candidate.Duration < 0) return -1;
        if (targetKnown && sourceKnown && (candidate.Duration <= identity.Duration || candidate.Duration > identity.Duration * 2)) return -1;
        // Duration cannot establish the original recording, so require a much
        // closer title/artist match before estimating its playback rate.
        return Score(identity with { Duration = sourceKnown ? candidate.Duration : 0 }, candidate, .94, .9);
    }

    private static bool CanScale(LyricsIdentity identity, LyricsCandidate candidate, LyricsDocument document) =>
        identity.TimingAvailable && identity.Duration > 0 && double.IsFinite(identity.Duration) &&
        candidate.Duration > identity.Duration && candidate.Duration <= identity.Duration * 2 &&
        double.IsFinite(candidate.Duration) && document.Status == LyricsStatus.Synced &&
        document.Lines[^1].Start <= candidate.Duration + 2;

    public static LyricsDocument SpedUpDocument(LyricsIdentity identity, LyricsCandidate candidate)
    {
        var document = candidate.ToDocument();
        if (SpedUpOriginal(identity) == null) return document;
        if (!CanScale(identity, candidate, document)) return document with { Lines = [] };
        var factor = identity.Duration / candidate.Duration;
        return document with { Lines = document.Lines.Select(line => line with { Start = line.Start * factor }).ToArray() };
    }

    public static bool Matches(LyricsIdentity identity, LyricsCandidate candidate) => Score(identity, candidate) >= 0;
    public static bool IsExact(LyricsIdentity identity, LyricsCandidate candidate) => Matches(identity, candidate) &&
        MatchingQueries(identity).Any(query => NormalizeTitle(query.Title) == NormalizeTitle(candidate.TrackName) &&
            ArtistSimilarity(query.Artist, candidate.ArtistName) == 1) && Math.Abs(identity.Duration - candidate.Duration) <= 2;

    public static LyricsCandidate? Choose(LyricsIdentity identity, IEnumerable<LyricsCandidate> candidates) => candidates
        .DistinctBy(candidate => candidate.Id)
        .Select(candidate => (Candidate: candidate, Score: Score(identity, candidate), Document: candidate.ToDocument()))
        .Where(item => item.Score >= 0 && (item.Document.Instrumental || !string.IsNullOrWhiteSpace(item.Document.PlainText)))
        // Timed lyrics win among plausible versions; duration still strongly affects their rank.
        .OrderByDescending(item => item.Document.Status == LyricsStatus.Synced)
        .ThenByDescending(item => item.Score)
        .ThenBy(item => Math.Abs(identity.Duration - item.Candidate.Duration))
        .ThenBy(item => item.Candidate.Id)
        .Select(item => item.Candidate).FirstOrDefault();

    private static double Score(LyricsIdentity identity, LyricsCandidate candidate, double titleMinimum = .78, double artistMinimum = .75)
    {
        if (Versions(identity.Queries()[0].Title) != Versions(candidate.TrackName ?? "")) return -1;
        var tolerance = Math.Clamp(identity.Duration * .05, 5, 15);
        var durationKnown = identity.Duration > 0 && double.IsFinite(identity.Duration);
        var difference = Math.Abs(identity.Duration - candidate.Duration);
        if (durationKnown && (!double.IsFinite(candidate.Duration) || candidate.Duration <= 0 || difference > tolerance)) return -1;
        var best = -1d;
        foreach (var query in MatchingQueries(identity))
        {
            var title = Similarity(NormalizeTitle(query.Title), NormalizeTitle(candidate.TrackName));
            var artist = ArtistSimilarity(query.Artist, candidate.ArtistName);
            if (title < titleMinimum || artist < artistMinimum || !durationKnown && (title < 1 || artist < 1)) continue;
            var duration = durationKnown ? 1 - difference / tolerance : .5;
            var album = string.IsNullOrWhiteSpace(identity.Album) ? .5 : Similarity(Normalize(identity.Album), Normalize(candidate.AlbumName));
            best = Math.Max(best, title * .4 + artist * .25 + duration * .3 + album * .05);
        }
        return best;
    }

    private static double ArtistSimilarity(string expected, string? actual)
    {
        var normalized = Normalize(expected);
        if (normalized.Length == 0 || normalized is "unknown artist" or "unknown") return 0;
        var parts = Regex.Split(actual ?? "", @"\s*(?:&|,|\bfeat\.?|\bft\.?)\s*", RegexOptions.IgnoreCase);
        return Math.Max(Similarity(normalized, Normalize(actual)), parts.Max(artist => Similarity(normalized, Normalize(artist))));
    }

    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        if (left == right) return 1;
        // Bounded edit distance handles small spelling differences without accepting
        // an unrelated uploader or a different recording solely by its duration.
        if (left.Length > 256 || right.Length > 256) return 0;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        var edit = 1 - previous[right.Length] / (double)Math.Max(left.Length, right.Length);
        var a = left.Split(' ').ToHashSet(); var b = right.Split(' ').ToHashSet();
        var tokens = 2d * a.Intersect(b).Count() / (a.Count + b.Count);
        return Math.Max(edit, tokens);
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex Punctuation();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex Parentheses();
    [GeneratedRegex(@"[\[(]\s*prod\.?[^\])]*[\])]", RegexOptions.IgnoreCase)]
    private static partial Regex Production();
    [GeneratedRegex(@"[\p{L}\p{Nd}\p{M}\p{Cf}]+")]
    private static partial Regex TagWord();
    [GeneratedRegex(@"\s+[—–-]\s+")]
    private static partial Regex ArtistSeparator();
    private const string Effect = @"(?:reverb(?:ed)?|echo|bass\s*boost(?:ed)?|8d(?:\s+audio)?)(?:\s+(?:edit|version))?(?![\p{L}\p{Nd}])";
    private const string EffectJoin = @"(?:\s*(?:[+&/,|–-]|\band\b|\bx\b|\bwith\b)\s*|\s+)";
    [GeneratedRegex("^(?:" + EffectJoin + Effect + ")*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EffectSuffix();
    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])" + Effect + "(?:" + EffectJoin + Effect + @")*\s*(?:[+&/,|–-]|\band\b|\bx\b)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EffectPrefix();
    [GeneratedRegex(@"^(?:\s+|\s*[-–—|,]\s*)by\s+(?<editor>[^()\[\]{}]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EditorCredit();
    [GeneratedRegex(@"\(\s*\)|\[\s*\]|\{\s*\}")]
    private static partial Regex EmptyGroup();
    [GeneratedRegex(@"\b(?:slowed(?:\s*down)?|remix|live|cover|instrumental|acoustic|nightcore)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Version();
}
