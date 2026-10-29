using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed record SoundCloudMobileOAuthOptions(string ClientId, string ClientSecret, SoundCloudMobileIdentity MobileIdentity, int AppId = 3152)
{
    public SoundCloudTokenExchangeClient? ExchangeClient { get; init; }
    public bool IsValid() => ClientId is { Length: > 0 and <= 256 } && ClientId.All(char.IsAsciiLetterOrDigit) &&
        ClientSecret is { Length: > 0 and <= 4096 } && !ClientSecret.Any(char.IsControl) && MobileIdentity?.IsValid() == true && AppId > 0 &&
        (ExchangeClient == null || ExchangeClient.IsValid());
    public override string ToString() => "SoundCloud mobile OAuth configuration (credentials redacted)";
}

public sealed record SoundCloudTokenExchangeClient(string ClientId, string ClientSecret)
{
    public bool IsValid() => ClientId is { Length: > 0 and <= 256 } && ClientId.All(char.IsAsciiLetterOrDigit) &&
        ClientSecret is { Length: > 0 and <= 4096 } && !ClientSecret.Any(char.IsControl);
    public override string ToString() => "SoundCloud token exchange client (credentials redacted)";
}

public sealed record MobileAuthorizationRequest(string Url, string State)
{
    public const string RedirectUri = "sc://auth";
    public bool IsValid() => Url is { Length: > 0 and <= 8192 } && State is { Length: 64 } && State.All(char.IsAsciiHexDigit) &&
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "secure.soundcloud.com" &&
        uri.Port == 443 && uri.UserInfo == "" && uri.AbsolutePath == "/web-auth" && uri.Fragment == "" &&
        Query(uri) is { } query && query.GetValueOrDefault("state") == State && query.GetValueOrDefault("redirect_uri") == RedirectUri &&
        query.GetValueOrDefault("code_challenge_method") == "S256" && query.GetValueOrDefault("code_challenge") is { Length: 43 };

    public MobileAuthorizationResult? ReadCallback(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "sc" || uri.Host != "auth" ||
            uri.AbsolutePath is not ("" or "/") || uri.UserInfo != "" || uri.Port != -1) return null;
        var query = Query(uri);
        if (uri.Fragment.Length != 0 || query == null) return new(null, "invalid_callback");
        // The mobile endpoint's Android contract does not require an echoed state.
        // When echoed, check it; PKCE binds every code to this private attempt.
        if (query.TryGetValue("state", out var state) && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(State), Encoding.UTF8.GetBytes(state)))
            return new(null, "state_mismatch");
        if (query.TryGetValue("error", out var error)) return new(null, error == "access_denied" ? "access_denied" : "server_error");
        return query.TryGetValue("code", out var code) && MobileAuthorizationResult.ValidCode(code) ? new(code) : new(null, "invalid_callback");
    }

    private static Dictionary<string, string>? Query(Uri uri)
    {
        try
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = entry.Split('=', 2);
                if (!result.TryAdd(Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair.Length == 1 ? "" : Uri.UnescapeDataString(pair[1].Replace('+', ' ')))) return null;
            }
            return result;
        }
        catch (UriFormatException) { return null; }
    }
    public override string ToString() => "SoundCloud mobile authorization request (URL and state redacted)";
}

public sealed record MobileAuthorizationResult(string? Code, string? Error = null, string? DataDomeClientId = null)
{
    internal static bool ValidCode(string? value) => value is { Length: > 0 and <= 4096 } && value.All(c => c is >= '!' and <= '~');
    public bool IsValid() => (Error == null ? ValidCode(Code) : Code == null && Error is "access_denied" or "server_error" or "invalid_callback" or "state_mismatch") &&
        (DataDomeClientId == null || WebSession.IsDataDomeClientId(DataDomeClientId));
    public override string ToString() => "SoundCloud mobile authorization result (code and cookie redacted)";
}

