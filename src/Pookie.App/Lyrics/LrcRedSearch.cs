using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Pookie.Lyrics;
using Pookie.SoundCloud;

namespace Pookie.App;

internal enum LyricPhraseMatch { Unverified, Near, Exact }

internal sealed record LyricSearchMatch(LibraryItem Item, LyricPhraseMatch Phrase);

internal sealed partial class LrcRedSearch(HttpClient http)
{
    internal const string Source = "https://lrc.red/";
    internal const string Name = "lrc.red";
    private const int CandidateLimit = 3;
    private const int MaxResponseBytes = 128 * 1024;
    private static readonly string[] Latin =
        ["a", "b", "v", "g", "d", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p",
         "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya"];
    private long retryAfter;

    internal async Task<LyricSearchMatch[]> FindAsync(string query,
        Func<string, CancellationToken, Task<LibraryPage>> searchTracks, CancellationToken token)
    {
        if (query.Length is < 8 or > 240 || query.Contains("://", StringComparison.Ordinal)) return [];
        var words = LyricsMatcher.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return [];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var budget = CandidateLimit + 2;
        Task<LibraryPage> SearchWithinBudget(string metadata, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return Interlocked.Decrement(ref budget) >= 0
                ? searchTracks(metadata, cancellation) : Task.FromResult(new LibraryPage([], null));
        }
        var primary = await FindPhraseAsync(query).ConfigureAwait(false);
        var ranked = primary;
        if (words.Length > 3 && !primary.Any(match => match.Phrase == LyricPhraseMatch.Exact) && !timeout.IsCancellationRequested)
        {
            var fragment = await FindPhraseAsync(string.Join(' ', words.TakeLast(3))).ConfigureAwait(false);
            // A fragment can retrieve an alternate wording. Compare it against the original query
            // before letting it displace any results from the complete phrase.
            ranked = primary.Concat(fragment.Where(match => primary.Length == 0 || match.Phrase != LyricPhraseMatch.Unverified))
                .OrderByDescending(match => match.Phrase).DistinctBy(match => match.Item.Key).ToArray();
        }
        token.ThrowIfCancellationRequested();
        return ranked;

        async Task<LyricSearchMatch[]> FindPhraseAsync(string phrase)
        {
            var recordings = await ResolveAsync(phrase, SearchWithinBudget, timeout.Token).ConfigureAwait(false);
            var matches = await Task.WhenAll(recordings.Select(async recording => new LyricSearchMatch(recording.Item,
                await CheckPhraseAsync(recording.Hit, words, timeout.Token).ConfigureAwait(false)))).ConfigureAwait(false);
            return matches.OrderByDescending(match => match.Phrase).ToArray();
        }
    }

