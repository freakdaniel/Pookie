using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pookie.SoundCloud;

// Website protocol, deliberately isolated from UI and audio. Not the registered-app API.
public sealed partial class SoundCloudWebClient(HttpClient http) : ISoundCloudPlaybackSource
{
    public const string DefaultUserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36";
    private readonly SemaphoreSlim discoveryGate = new(1, 1);
    private string? anonymousClientId;
    public WebSession? Session { get; set; }
    public ISoundCloudBrowserTransport? BrowserTransport { get; set; }
    public bool RequireBrowserTransport { get; init; }

    [GeneratedRegex("\"hydratable\"\\s*:\\s*\"apiClient\"\\s*,\\s*\"data\"\\s*:\\s*\\{\\s*\"id\"\\s*:\\s*\"([A-Za-z0-9]+)\"")]
    private static partial Regex ClientIdPattern();

    public static string? ExtractClientId(string html)
    {
        var match = ClientIdPattern().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool IsApiUri(Uri uri) => uri.Scheme == "https" && uri.Host == "api-v2.soundcloud.com" && uri.Port == 443 && uri.UserInfo == "";
    public static bool IsMediaUri(Uri uri) => uri.Scheme == "https" &&
        (uri.Host == "sndcdn.com" || uri.Host.EndsWith(".sndcdn.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host == "playback.media-streaming.soundcloud.cloud") && uri.Port == 443 && uri.UserInfo == "";

    public async Task<string> GetClientIdAsync(CancellationToken cancellationToken = default)
    {
        if (Session is { } session) return session.ClientId;
        await discoveryGate.WaitAsync(cancellationToken);
        try
        {
            if (anonymousClientId != null) return anonymousClientId;
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://soundcloud.com/");
            request.Headers.UserAgent.ParseAdd(DefaultUserAgent);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new SoundCloudException("SoundCloud не открыл главную страницу. Проверь сеть или прокси.", (int)response.StatusCode);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            anonymousClientId = ExtractClientId(html) ?? throw new SoundCloudException("Изменилась страница SoundCloud. Подключи браузерную сессию.");
            return anonymousClientId;
        }
        finally { discoveryGate.Release(); }
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken, HttpMethod? method = null)
    {
        if (!IsApiUri(uri)) throw new SoundCloudException("Отклонён адрес вне SoundCloud API.");
        var session = Session;
        if (session != null && !session.IsValid()) throw new SoundCloudException("Некорректная сессия SoundCloud. Войди ещё раз.");
        if (session != null && BrowserTransport is { } browser && (method == null || method == HttpMethod.Get))
            return await browser.GetJsonAsync(uri, cancellationToken);
        if (session != null && RequireBrowserTransport)
            throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        var clientId = session?.ClientId ?? await GetClientIdAsync(cancellationToken);
        // Replace rather than accumulate a stale client_id from next_href/transcoding URLs.
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("client_id=", StringComparison.OrdinalIgnoreCase));
        var parameters = query.Where(part => !part.StartsWith("app_version=", StringComparison.OrdinalIgnoreCase) &&
            !part.StartsWith("app_locale=", StringComparison.OrdinalIgnoreCase)).Append("client_id=" + Uri.EscapeDataString(clientId));
        if (session?.AppVersion is { } version) parameters = parameters.Append("app_version=" + Uri.EscapeDataString(version));
        if (session?.AppLocale is { } locale) parameters = parameters.Append("app_locale=" + Uri.EscapeDataString(locale));
        var target = new UriBuilder(uri) { Query = string.Join('&', parameters) }.Uri;
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, target);
        request.Headers.Accept.ParseAdd("application/json, text/javascript, */*; q=0.1");
        request.Headers.UserAgent.ParseAdd(session?.UserAgent is { Length: > 0 } ua ? ua : DefaultUserAgent);
        request.Headers.Referrer = new Uri("https://soundcloud.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://soundcloud.com");
        if (session != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", session.AccessToken);
            // SoundCloud enables DataDome's sessionByHeader, rather than sending all cookies.
            if (session.DataDomeClientId is { } protectionSession)
                request.Headers.Add("x-datadome-clientid", protectionSession);
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (session != null && response.Headers.TryGetValues("x-set-cookie", out var cookies))
            foreach (var cookie in cookies)
                if (cookie.StartsWith("datadome=", StringComparison.OrdinalIgnoreCase))
                    session.UpdateDataDomeClientId(cookie["datadome=".Length..].Split(';', 2)[0]);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden && await IsBrowserChallengeAsync(response.Content, cancellationToken))
                throw new SoundCloudException("SoundCloud запросил браузерную проверку. Повтори действие после проверки на сайте.", 403, requiresBrowserVerification: true);
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Сессия SoundCloud истекла или web API отклонил запрос. Войди в SoundCloud ещё раз.",
                HttpStatusCode.Forbidden => "SoundCloud запретил доступ к этому ресурсу.",
                HttpStatusCode.NotFound => "Трек или ресурс SoundCloud не найден.",
                HttpStatusCode.TooManyRequests => "SoundCloud ограничил частоту запросов. Повтори позже.",
                _ => "SoundCloud временно не отвечает."
            };
            throw new SoundCloudException(message, (int)response.StatusCode);
        }
        if (method == HttpMethod.Put || method == HttpMethod.Delete) return JsonDocument.Parse("{}");
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
    }

    private static async Task<bool> IsBrowserChallengeAsync(HttpContent content, CancellationToken cancellationToken)
    {
        // A challenge URL contains session data; inspect its host without exposing or opening it.
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var bytes = new byte[16 * 1024];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        try
        {
            using var json = JsonDocument.Parse(bytes.AsMemory(0, count));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("url", out var value) &&
                value.ValueKind == JsonValueKind.String && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) &&
                uri.Scheme == "https" && (uri.Host == "captcha-delivery.com" || uri.Host.EndsWith(".captcha-delivery.com", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { return false; }
    }

    private static Uri Api(string path) => new("https://api-v2.soundcloud.com/" + path);

    public async Task<SoundCloudUser> GetMeAsync(CancellationToken cancellationToken = default)
    {
        if (Session == null) throw new SoundCloudException("Сначала войди в SoundCloud.");
        using var document = await GetJsonAsync(Api("me"), cancellationToken);
        return document.RootElement.Deserialize(SoundCloudJson.Default.SoundCloudUser) ?? throw new SoundCloudException("Не удалось прочитать профиль.");
    }

    public async Task<TrackPage> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(Api("search/tracks?q=" + Uri.EscapeDataString(query.Trim()) + "&limit=30&linked_partitioning=1"), cancellationToken);
        return ParsePage(document.RootElement);
    }

    public async Task<TrackPage> GetLikesAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (Session == null) throw new SoundCloudException("Для лайков нужен вход в SoundCloud.");
        using var document = await GetJsonAsync(Api($"users/{userId}/likes?limit=30&linked_partitioning=1"), cancellationToken);
        return ParsePage(document.RootElement);
    }

    public async Task<LibraryPage> GetLibraryAsync(string section, long userId, CancellationToken cancellationToken = default)
    {
        if (Session == null) throw new SoundCloudException("Для библиотеки нужен вход в SoundCloud.");
        var path = section switch {
            "collections" => "me/library/all", "stations" => "me/library/stations",
            "recent" => "me/play-history/contexts", "history" => "me/play-history/tracks",
            "following" when userId > 0 => $"users/{userId}/followings",
            _ => throw new ArgumentOutOfRangeException(nameof(section)) };
        using var document = await GetJsonAsync(Api(path + (section == "recent" ? "?limit=6&linked_partitioning=1" : "?limit=30&linked_partitioning=1")), cancellationToken);
        return await ParseLibraryAsync(document.RootElement, cancellationToken);
    }

    public async Task<LibraryPage> GetLibraryNextAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!BrowserRequestCommand.IsReadUrl(url)) throw new SoundCloudException("Неверный адрес страницы библиотеки.");
        using var document = await GetJsonAsync(new Uri(url), cancellationToken);
        return await ParseLibraryAsync(document.RootElement, cancellationToken);
    }

    private async Task<LibraryPage> ParseLibraryAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var parsed = LibraryData.Parse(root);
        var collection = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
        var ids = collection.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("track_id", out _))
            .Select(e => e.GetProperty("track_id")).Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out _))
            .Select(e => e.GetInt64()).Where(id => id > 0 && !parsed.Items.Any(i => i.Track?.Id == id)).Distinct().ToArray();
        if (ids.Length == 0) return await EnrichLibraryAsync(parsed, cancellationToken);
        var fetched = new Dictionary<long, SoundCloudTrack>();
        foreach (var batch in ids.Chunk(50))
        {
            using var metadata = await GetJsonAsync(Api("tracks?ids=" + string.Join(',', batch)), cancellationToken);
            foreach (var track in ParsePage(metadata.RootElement).Tracks) fetched[track.Id] = track;
        }
        var ordered = new List<LibraryItem>();
        foreach (var entry in collection.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (entry.TryGetProperty("track_id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var value) && fetched.TryGetValue(value, out var track))
                ordered.Add(LibraryItem.FromTrack(track));
            else ordered.AddRange(LibraryData.Parse(JsonSerializer.SerializeToElement(new[] { entry })).Items);
        }
        return await EnrichLibraryAsync(new(ordered.DistinctBy(item => item.Key).ToArray(), parsed.NextHref), cancellationToken);
    }

    public async Task<TrackPage> GetCollectionTracksAsync(LibraryItem item, CancellationToken cancellationToken = default)
    {
        string path;
        if (item.User is { Id: > 0 } user) path = $"users/{user.Id}/tracks?limit=30&linked_partitioning=1";
        else if (item.Playlist is { } playlist)
            path = (playlist.Urn ?? "").StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal)
                ? "system-playlists/" + Uri.EscapeDataString(playlist.Urn!)
                : playlist.Id > 0 ? $"playlists/{playlist.Id}" : throw new SoundCloudException("Подборка недоступна.");
        else throw new SoundCloudException("Нет треков для этой карточки.");
        using var document = await GetJsonAsync(Api(path), cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tracks", out var tracks)) return ParsePage(root);
        // Some playlists contain ID-only placeholders. Resolve those in bounded batches.
        var complete = new List<SoundCloudTrack>();
        foreach (var value in tracks.EnumerateArray())
        {
            var track = value.Deserialize(SoundCloudJson.Default.SoundCloudTrack);
            if (track is not { Id: > 0 }) continue;
            complete.Add(track);
        }
        var missing = complete.Where(t => string.IsNullOrEmpty(t.Title)).Select(t => t.Id).Distinct().ToArray();
        var resolved = new Dictionary<long, SoundCloudTrack>();
        foreach (var batch in missing.Chunk(50))
        {
            using var json = await GetJsonAsync(Api("tracks?ids=" + string.Join(',', batch)), cancellationToken);
            foreach (var track in ParsePage(json.RootElement).Tracks) resolved[track.Id] = track;
        }
        return new(complete.Select(t => resolved.GetValueOrDefault(t.Id) ?? t).Where(t => !string.IsNullOrEmpty(t.Title)).ToArray(), null);
    }

    public async Task<TrackPage> GetFeedAsync(CancellationToken cancellationToken = default)
    {
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы открыть ленту подписок.");
        using var document = await GetJsonAsync(Api("stream?limit=30&linked_partitioning=1"), cancellationToken);
        return ParsePage(document.RootElement);
    }

    public async Task<HashSet<long>> GetLikedIdsAsync(CancellationToken cancellationToken = default)
    {
        if (Session == null) throw new SoundCloudException("Для лайков нужен вход в SoundCloud.");
        var ids = new HashSet<long>();
        Uri? target = Api("me/track_likes/ids?limit=200&linked_partitioning=1");
        var seen = new HashSet<string>();
        while (target != null && seen.Add(target.AbsoluteUri))
        {
            using var document = await GetJsonAsync(target, cancellationToken);
            var root = document.RootElement;
            var collection = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
            foreach (var item in collection.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id) && id > 0) ids.Add(id);
                else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out id) && id > 0) ids.Add(id);
            }
            target = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_href", out var next) &&
                next.ValueKind == JsonValueKind.String && Uri.TryCreate(next.GetString(), UriKind.Absolute, out var uri) ? uri : null;
        }
        return ids;
    }

    public async Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default)
    {
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы поставить лайк.");
        if (userId <= 0 || trackId <= 0) throw new ArgumentOutOfRangeException(nameof(trackId));
        if (BrowserTransport is { } browser)
        {
            await browser.SetLikedAsync(userId, trackId, liked, cancellationToken);
            return;
        }
        if (RequireBrowserTransport) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        // Compatibility for standalone HTTP consumers; Pookie always attaches its browser.
        using var document = await GetJsonAsync(Api($"users/{userId}/track_likes/{trackId}"), cancellationToken, liked ? HttpMethod.Put : HttpMethod.Delete);
    }

    public async Task<TrackPage> GetNextPageAsync(string nextHref, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(nextHref, UriKind.Absolute, out var uri) || !IsApiUri(uri)) throw new SoundCloudException("Неверный адрес следующей страницы.");
        using var document = await GetJsonAsync(uri, cancellationToken);
        return ParsePage(document.RootElement);
    }

    public static TrackPage ParsePage(JsonElement root)
    {
        var collection = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
        List<SoundCloudTrack> tracks = [];
        foreach (var item in collection.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var track = item.TryGetProperty("track", out var nested) ? nested :
                item.TryGetProperty("origin", out nested) ? nested : item;
            if (track.ValueKind != JsonValueKind.Object || !track.TryGetProperty("title", out _) || track.TryGetProperty("tracks", out _)) continue;
            var parsed = track.Deserialize(SoundCloudJson.Default.SoundCloudTrack);
            if (parsed is { Id: > 0 }) tracks.Add(parsed);
        }
        var next = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_href", out var href) && href.ValueKind == JsonValueKind.String ? href.GetString() : null;
        return new(tracks.ToArray(), next);
    }

    public async Task<SoundCloudTrack> GetTrackAsync(long id, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(Api($"tracks/{id}"), cancellationToken);
        return document.RootElement.Deserialize(SoundCloudJson.Default.SoundCloudTrack) ?? throw new SoundCloudException("Не удалось прочитать трек.");
    }

    public async Task<SoundCloudTrack> ResolveAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            (uri.Host != "soundcloud.com" && uri.Host != "www.soundcloud.com")) throw new SoundCloudException("Нужна HTTPS-ссылка на трек SoundCloud.");
        using var document = await GetJsonAsync(Api("resolve?url=" + Uri.EscapeDataString(uri.AbsoluteUri)), cancellationToken);
        if (!document.RootElement.TryGetProperty("kind", out var kind) || kind.GetString() != "track") throw new SoundCloudException("Открой ссылку на отдельный трек.");
        return document.RootElement.Deserialize(SoundCloudJson.Default.SoundCloudTrack) ?? throw new SoundCloudException("Не удалось прочитать трек.");
    }

    public static IEnumerable<Transcoding> PlayableTranscodings(SoundCloudTrack track) => (track.Media?.Transcodings ?? [])
        .Where(t => !t.Snipped && !t.Url.Contains("/preview", StringComparison.OrdinalIgnoreCase) &&
            t.Format?.Protocol is "progressive" or "hls")
        .OrderBy(t => t.Format!.Protocol == "progressive" ? 0 : 1)
        .ThenBy(t => t.Format!.MimeType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ? 0 : 1);

    public async Task<SoundCloudStream?> ResolveTranscodingAsync(SoundCloudTrack track, Transcoding transcoding, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(transcoding.Url, UriKind.Absolute, out var target) || !IsApiUri(target)) return null;
        var query = target.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("track_authorization=", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(track.TrackAuthorization))
            query = query.Append("track_authorization=" + Uri.EscapeDataString(track.TrackAuthorization));
        using var document = await GetJsonAsync(new UriBuilder(target) { Query = string.Join('&', query) }.Uri, cancellationToken);
        if (!document.RootElement.TryGetProperty("url", out var value) || value.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var media) || !IsMediaUri(media)) return null;
        var license = document.RootElement.TryGetProperty("licenseAuthToken", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() : null;
        return new(media, transcoding.Format!.Protocol, track.DurationSeconds) { LicenseAuthToken = license };
    }

    // Compatibility entry point for native-only consumers and diagnostics.
    public async Task<SoundCloudStream> GetStreamAsync(SoundCloudTrack track, CancellationToken cancellationToken = default, string? protocol = null) =>
        (await new SoundCloudStreamResolver(this).ResolveAsync(track.Id, cancellationToken,
            protocol == null ? ["progressive", "hls"] : [protocol])).Stream;
}