public sealed class SoundCloudPkceAttempt
{
    internal string Verifier { get; } = Base64Url(RandomNumberGenerator.GetBytes(48));
    internal string Locale { get; }
    public MobileAuthorizationRequest Request { get; }
    internal SoundCloudPkceAttempt(SoundCloudMobileOAuthOptions options, string locale)
    {
        Locale = locale;
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId, ["app_id"] = options.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["device_id"] = options.MobileIdentity.Udid, ["start_view"] = "sign_in", ["redirect_uri"] = MobileAuthorizationRequest.RedirectUri,
            ["response_type"] = "code", ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier))),
            ["code_challenge_method"] = "S256", ["ui_evo"] = "true", ["stand_alone"] = "true", ["tracking"] = "local",
            ["show_confirmation"] = "true", ["theme"] = "dark", ["locale"] = locale, ["state"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))
        };
        Request = new("https://secure.soundcloud.com/web-auth?" + string.Join('&', fields.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))), fields["state"]);
    }
    internal static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public override string ToString() => "SoundCloud PKCE attempt (verifier redacted)";
}

public enum SoundCloudOAuthFailure { InvalidGrant, InvalidClient, InvalidResponse, RateLimited, ServerUnavailable, Denied }

public sealed class SoundCloudOAuthException(SoundCloudOAuthFailure failure, int statusCode = 0) : Exception(failure switch
{
    SoundCloudOAuthFailure.InvalidGrant => "Мобильная сессия SoundCloud завершилась. Войди снова.",
    SoundCloudOAuthFailure.InvalidClient => "SoundCloud отклонил конфигурацию мобильного OAuth.",
    SoundCloudOAuthFailure.RateLimited => "SoundCloud временно ограничил обновление сессии.",
    SoundCloudOAuthFailure.Denied => "Вход в SoundCloud отменён или отклонён.",
    SoundCloudOAuthFailure.ServerUnavailable => "SoundCloud временно не смог обновить мобильную сессию.",
    _ => "SoundCloud вернул некорректный ответ мобильного OAuth."
})
{
    public SoundCloudOAuthFailure Failure { get; } = failure;
    public int StatusCode { get; } = statusCode;
    public DateTimeOffset? RetryAfterUtc { get; init; }
}

