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
        foreach (var raw in (lrc ?? "").Replace("\r", "").Split('\n'))
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
        var text = plain?.Trim() ?? "";
        if (text.Length == 0)
            text = grouped.Length > 0 ? string.Join('\n', grouped.Select(line => line.Text)) :
                string.Join('\n', (lrc ?? "").Replace("\r", "").Split('\n')
                    .Select(line => InfoTag().Replace(Timestamp().Replace(line, ""), "").Trim()).Where(line => line.Length > 0));
        return new(source, id, title, artist, text, grouped, instrumental);
    }

    [GeneratedRegex(@"\[(\d{1,4}):(\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();
    [GeneratedRegex(@"\[[a-z]+:[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex InfoTag();
}
