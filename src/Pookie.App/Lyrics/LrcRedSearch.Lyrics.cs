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
            foreach (var hit in hits.Where(hit => MatchesRecording(hit, identity, titleKeys, artistKeys))
                         .OrderBy(hit => Math.Abs(hit.Duration - identity.Duration)))
            {
                if (NormalizeIsrc(hit.Isrc) is not { } candidate || !tried.Add(candidate)) continue;
                var document = await ReadLyricsAsync(candidate, identity, allowPlain, token).ConfigureAwait(false);
                if (document != null) return document;
                if (--candidatesLeft == 0) return null;
            }
        }
        return null;
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