public sealed class SoundCloudMobileOAuth(HttpClient http, SoundCloudMobileOAuthOptions options, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public SoundCloudPkceAttempt Begin(string locale = "en")
    {
        ValidateOptions();
        if (locale is not { Length: > 0 and <= 35 } || !locale.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) throw new ArgumentException("Некорректная локаль OAuth.");
        return new(options, locale);
    }

    public async Task<WebSession> ExchangeAsync(SoundCloudPkceAttempt attempt, MobileAuthorizationResult result, CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        if (!attempt.Request.IsValid() || !result.IsValid() || result.Error != null) throw new SoundCloudOAuthException(SoundCloudOAuthFailure.Denied);
        var previous = new WebSession(options.ClientId, "pending", options.MobileIdentity.UserAgent)
        { MobileIdentity = options.MobileIdentity, SessionId = Guid.NewGuid().ToString("N"), DataDomeClientId = result.DataDomeClientId, AppLocale = attempt.Locale };
        return await TokenAsync(new()
        {
            ["grant_type"] = "authorization_code", ["client_id"] = options.ClientId, ["code"] = result.Code!,
            ["redirect_uri"] = MobileAuthorizationRequest.RedirectUri, ["code_verifier"] = attempt.Verifier
        }, previous, authenticateClient: true, cancellationToken).ConfigureAwait(false);
    }

    public Task<WebSession> RefreshAsync(WebSession previous, CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        if (!previous.IsValid() || previous.ClientId != options.ClientId || previous.MobileIdentity != options.MobileIdentity || previous.RefreshToken == null)
            throw new SoundCloudOAuthException(SoundCloudOAuthFailure.InvalidGrant);
        return TokenAsync(new() { ["grant_type"] = "refresh_token", ["client_id"] = options.ClientId, ["refresh_token"] = previous.RefreshToken },
            previous, authenticateClient: false, cancellationToken);
    }

    public Task<WebSession> ExchangeTokenAsync(WebSession source, CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        // APK migration authenticates the legacy source client and issues for
        // the expiring destination client. Relabelling a web token is invalid.
        if (!source.IsValid() || options.ExchangeClient is not { } origin || source.ClientId != origin.ClientId)
            throw new SoundCloudOAuthException(SoundCloudOAuthFailure.InvalidClient);
        var destination = new WebSession(options.ClientId, "pending", options.MobileIdentity.UserAgent)
        { MobileIdentity = options.MobileIdentity, AppLocale = source.AppLocale, SessionId = Guid.NewGuid().ToString("N") };
        return TokenAsync(new()
        {
            ["grant_type"] = "token_exchange", ["token"] = source.AccessToken, ["client_id"] = origin.ClientId,
            ["client_secret"] = origin.ClientSecret, ["issue_for_client_id"] = options.ClientId
        }, destination, authenticateClient: false, cancellationToken);
    }

    private void ValidateOptions()
    {
        if (!options.IsValid()) throw new SoundCloudOAuthException(SoundCloudOAuthFailure.InvalidClient);
    }

    private async Task<WebSession> TokenAsync(Dictionary<string, string> fields, WebSession previous, bool authenticateClient, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://secure.soundcloud.com/oauth/token?client_id=" + Uri.EscapeDataString(options.ClientId))
        { Content = new FormUrlEncodedContent(fields), Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
        request.Headers.UserAgent.ParseAdd(options.MobileIdentity.UserAgent);
        request.Headers.Accept.ParseAdd("application/x-www-form-urlencoded");
        request.Headers.Add("Accept-Charset", "UTF-8"); request.Headers.Add("App-Version", options.MobileIdentity.BuildString);
        request.Headers.Add("App-Locale", previous.AppLocale ?? "en"); request.Headers.Add("App-Environment", "prod");
        if (previous.DataDomeClientId != null) request.Headers.Add("Cookie", "datadome=" + previous.DataDomeClientId);
        if (authenticateClient)
            request.Headers.TryAddWithoutValidation("Authorization", SoundCloudPkceAttempt.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(options.ClientId + ":" + options.ClientSecret))));
        // ApiMobileAuthTokenRefresh sets the auth/public request flag and sends
        // only the refresh token and client_id: no OAuth header or client secret.
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if ((int)response.StatusCode >= 500) throw new SoundCloudOAuthException(SoundCloudOAuthFailure.ServerUnavailable, (int)response.StatusCode);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new SoundCloudOAuthException(SoundCloudOAuthFailure.RateLimited, 429)
            { RetryAfterUtc = response.Headers.RetryAfter?.Date ?? clock.GetUtcNow() + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30)) };
        byte[] bytes;
        await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        {
            using var output = new MemoryStream(); var buffer = new byte[4096]; int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > 32768) throw new SoundCloudOAuthException(SoundCloudOAuthFailure.InvalidResponse, (int)response.StatusCode);
                output.Write(buffer, 0, count);
            }
            bytes = output.ToArray();
        }
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!response.IsSuccessStatusCode)
            {
                var error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                throw new SoundCloudOAuthException(error == "invalid_grant" ? SoundCloudOAuthFailure.InvalidGrant :
                    error == "invalid_client" ? SoundCloudOAuthFailure.InvalidClient : SoundCloudOAuthFailure.InvalidResponse, (int)response.StatusCode);
            }
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("access_token", out var access) || access.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("refresh_token", out var refresh) || refresh.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("expires_in", out var expires) || expires.ValueKind != JsonValueKind.Number || !expires.TryGetDouble(out var seconds) || !double.IsFinite(seconds) || seconds is <= 0 or > 31536000)
                throw new SoundCloudOAuthException(SoundCloudOAuthFailure.InvalidResponse);
            var next = previous with { AccessToken = access.GetString()!, RefreshToken = refresh.GetString(), ExpiresAtUtc = clock.GetUtcNow().AddSeconds(seconds) };
            foreach (var name in new[] { "Set-Cookie", "x-set-cookie" })
                if (response.Headers.TryGetValues(name, out var cookies))
                    foreach (var cookie in cookies)
                        if (cookie.StartsWith("datadome=", StringComparison.OrdinalIgnoreCase)) next.UpdateDataDomeClientId(cookie[9..].Split(';', 2)[0]);
            if (!next.IsValid() || string.IsNullOrEmpty(next.RefreshToken)) throw new SoundCloudOAuthException(SoundCloudOAuthFailure.InvalidResponse);
            return next;
        }
        catch (JsonException) { throw new SoundCloudOAuthException(SoundCloudOAuthFailure.InvalidResponse, (int)response.StatusCode); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
