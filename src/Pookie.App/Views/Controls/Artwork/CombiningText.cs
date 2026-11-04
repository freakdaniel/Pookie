using System.Globalization;
using System.Text;

namespace Pookie.App;

// Bound glyph stacking, not the document: layout inlines hide only abusive
// clusters, retaining every original UTF-16 offset for selection and editing.
internal static class CombiningText
{
    private const int MaximumMarks = 8;
    private static bool IsMark(UnicodeCategory category) => category is UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    internal static bool ContainsMark(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes()) if (IsMark(Rune.GetUnicodeCategory(rune))) return true;
        return false;
    }

    internal static IReadOnlyList<(int Start, int Length)> HiddenRuns(ReadOnlySpan<char> text)
    {
        List<(int Start, int Length)>? result = null;
        Span<int> seen = stackalloc int[MaximumMarks];
        var count = 0; var start = -1; var end = 0; var abusive = false; var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var length = rune.Utf16SequenceLength;
            if (IsMark(category))
            {
                if (start < 0) start = offset;
                end = offset + length;
                if (count == MaximumMarks) abusive = true;
                else
                {
                    var repeats = 0;
                    for (var index = 0; index < count; index++) if (seen[index] == rune.Value) repeats++;
                    if (repeats >= 2) abusive = true;
                    seen[count++] = rune.Value;
                }
            }
            else if (category != UnicodeCategory.Format)
            {
                if (abusive) (result ??= []).Add((start, end - start));
                start = -1; count = 0; abusive = false;
            }
            offset += length;
        }
        if (abusive) (result ??= []).Add((start, end - start));
        if (result == null) return [];
        // Inline spans must cover whole Unicode text elements. Hiding a broken
        // cluster also keeps caret movement atomic, rather than creating
        // hundreds of invisible positions inside its original mark sequence.
        var clusters = new List<(int Start, int Length)>();
        var candidate = 0;
        for (var position = 0; position < text.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(text[position..]);
            while (candidate < result.Count && result[candidate].Start + result[candidate].Length <= position) candidate++;
            if (candidate == result.Count) break;
            if (result[candidate].Start < position + length) clusters.Add((position, length));
            position += length;
        }
        return clusters;
    }
}
