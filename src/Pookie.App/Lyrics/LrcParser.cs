using System.Globalization;
using System.Text.RegularExpressions;

namespace Pookie.Lyrics;

public static partial class LrcParser
{
    public static LyricsDocument Parse(string? lrc, string? plain, string source, long? id,
        string title, string artist, bool instrumental = false)
    {
        var lines = new List<LyricsLine>();
        var offsetMatch = OffsetTag().Match(lrc ?? "");
        var offset = offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out var parsed) ? parsed / 1000d : 0;
        // Enhanced LRC can mark words with angle brackets within a timed line.
        // Keep the line timestamp and the original spacing of the visible text.
        var rawLines = WithoutWordTimestamps(lrc ?? "").Replace("\r", "").Split('\n');
        foreach (var raw in rawLines)
        {
            var tags = Timestamp().Matches(raw);
            if (tags.Count == 0) continue;
            var content = Timestamp().Replace(raw, "").Trim();
            foreach (Match tag in tags)
            {
                if (!int.TryParse(tag.Groups[1].Value, out var minutes) ||
                    !double.TryParse(tag.Groups[2].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) || seconds >= 60)
                    continue;
                lines.Add(new(Math.Max(0, minutes * 60 + seconds - offset), content));
            }
        }
        var grouped = lines.OrderBy(line => line.Start).GroupBy(line => line.Start)
            .Select(group => new LyricsLine(group.Key, string.Join('\n', group.Select(line => line.Text).Distinct()))).ToArray();
        // A file containing only timestamped silence is not a synced text.
        if (!grouped.Any(line => line.Text.Length > 0)) grouped = [];
        var text = WithoutWordTimestamps(plain ?? "").Trim();
        if (text.Length == 0)
            text = grouped.Length > 0 ? string.Join('\n', grouped.Select(line => line.Text)) :
                string.Join('\n', rawLines
                    .Select(line => InfoTag().Replace(Timestamp().Replace(line, ""), "").Trim()).Where(line => line.Length > 0));
        return new(source, id, title, artist, text, grouped, instrumental);
    }

    internal static LyricsDocument CleanCachedWordTimestamps(LyricsDocument document)
    {
        var plain = WithoutWordTimestamps(document.PlainText);
        LyricsLine[]? cleaned = null;
        for (var i = 0; i < document.Lines.Length; i++)
        {
            var line = document.Lines[i];
            var text = WithoutWordTimestamps(line.Text);
            if (text == line.Text) continue;
            cleaned ??= (LyricsLine[])document.Lines.Clone();
            cleaned[i] = line with { Text = text.Trim() };
        }
        if (cleaned == null && plain == document.PlainText) return document;
        if (cleaned != null && cleaned.All(line => line.Text.Length == 0)) cleaned = [];
        return document with { PlainText = plain.Trim(), Lines = cleaned ?? document.Lines };
    }

    private static string WithoutWordTimestamps(string text) => WordTimestamp().Replace(text, "");

    [GeneratedRegex(@"\[(\d{1,4}):(\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"<\d{1,4}:\d{1,2}(?:\.\d{1,3})?>")]
    private static partial Regex WordTimestamp();
    [GeneratedRegex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();
    [GeneratedRegex(@"\[[a-z]+:[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex InfoTag();
}
