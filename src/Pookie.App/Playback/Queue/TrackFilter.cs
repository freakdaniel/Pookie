using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Pookie.SoundCloud;

namespace Pookie.App.Playback;

internal static class TrackFilter
{
    private sealed record SearchFields(string Title, string Author);
    private static readonly ConditionalWeakTable<SoundCloudTrack, SearchFields> fields = new();

    internal static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    internal static bool Matches(SoundCloudTrack track, string? text) => CreateMatcher(text)(track);

    // Prepare the query once for a library pass. Weak keys keep the index bounded
    // by the actual track lifetime, including queues and recycled library pages.
    internal static Func<SoundCloudTrack, bool> CreateMatcher(string? text)
    {
        var words = SearchKey(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            if (Normalize(text) is not { } literal) return static _ => true;
            return track => track.Title.Contains(literal, StringComparison.OrdinalIgnoreCase) ||
                track.Author.Contains(literal, StringComparison.OrdinalIgnoreCase);
        }
        return track =>
        {
            var entry = fields.GetValue(track, static value => new(SearchKey(value.Title), SearchKey(value.Author)));
            foreach (var word in words)
                if (!entry.Title.Contains(word, StringComparison.Ordinal) && !entry.Author.Contains(word, StringComparison.Ordinal)) return false;
            return true;
        };
    }

    internal static bool MatchesText(string value, string? text)
    {
        var key = SearchKey(value);
        var words = SearchKey(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? Normalize(text) is not { } literal || value.Contains(literal, StringComparison.OrdinalIgnoreCase)
            : words.All(word => key.Contains(word, StringComparison.Ordinal));
    }

    private static string SearchKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        // Compatibility normalization also handles full-width and decorative Latin
        // letters. Preserve Cyrillic distinctions (й/и, ў/у); only ё folds to е.
        var source = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace('ё', 'е').Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var result = new StringBuilder(source.Length);
        var latin = false;
        foreach (var rune in source.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                if (!latin && rune.Value is not (0x301 or 0x341)) result.Append(rune);
                continue;
            }
            latin = rune.Value is >= 'a' and <= 'z';
            if (Rune.IsLetterOrDigit(rune)) result.Append(rune);
            else if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
        }
        return result.ToString().Normalize(NormalizationForm.FormC).Trim();
    }
}
