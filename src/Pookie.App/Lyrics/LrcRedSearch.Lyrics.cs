using System.Net;
using System.Text.RegularExpressions;
using Pookie.Lyrics;

namespace Pookie.App;

internal sealed partial class LrcRedSearch
{
    internal async Task<LyricsDocument?> FindLyricsAsync(LyricsIdentity identity, bool allowPlain, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        token = timeout.Token;
        var tried = new HashSet<string>(StringComparer.Ordinal);
        if (NormalizeIsrc(identity.Isrc) is { } isrc)
        {
            tried.Add(isrc);
            var direct = await ReadLyricsAsync(isrc, identity, allowPlain, token).ConfigureAwait(false);
            if (direct != null) return direct;
        }

        var queries = identity.Queries();
        var primary = queries[0];
        var titleKeys = queries.Select(query => TitleKey(query.Title)).Where(key => key.Length > 0).ToHashSet();
        var artistKeys = queries.SelectMany(query => Artists(query.Artist)).Select(MatchKey)
            .Where(key => key.Length > 0 && key is not ("unknown" or "unknownartist")).ToHashSet();
        if (titleKeys.Count == 0 || artistKeys.Count == 0 || primary.Title.Length > 240) return null;

        var candidatesLeft = CandidateLimit;
        foreach (var query in new[] { (primary.Title + " " + primary.Artist).Trim(), primary.Title }.Distinct())
        {
            if (query.Length > 240) continue;
            var hits = await SearchAsync(query, token, byLyrics: false).ConfigureAwait(false);
            var document = await ReadMatchesAsync(hits.Where(hit => MatchesRecording(hit, identity, titleKeys, artistKeys))).ConfigureAwait(false);
            if (document != null || candidatesLeft == 0) return document;
        }

        // The title may be a line in the song. The search rank alone says nothing
        // about which recording it is, so validate the hit before fetching its LRC.
        if (primary.Title.Length < 6 || !double.IsFinite(identity.Duration) || identity.Duration <= 0) return null;
        var lyricHits = await SearchAsync(primary.Title, token, byLyrics: true).ConfigureAwait(false);
        var matches = lyricHits.Where(hit => hit != null && !string.IsNullOrWhiteSpace(hit.Title) && !string.IsNullOrWhiteSpace(hit.Artist) &&
            hit.Duration > 0 && Math.Abs(hit.Duration - identity.Duration) <= 2 && LyricsMatcher.CloseTitle(primary.Title, hit.Title) &&
            Artists(primary.Artist).Any(expected => Artists(hit.Artist).Any(actual => SameLyricArtist(expected, actual)))).ToArray();
        if (matches.Select(hit => (TitleKey(hit.Title), MatchKey(hit.Artist))).Distinct().Skip(1).Any()) return null;
        return await ReadMatchesAsync(matches).ConfigureAwait(false);

        async Task<LyricsDocument?> ReadMatchesAsync(IEnumerable<LrcRedHit> matches)
        {
            foreach (var hit in matches.OrderBy(hit => Math.Abs(hit.Duration - identity.Duration)))
            {
                if (NormalizeIsrc(hit.Isrc) is not { } candidate || !tried.Add(candidate)) continue;
                candidatesLeft--;
                var document = await ReadLyricsAsync(candidate, identity, allowPlain, token).ConfigureAwait(false);
                if (document != null || candidatesLeft == 0) return document;
            }
            return null;
        }
    }

    private static bool SameLyricArtist(string expected, string actual)
    {
        var left = MatchKey(expected);
        var right = MatchKey(actual);
        if (left is "" or "unknown" or "unknownartist") return false;
        if (left == right) return true;
        // A long transliterated credit can have a different spelling at the
        // label (e.g. Молли / Molly). Keep same-script artist names strict.
        var transliteratedLeft = left != LyricsMatcher.Normalize(expected).Replace(" ", "");
        var transliteratedRight = right != LyricsMatcher.Normalize(actual).Replace(" ", "");
        return transliteratedLeft != transliteratedRight && Math.Min(left.Length, right.Length) >= 10 &&
            LyricsMatcher.OneLetterApart(left, right);
    }

    private static bool MatchesRecording(LrcRedHit? hit, LyricsIdentity identity, HashSet<string> titles, HashSet<string> artists)
    {
        if (hit == null || string.IsNullOrWhiteSpace(hit.Title) || string.IsNullOrWhiteSpace(hit.Artist) ||
            !double.IsFinite(hit.Duration) || hit.Duration <= 0) return false;
        if (identity.Duration > 0 && double.IsFinite(identity.Duration) &&
            Math.Abs(hit.Duration - identity.Duration) > Math.Clamp(identity.Duration * .03, 4, 8)) return false;
        return titles.Contains(TitleKey(hit.Title)) && Artists(hit.Artist).Select(MatchKey).Any(artists.Contains);
    }

    private async Task<LyricsDocument?> ReadLyricsAsync(string isrc, LyricsIdentity identity, bool allowPlain, CancellationToken token)
    {
        using var response = await SendAsync($"s/{isrc}.lrc", token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType == "text/html")
            throw new HttpRequestException("lrc.red returned an HTML page instead of an LRC file.");
        await response.Content.LoadIntoBufferAsync(MaxResponseBytes, token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var document = LrcParser.Parse(text, null, Name, null, identity.Title, identity.Artist);
        if (identity.TimingAvailable && document.Status == LyricsStatus.Synced &&
            (!double.IsFinite(identity.Duration) || identity.Duration <= 0 || document.Lines[^1].Start <= identity.Duration + 2))
            return document;
        return allowPlain && !string.IsNullOrWhiteSpace(document.PlainText) ? document with { Lines = [] } : null;
    }

    internal static string? NormalizeIsrc(string? value)
    {
        var code = value?.Trim().Replace("-", "").ToUpperInvariant();
        return code != null && IsrcPattern().IsMatch(code) ? code : null;
    }

    [GeneratedRegex(@"\A[A-Z]{2}[A-Z0-9]{3}[0-9]{7}\z")]
    private static partial Regex IsrcPattern();
}
