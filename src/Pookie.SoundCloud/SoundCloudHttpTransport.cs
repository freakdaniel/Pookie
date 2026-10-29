using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed record SoundCloudHttpObservation(string Method, string Path, int Status, bool BrowserRecovery = false)
{
    public string? Host { get; init; }
}
public enum SoundCloudRecoveryStage { PreparingSession, SessionPrepared, NativeRetry, NativeAccepted, NativeRejected, BrowserOperation, BrowserCompleted }
public sealed record SoundCloudRecoveryObservation(string Method, string Path, SoundCloudRecoveryStage Stage);

// Normal requests never start a browser. Explicit challenges first prepare the
// site's session; an operation fallback remains if native HTTP is still denied.
public sealed partial class SoundCloudHttpTransport(HttpClient http, Func<WebSession> session,
    ISoundCloudBrowserTransport? verification = null, TimeProvider? timeProvider = null, ISoundCloudSessionRefresher? authorization = null) : ISoundCloudTransport, IDisposable
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim concurrency = new(4, 4);
    private readonly SemaphoreSlim recovery = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> reads = new();
    private long retryAfterTicks;
    private bool verified;
    private string? verifiedProtection;
    private string? verifiedToken;
    private string? verifiedRequest;
    private bool prepared;
    private string? preparedProtection;
    private string? preparedToken;
    private long preparationGeneration;
    public event Action<SoundCloudHttpObservation>? Observed;
    public event Action<SoundCloudRecoveryObservation>? RecoveryObserved;
    public event Action? SessionChanged;

    public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lifetime.Token.ThrowIfCancellationRequested();
        var account = Account();
        if (!(BrowserRequestCommand.IsReadUrl(uri.AbsoluteUri) || account.MobileIdentity != null && SoundCloudMobileRoutes.IsRead(uri)))
            throw new SoundCloudException("Отклонён адрес вне SoundCloud API.");
        var key = account.ClientId + "\n" + account.AccessToken + "\n" + uri.AbsoluteUri;
        var candidate = new Lazy<Task<byte[]>>(() => ReadAsync(key, uri, lifetime.Token));
        var read = reads.GetOrAdd(key, candidate);
        try { return JsonDocument.Parse(await read.Value.WaitAsync(cancellationToken).ConfigureAwait(false)); }
        finally { if (read.IsValueCreated && read.Value.IsCompleted) reads.TryRemove(new(key, read)); }
    }

    private async Task<byte[]> ReadAsync(string key, Uri uri, CancellationToken token)
    {
        try
        {
            using var document = await ExecuteAsync(HttpMethod.Get, uri, null,
                verification == null || SoundCloudMobileRoutes.IsOrigin(uri) ? null : ct => verification.GetJsonAsync(uri, ct), token).ConfigureAwait(false);
            return JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
        }
        finally { reads.TryRemove(key, out _); }
    }

    private WebSession Account()
    {
        var account = session();
        if (!account.IsValid()) throw new SoundCloudException("Некорректная сессия SoundCloud. Войди ещё раз.");
        return account;
    }

    private async Task<JsonDocument> ExecuteAsync(HttpMethod method, Uri uri, byte[]? body,
        Func<CancellationToken, Task<JsonDocument>>? verify, CancellationToken token, string contentType = "application/json", bool expectJson = true,
        Action<System.Text.Json.JsonElement>? validate = null)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var stop = cancellation.Token;
        var account = Account();
        if (authorization != null && account.MobileIdentity != null && account.ExpiresAtUtc <= clock.GetUtcNow().AddMinutes(1))
            account = await authorization.RefreshAsync(account, force: false, stop).ConfigureAwait(false);
        var protection = account.DataDomeClientId;
        var accessToken = account.AccessToken;
        var generation = Interlocked.Read(ref preparationGeneration);
        try { return await SendAuthorizedAsync(method, uri, body, contentType, expectJson, account, stop, validate).ConfigureAwait(false); }
        catch (SoundCloudException error) when (error.RequiresBrowserVerification && error.ChallengeKind != SoundCloudChallengeKind.HardBlock &&
            (verify != null || verification is ISoundCloudSessionVerifier))
        {
            var rejection = error;
            await recovery.WaitAsync(stop).ConfigureAwait(false);
            try
            {
                stop.ThrowIfCancellationRequested();
                // A concurrent check may already have supplied a new cookie. Only
                // explicit protection denials are replayed; network failures aren't.
                var current = Account();
                if (!account.Equals(current))
                    throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
                if (protection != current.DataDomeClientId || accessToken != current.AccessToken || generation != Interlocked.Read(ref preparationGeneration))
                {
                    var resumed = await RetryNativeAsync().ConfigureAwait(false);
                    if (resumed != null) return resumed;
                }
                var requestKey = method.Method + " " + uri.GetLeftPart(UriPartial.Path);
                if (verified && verifiedProtection == Account().DataDomeClientId && verifiedToken == Account().AccessToken && verifiedRequest == requestKey)
                    throw new SoundCloudException("SoundCloud не принял нативную сессию после браузерной проверки.", rejection.StatusCode, requiresBrowserVerification: true)
                        { ChallengeKind = rejection.ChallengeKind };
                current = Account();
                if (verification is ISoundCloudSessionVerifier preparer &&
                    (!prepared || preparedProtection != current.DataDomeClientId || preparedToken != current.AccessToken))
                {
                    Report(SoundCloudRecoveryStage.PreparingSession);
                    await preparer.PrepareSessionAsync(stop).ConfigureAwait(false);
                    stop.ThrowIfCancellationRequested();
                    current = Account();
                    if (!account.Equals(current)) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
                    prepared = true; preparedProtection = current.DataDomeClientId; preparedToken = current.AccessToken;
                    Interlocked.Increment(ref preparationGeneration);
                    Report(SoundCloudRecoveryStage.SessionPrepared);
                    var resumed = await RetryNativeAsync().ConfigureAwait(false);
                    if (resumed != null) return resumed;
                }
                // Mobile-only endpoints cannot be executed by the site's V2
                // bridge. Session preparation still gets one native retry.
                if (verify == null) throw rejection;
                Report(SoundCloudRecoveryStage.BrowserOperation);
                Observed?.Invoke(new(method.Method, uri.AbsolutePath, rejection.StatusCode, BrowserRecovery: true) { Host = uri.Host });
                var result = await verify(stop).ConfigureAwait(false);
                verified = true; verifiedProtection = Account().DataDomeClientId; verifiedToken = Account().AccessToken; verifiedRequest = requestKey;
                // The browser has now completed the operation. Never replay that
                // mutation natively: only subsequent requests start with HTTP.
                Report(SoundCloudRecoveryStage.BrowserCompleted);
                return result;
            }
            finally { recovery.Release(); }

            async Task<JsonDocument?> RetryNativeAsync()
            {
                Report(SoundCloudRecoveryStage.NativeRetry);
                try
                {
                    var result = await SendAuthorizedAsync(method, uri, body, contentType, expectJson, account, stop, validate).ConfigureAwait(false);
                    Report(SoundCloudRecoveryStage.NativeAccepted);
                    return result;
                }
                catch (SoundCloudException next) when (next.RequiresBrowserVerification && next.ChallengeKind != SoundCloudChallengeKind.HardBlock)
                { rejection = next; Report(SoundCloudRecoveryStage.NativeRejected); return null; }
            }
        }

        void Report(SoundCloudRecoveryStage stage) => RecoveryObserved?.Invoke(new(method.Method, uri.AbsolutePath, stage));
    }

    private async Task<JsonDocument> SendAuthorizedAsync(HttpMethod method, Uri uri, byte[]? body, string contentType, bool expectJson, WebSession account, CancellationToken token,
        Action<JsonElement>? validate = null)
    {
        try { return await SendAsync(method, uri, body, contentType, expectJson, account, token, validate).ConfigureAwait(false); }
        catch (SoundCloudException error) when (error.StatusCode == 401 && !error.RequiresBrowserVerification && authorization != null && account.MobileIdentity != null)
        {
            var updated = await authorization.RefreshAsync(account, force: true, token).ConfigureAwait(false);
            if (updated.AccessToken == account.AccessToken) throw;
            return await SendAsync(method, uri, body, contentType, expectJson, updated, token, validate).ConfigureAwait(false);
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, Uri uri, byte[]? body, string contentType, bool expectJson, WebSession expected, CancellationToken token,
        Action<JsonElement>? validate)
    {
        if (!(SoundCloudWebClient.IsApiUri(uri) || IsGraphUri(uri) || expected.MobileIdentity != null && SoundCloudMobileRoutes.IsAllowed(uri, method)) || uri.Fragment.Length != 0)
            throw new SoundCloudException("Отклонён адрес вне SoundCloud API.");
        CheckCooldown();
        await concurrency.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckCooldown();
            var account = Account();
            if (!expected.Equals(account)) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
            using var request = new HttpRequestMessage(method, WithContext(uri, account))
                { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
            request.Headers.Authorization = new("OAuth", account.AccessToken);
            request.Headers.Accept.ParseAdd(SoundCloudMobileRoutes.IsStream(uri) ? "*/*" : account.MobileIdentity == null ? "application/json" : "application/json; charset=utf-8");
            request.Headers.UserAgent.ParseAdd(account.UserAgent);
            if (account.MobileIdentity is { } mobile)
            {
                request.Headers.Add("App-Version", mobile.BuildString);
                request.Headers.Add("App-Locale", account.AppLocale ?? "en");
                request.Headers.Add("App-Environment", "prod");
                request.Headers.Add("UDID", mobile.Udid);
                if (mobile.IsTablet) request.Headers.Add("User-Interface-Type", "tablet");
                if (mobile.AcceptLanguage != null) request.Headers.Add("Accept-Language", mobile.AcceptLanguage);
                request.Headers.Add("App-Requested-Features", mobile.FeaturesHeader);
                if (account.DataDomeClientId is { } cookie) request.Headers.Add("Cookie", "datadome=" + cookie);
            }
            else
            {
                request.Headers.Referrer = new("https://soundcloud.com/");
                request.Headers.Add("Origin", "https://soundcloud.com");
                if (account.DataDomeClientId is { } cookie) request.Headers.Add("x-datadome-clientid", cookie);
            }
            if (IsGraphUri(uri))
            {
                request.Headers.Add("apollographql-client-name", account.MobileIdentity == null ? "webi" : "android");
                if (account.MobileIdentity is { } graphMobile) request.Headers.Add("apollographql-client-version", graphMobile.BuildString);
            }
            if (body != null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new(contentType);
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            Observed?.Invoke(new(method.Method, uri.AbsolutePath, (int)response.StatusCode) { Host = uri.Host });
            foreach (var header in new[] { "Set-Cookie", "x-set-cookie" })
                if (response.Headers.TryGetValues(header, out var cookies))
                    foreach (var cookie in cookies)
                        if (cookie.StartsWith("datadome=", StringComparison.OrdinalIgnoreCase))
                            UpdateProtection(account, cookie[9..].Split(';', 2)[0]);
            var streamResponse = SoundCloudMobileRoutes.IsStream(uri);
            var redirect = (int)response.StatusCode is 301 or 302 or 303 or 307 or 308;
            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            var directMedia = streamResponse && response.IsSuccessStatusCode && (mediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true ||
                mediaType is "application/vnd.apple.mpegurl" or "application/x-mpegurl" or "application/octet-stream");
            // A mobile stream endpoint is a MediaItem URI. Do not buffer audio as
            // JSON (or download an entire file just to resolve it).
            var bytes = directMedia || streamResponse && redirect ? [] : await ReadBoundedAsync(response.Content, token).ConfigureAwait(false);
            var challenge = Challenge(response, bytes);
            await RecordMobileResponseAsync(uri, account, token, allowSend: challenge == null).ConfigureAwait(false);
            if (challenge != null)
                throw new SoundCloudException(challenge == SoundCloudChallengeKind.HardBlock
                    ? "SoundCloud заблокировал доступ. Капча не предложена."
                    : "SoundCloud запросил проверку сессии.", (int)response.StatusCode, requiresBrowserVerification: true)
                    { ChallengeKind = challenge.Value };
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var until = response.Headers.RetryAfter?.Date ?? clock.GetUtcNow() + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
                var ticks = Math.Max(until.UtcTicks, clock.GetUtcNow().AddSeconds(1).UtcTicks);
                long previous;
                do { previous = Interlocked.Read(ref retryAfterTicks); }
                while (ticks > previous && Interlocked.CompareExchange(ref retryAfterTicks, ticks, previous) != previous);
                throw RateLimited();
            }
            if (streamResponse && (redirect || directMedia))
            {
                var media = redirect && response.Headers.Location is { } location ? new Uri(uri, location) : WithContext(uri, account);
                if (redirect && response.Headers.Location == null || !(SoundCloudWebClient.IsMediaUri(media) || media.GetLeftPart(UriPartial.Path) == uri.GetLeftPart(UriPartial.Path)) ||
                    media.Fragment.Length != 0 || media.AbsolutePath.Contains("preview", StringComparison.OrdinalIgnoreCase))
                    throw new SoundCloudException("SoundCloud вернул недопустимый адрес аудиопотока.");
                return JsonDocument.Parse(Json(writer => { writer.WriteStartObject(); writer.WriteString("url", media.AbsoluteUri); writer.WriteEndObject(); }));
            }
            if (!response.IsSuccessStatusCode)
                throw new SoundCloudException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Сессия SoundCloud истекла. Войди ещё раз.",
                    HttpStatusCode.Forbidden => "SoundCloud запретил доступ к ресурсу.",
                    HttpStatusCode.NotFound => "Ресурс SoundCloud не найден.",
                    _ => "SoundCloud не выполнил запрос."
                }, (int)response.StatusCode);
            var document = JsonDocument.Parse(!expectJson || bytes.Length == 0 ? "{}"u8.ToArray() : bytes);
            try { validate?.Invoke(document.RootElement); return document; }
            catch { document.Dispose(); throw; }
        }
        finally { concurrency.Release(); }
    }

    private void UpdateProtection(WebSession owner, string value)
    {
        var current = Account();
        var previous = current.DataDomeClientId;
        owner.UpdateDataDomeClientId(value);
        if (owner.Equals(current))
        {
            current.UpdateDataDomeClientId(value);
            if (previous != current.DataDomeClientId) SessionChanged?.Invoke();
        }
    }

    private void CheckCooldown()
    {
        if (Interlocked.Read(ref retryAfterTicks) > clock.GetUtcNow().UtcTicks) throw RateLimited();
    }

    private SoundCloudException RateLimited() => new("SoundCloud ограничил частоту запросов. Повтори позже.", 429)
        { RetryAfterUtc = new DateTimeOffset(Interlocked.Read(ref retryAfterTicks), TimeSpan.Zero) };

    private static Uri WithContext(Uri uri, WebSession account)
    {
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(part =>
            !new[] { "client_id=", "app_version=", "app_locale=" }.Any(key => part.StartsWith(key, StringComparison.OrdinalIgnoreCase)))
            .Append("client_id=" + Uri.EscapeDataString(account.ClientId));
        if (account.MobileIdentity == null)
        {
            if (account.AppVersion is { } version) query = query.Append("app_version=" + Uri.EscapeDataString(version));
            if (account.AppLocale is { } locale) query = query.Append("app_locale=" + Uri.EscapeDataString(locale));
        }
        return new UriBuilder(uri) { Query = string.Join('&', query) }.Uri;
    }

    private static bool IsGraphUri(Uri uri) => uri.Scheme == "https" && uri.Host == "graph.soundcloud.com" &&
        uri.Port == 443 && uri.UserInfo == "" && uri.AbsolutePath == "/graphql";

    private static SoundCloudChallengeKind? Challenge(HttpResponseMessage response, byte[] bytes)
    {
        foreach (var header in new[] { "X-DD-B", "X-SF-CC-X-dd-b" })
            if (response.Headers.TryGetValues(header, out var values))
            {
                if (!int.TryParse(values.FirstOrDefault(), out var value)) return SoundCloudChallengeKind.Unknown;
                return (value & 255) switch
                {
                    1 => SoundCloudChallengeKind.Captcha, 2 => SoundCloudChallengeKind.HardBlock,
                    3 => (value & 256) != 0 ? SoundCloudChallengeKind.InvisibleDeviceCheck : SoundCloudChallengeKind.DeviceCheck,
                    4 => SoundCloudChallengeKind.Redirect, _ => SoundCloudChallengeKind.Unknown
                };
            }
        if (response.Headers.Location is { IsAbsoluteUri: true } location && IsChallengeUri(location))
            return SoundCloudChallengeKind.Redirect;
        if (response.StatusCode != HttpStatusCode.Forbidden || bytes.Length == 0) return null;
        try
        {
            using var json = JsonDocument.Parse(bytes);
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("url", out var url) &&
                url.ValueKind == JsonValueKind.String && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var target) && IsChallengeUri(target)
                ? SoundCloudChallengeKind.Unknown : null;
        }
        catch (JsonException) { return null; } // A plain HTML denial is not proof of a challenge.
    }

    private static bool IsChallengeUri(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo == "" &&
        (uri.Host == "captcha-delivery.com" || uri.Host.EndsWith(".captcha-delivery.com", StringComparison.OrdinalIgnoreCase));

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) throw new SoundCloudException("Слишком большой ответ SoundCloud.");
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > MaxResponseBytes) throw new SoundCloudException("Слишком большой ответ SoundCloud.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static Uri Api(string path) => new("https://api-v2.soundcloud.com" + path);
    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output)) write(writer);
        return output.ToArray();
    }

    public void Dispose() => lifetime.Cancel();
}