    private async Task<LyricPhraseMatch> CheckPhraseAsync(LrcRedHit hit, string[] words, CancellationToken token)
    {
        var preview = PhraseMatch(hit.Line, words);
        if (preview == LyricPhraseMatch.Exact || words.Length < 3 || NormalizeIsrc(hit.Isrc) is not { } isrc) return preview;
        // Search previews can end before the chorus. Limit this optional check so the
        // regular search and already resolved recordings can still be displayed promptly.
        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(token);
        lookup.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            var document = await ReadDocumentAsync(isrc, hit.Title, hit.Artist, lookup.Token).ConfigureAwait(false);
            var full = PhraseMatch(document?.PlainText, words);
            return full > preview ? full : preview;
        }
        catch (Exception error) when (Unavailable(error)) { return preview; }
    }

    private static LyricPhraseMatch PhraseMatch(string? text, string[] query)
    {
        var words = LyricsMatcher.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var best = LyricPhraseMatch.Unverified;
        for (var start = 0; start <= words.Length - query.Length; start++)
        {
            var window = words.AsSpan(start, query.Length);
            var i = 0;
            while (i < query.Length && query[i] == window[i]) i++;
            if (i == query.Length) return LyricPhraseMatch.Exact;
            if (query.Length >= 4 && window[(i + 1)..].SequenceEqual(query.AsSpan(i + 1)) &&
                LyricsMatcher.OneLetterApart(window[i], query[i], minimumLength: 2))
                best = LyricPhraseMatch.Near;
        }
        return best;
    }

    private async Task<(LrcRedHit Hit, LibraryItem Item)[]> ResolveAsync(string query,
        Func<string, CancellationToken, Task<LibraryPage>> searchTracks, CancellationToken token)
    {
        LrcRedHit[] hits;
        try { hits = await SearchAsync(query, token).ConfigureAwait(false); }
        catch (Exception error) when (Unavailable(error)) { return []; }

        var candidates = hits.Where(hit => hit != null && !string.IsNullOrWhiteSpace(hit.Title) && !string.IsNullOrWhiteSpace(hit.Artist))
            .DistinctBy(hit => (TitleKey(hit.Title), MatchKey(Artists(hit.Artist)[0])))
            .Take(CandidateLimit);
        var matches = await Task.WhenAll(candidates.Select(async (hit, index) =>
        {
            try
            {
                var title = Remaster().Replace(hit.Title, "").Trim();
                var items = await searchTracks(Artists(hit.Artist)[0] + " " + title, token).ConfigureAwait(false);
                var best = BestTrack(hit, items.Items);
                // A title-only lookup can recover an uploader or artist alias.
                best ??= index == 0 ? BestTrack(hit, (await searchTracks(title, token).ConfigureAwait(false)).Items) : null;
                return (Hit: hit, Item: best);
            }
            catch (Exception error) when (Unavailable(error)) { return (Hit: hit, Item: null); }
        })).ConfigureAwait(false);

        var recordings = new List<(LrcRedHit Hit, LibraryItem Item)>();
        foreach (var (hit, item) in matches)
        {
            if (item?.Track is not { } track) continue;
            var duplicate = recordings.FindIndex(previous => previous.Item.Key == item.Key || SameRecording(previous.Hit, hit));
            if (duplicate < 0) recordings.Add((hit, item));
            else if (SameIsrc(hit, track) && !SameIsrc(recordings[duplicate].Hit, recordings[duplicate].Item.Track!))
                recordings[duplicate] = (hit, item);
        }
        return recordings.ToArray();
    }

    private static bool SameRecording(LrcRedHit first, LrcRedHit second) =>
        Math.Abs(first.Duration - second.Duration) <= 4 && TitleKey(first.Title) == TitleKey(second.Title) &&
        Artists(first.Artist).Select(MatchKey).Intersect(Artists(second.Artist).Select(MatchKey)).Any();

    private static bool SameIsrc(LrcRedHit hit, SoundCloudTrack track) =>
        !string.IsNullOrWhiteSpace(hit.Isrc) &&
        string.Equals(hit.Isrc, track.PublisherMetadata?.Isrc, StringComparison.OrdinalIgnoreCase);

    private async Task<LrcRedHit[]> SearchAsync(string query, CancellationToken token, bool byLyrics = true)
    {
        var path = (byLyrics ? "search.json?in=lyrics&q=" : "search.json?q=") + Uri.EscapeDataString(query);
        using var response = await SendAsync(path, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(MaxResponseBytes, token).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync(LrcRedJson.Default.LrcRedResults, token).ConfigureAwait(false))?.Hits ?? [];
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken token)
    {
        var remaining = Interlocked.Read(ref retryAfter) - DateTimeOffset.UtcNow.UtcTicks;
        if (remaining > 0) throw new LyricsRateLimitException(TimeSpan.FromTicks(remaining));
        using var request = new HttpRequestMessage(HttpMethod.Get, Source + path);
        request.Headers.UserAgent.ParseAdd("Pookie/0.1 (+https://github.com/freakdaniel/Pookie; contact whoisfreak@icloud.com)");
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return response;
        using (response)
        {
            var received = DateTimeOffset.UtcNow;
            var until = response.Headers.RetryAfter?.Date ??
                received + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            var retryAt = Math.Max(received.AddSeconds(1).UtcTicks, until.UtcTicks);
            Interlocked.Exchange(ref retryAfter, retryAt);
            throw new LyricsRateLimitException(TimeSpan.FromTicks(retryAt - received.UtcTicks));
        }
    }

    private static bool Unavailable(Exception error) => error is HttpRequestException or IOException or JsonException or
        SoundCloudException or OperationCanceledException or NotSupportedException or ObjectDisposedException;

    private static LibraryItem? BestTrack(LrcRedHit hit, IEnumerable<LibraryItem> items)
    {
        var title = TitleKey(hit.Title);
        var artists = Artists(hit.Artist).Select(MatchKey).ToHashSet();
        return items.Where(item => item.Track is { Id: > 0 } track &&
                track.Access is not ("blocked" or "preview") && track.Policy is not ("BLOCK" or "SNIP"))
            .Select(item => (Item: item, Rank: Rank(item.Track!)))
            .Where(candidate => candidate.Rank >= 0)
            .OrderBy(candidate => candidate.Rank == 0 ? 0 : 1)
            .ThenByDescending(candidate => candidate.Item.Track!.PlaybackCount)
            .ThenBy(candidate => candidate.Rank)
            .Select(candidate => candidate.Item).FirstOrDefault();

        double Rank(SoundCloudTrack track)
        {
            if (SameIsrc(hit, track)) return 0;
            var duration = track.FullDuration > 0 ? track.FullDuration / 1000 : track.DurationSeconds;
            if (!double.IsFinite(duration) || !double.IsFinite(hit.Duration) || duration <= 0 || hit.Duration <= 0) return -1;
            var difference = Math.Abs(duration - hit.Duration);
            if (difference > Math.Clamp(hit.Duration * .03, 4, 10)) return -1;

            var tagged = LyricsMatcher.SplitArtistTitle(track.Title);
            var published = track.PublisherMetadata?.Artist;
            var knownArtist = !string.IsNullOrWhiteSpace(published) ? published : track.Author;
            if (tagged is { } parts && TitleKey(parts.Title) == title &&
                Artists(!string.IsNullOrWhiteSpace(published) ? published : parts.Artist).Select(MatchKey).Any(artists.Contains))
                return 1 + difference;
            return TitleKey(track.Title) == title && Artists(knownArtist).Select(MatchKey).Any(artists.Contains)
                ? 1 + difference : -1;
        }
    }

    private static string[] Artists(string text)
    {
        var parts = ArtistSeparator().Split(text).Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return parts.Length == 0 ? [text] : parts;
    }

    private static string TitleKey(string text) => MatchKey(Remaster().Replace(text, ""));

    private static string MatchKey(string text)
    {
        const string cyrillic = "абвгдежзийклмнопрстуфхцчшщъыьэюя";
        var normalized = LyricsMatcher.Normalize(text);
        var key = new StringBuilder(normalized.Length);
        foreach (var letter in normalized)
        {
            var index = cyrillic.IndexOf(letter);
            if (index >= 0) key.Append(Latin[index]);
            else if (letter != ' ') key.Append(letter);
        }
        return key.ToString();
    }

    [GeneratedRegex(@"\s*(?:,|\s+&\s+|\s+(?:feat|ft)\.?\s+)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSeparator();

    [GeneratedRegex(@"\s*[\[(]remaster(?:ed)?(?:\s+\d{4})?[\])]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Remaster();
}

internal sealed record LrcRedHit(string Title, string Artist, string? Isrc, double Duration, string? Line = null);
internal sealed record LrcRedResults(LrcRedHit[]? Hits);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LrcRedResults))]
internal sealed partial class LrcRedJson : JsonSerializerContext;
