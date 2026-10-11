using System.Net;
using System.Text.Json;

namespace Pookie.Lyrics;

public interface ILyricsProvider
{
    Task<LyricsCandidate?> GetAsync(LyricsQuery query, CancellationToken token);
    Task<LyricsCandidate[]> SearchAsync(string title, string artist, CancellationToken token);
}

public sealed class LrclibClient(HttpClient http, TimeProvider? timeProvider = null) : ILyricsProvider, IDisposable
{
    public const int MaxResponseBytes = 1024 * 1024;
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim requests = new(1);
    private DateTimeOffset blockedUntil;

    public async Task<LyricsCandidate?> GetAsync(LyricsQuery query, CancellationToken token)
    {
        var values = new Dictionary<string, string> { ["track_name"] = query.Title, ["artist_name"] = query.Artist };
        if (!string.IsNullOrWhiteSpace(query.Album)) values["album_name"] = query.Album;
        if (query.Duration is >= 1 and <= 3600) values["duration"] = query.Duration.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var data = await RequestAsync("get", values, token);
        return data == null ? null : JsonSerializer.Deserialize(data, LyricsJson.Default.LyricsCandidate);
    }

    public async Task<LyricsCandidate[]> SearchAsync(string title, string artist, CancellationToken token)
    {
        var data = await RequestAsync("search", new() { ["track_name"] = title, ["artist_name"] = artist }, token);
        return data == null ? [] : JsonSerializer.Deserialize(data, LyricsJson.Default.LyricsCandidateArray) ?? [];
    }

    private async Task<byte[]?> RequestAsync(string route, Dictionary<string, string> values, CancellationToken token)
    {
        await requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (blockedUntil > time.GetUtcNow()) throw new LyricsRateLimitException(blockedUntil - time.GetUtcNow());
            var url = "https://lrclib.net/api/" + route + "?" + string.Join('&', values.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Pookie/0.1 (+https://github.com/freakdaniel/Pookie)");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - time.GetUtcNow()) ?? TimeSpan.FromMinutes(1);
                blockedUntil = time.GetUtcNow() + (wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1));
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new LyricsRateLimitException(blockedUntil - time.GetUtcNow());
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException("Lyrics response is too large");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > MaxResponseBytes) throw new InvalidDataException("Lyrics response is too large");
                buffer.Write(chunk, 0, count);
            }
            return buffer.ToArray();
        }
        finally { requests.Release(); }
    }
    public void Dispose() => requests.Dispose();
}

public sealed class LyricsRateLimitException(TimeSpan retryAfter) : HttpRequestException("Lyrics provider cooldown", null, HttpStatusCode.TooManyRequests)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
