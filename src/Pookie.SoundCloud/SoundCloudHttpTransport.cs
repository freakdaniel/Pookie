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

// Native web requests recover through the same website session when verification is required.
public sealed partial class SoundCloudHttpTransport(HttpClient http, Func<WebSession> session,
    ISoundCloudBrowserTransport? verification = null, TimeProvider? timeProvider = null) : ISoundCloudTransport, IDisposable
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim concurrency = new(4, 4);
    private readonly SemaphoreSlim recovery = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, PendingRead> reads = new();
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
        if (!(BrowserRequestCommand.IsReadUrl(uri.AbsoluteUri)))
            throw new SoundCloudException("Отклонён адрес вне SoundCloud API.");
        var key = account.ClientId + "\n" + account.AccessToken + "\n" + uri.AbsoluteUri;
        while (true)
        {
            var candidate = new PendingRead(stop => ReadAsync(uri, stop), lifetime.Token);
            var read = reads.GetOrAdd(key, candidate);
            if (!ReferenceEquals(read, candidate)) candidate.Stop.Dispose();
            lock (read)
            {
                if (read.Retired) { reads.TryRemove(new(key, read)); continue; }
                read.Waiters++;
            }
            try { return JsonDocument.Parse(await read.Value.Value.WaitAsync(cancellationToken).ConfigureAwait(false)); }
            finally
            {
                bool last;
                lock (read) { last = --read.Waiters == 0; if (last) read.Retired = true; }
                if (last)
                {
                    reads.TryRemove(new(key, read));
                    read.Stop.Cancel();
                    _ = read.Value.Value.ContinueWith(_ => read.Stop.Dispose(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
    }

    private sealed class PendingRead
    {
        public readonly CancellationTokenSource Stop;
        public readonly Lazy<Task<byte[]>> Value;
        public int Waiters;
        public bool Retired;
        public PendingRead(Func<CancellationToken, Task<byte[]>> operation, CancellationToken lifetime)
        {
            Stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            Value = new(() => operation(Stop.Token));
        }
    }

    private async Task<byte[]> ReadAsync(Uri uri, CancellationToken token)
    {
        using var document = await ExecuteAsync(HttpMethod.Get, uri, null,
            verification == null ? null : ct => verification.GetJsonAsync(uri, ct), token).ConfigureAwait(false);
        return JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
    }

    private WebSession Account()
    {
        var account = session();
        if (!account.IsValid()) throw new SoundCloudException("Некорректная сессия SoundCloud. Войди ещё раз.");
        return account;
    }

    private async Task<JsonDocument> ExecuteAsync(HttpMethod method, Uri uri, byte[]? body,
        Func<CancellationToken, Task<JsonDocument>>? verify, CancellationToken token, string contentType = "application/json", bool expectJson = true,
        Action<System.Text.Json.JsonElement>? validate = null, WebSession? expectedAccount = null)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var stop = cancellation.Token;
        var account = expectedAccount ?? Account();
        if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
        var protection = account.DataDomeClientId;
        var accessToken = account.AccessToken;
        var generation = Interlocked.Read(ref preparationGeneration);
        try { return await SendAuthorizedAsync(method, uri, body, contentType, expectJson, account, stop, validate).ConfigureAwait(false); }
        catch (SoundCloudException error) when (error.RequiresBrowserVerification &&
            (verify != null || verification is ISoundCloudSessionVerifier))
        {
            // The native connection's block does not establish the browser's state.
            // The live website must perform its own check before executing the action.
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
                catch (SoundCloudException next) when (next.RequiresBrowserVerification)
                { rejection = next; Report(SoundCloudRecoveryStage.NativeRejected); return null; }
            }
        }

        void Report(SoundCloudRecoveryStage stage) => RecoveryObserved?.Invoke(new(method.Method, uri.AbsolutePath, stage));
    }

    private Task<JsonDocument> SendAuthorizedAsync(HttpMethod method, Uri uri, byte[]? body, string contentType, bool expectJson, WebSession account, CancellationToken token,
        Action<JsonElement>? validate = null) => SendAsync(method, uri, body, contentType, expectJson, account, token, validate);


    private async Task<JsonDocument> SendAsync(HttpMethod method, Uri uri, byte[]? body, string contentType, bool expectJson, WebSession expected, CancellationToken token,
        Action<JsonElement>? validate)
    {
        if (!(SoundCloudWebClient.IsApiUri(uri) || IsGraphUri(uri)) || uri.Fragment.Length != 0)
            throw new SoundCloudException("Отклонён адрес вне SoundCloud API.");
        CheckCooldown();
        await concurrency.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckCooldown();
            var account = Account();
            if (!expected.Equals(account)) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
            var sentProtection = account.DataDomeClientId;
            using var request = new HttpRequestMessage(method, WithContext(uri, account))
                { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
            request.Headers.Authorization = new("OAuth", account.AccessToken);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd(account.UserAgent);

            request.Headers.Referrer = new("https://soundcloud.com/");
            request.Headers.Add("Origin", "https://soundcloud.com");
            if (sentProtection is { } protectionCookie) request.Headers.Add("x-datadome-clientid", protectionCookie);

            if (IsGraphUri(uri))
            {
                request.Headers.Add("apollographql-client-name", "webi");
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
                            UpdateProtectionFromResponse(account, cookie[9..].Split(';', 2)[0], sentProtection);
            var bytes = await ReadBoundedAsync(response.Content, token).ConfigureAwait(false);
            var challenge = Challenge(response, bytes, request.RequestUri);
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
        if (!owner.Equals(current)) return;
        var previous = current.DataDomeClientId;
        current.UpdateDataDomeClientId(value);
        if (!ReferenceEquals(owner, current)) owner.ShareProtectionFrom(current);
        if (previous != current.DataDomeClientId) SessionChanged?.Invoke();
    }

    private void UpdateProtectionFromResponse(WebSession owner, string value, string? sentProtection)
    {
        var current = Account();
        if (!owner.Equals(current)) return;
        var changed = current.TryUpdateDataDomeClientId(value, sentProtection);
        if (!ReferenceEquals(owner, current)) owner.TryUpdateDataDomeClientId(value, sentProtection);
        if (changed) SessionChanged?.Invoke();
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

        if (account.AppVersion is { } version) query = query.Append("app_version=" + Uri.EscapeDataString(version));
        if (account.AppLocale is { } locale) query = query.Append("app_locale=" + Uri.EscapeDataString(locale));

        return new UriBuilder(uri) { Query = string.Join('&', query) }.Uri;
    }

    private static bool IsGraphUri(Uri uri) => uri.Scheme == "https" && uri.Host == "graph.soundcloud.com" &&
        uri.Port == 443 && uri.UserInfo == "" && uri.AbsolutePath == "/graphql";

    internal static SoundCloudChallengeKind? Challenge(HttpResponseMessage response, byte[] bytes, Uri? requestUri = null)
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
        if (response.Headers.Location is { } redirect && (requestUri ?? response.RequestMessage?.RequestUri) is { } original &&
            IsChallengeUri(new Uri(original, redirect))) return SoundCloudChallengeKind.Redirect;
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

    public void Dispose() { lifetime.Cancel(); }
}
