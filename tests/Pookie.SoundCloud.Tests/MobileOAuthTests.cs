using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class MobileOAuthTests
{
    private static SoundCloudMobileOAuthOptions Options() => new("fixturemobileid", "fixture-client-secret", new("0123456789abcdef", "Fixture", "Device", "15"));
    private static WebSession Session() => new("fixturemobileid", "old-token", Options().MobileIdentity.UserAgent)
    { MobileIdentity = Options().MobileIdentity, RefreshToken = "old-refresh", SessionId = "0123456789abcdef0123456789abcdef", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) };
    private static HttpResponseMessage Response(string text, int status = 200) => new((HttpStatusCode)status) { Content = new StringContent(text) };
    private const string TokenResponse = """{"access_token":"new-token","refresh_token":"new-refresh","expires_in":3600}""";
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static Dictionary<string, string> Fields(string form) => form.Split('&').Select(part => part.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair.Length == 1 ? "" : pair[1].Replace('+', ' ')));

    [Fact]
    public async Task CodeExchangeUsesOneSha256PkceAndAndroidClientAuthentication()
    {
        var options = Options(); SoundCloudPkceAttempt? attempt = null;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.Equal("secure.soundcloud.com", request.RequestUri!.Host); Assert.Equal("/oauth/token", request.RequestUri.AbsolutePath);
            var fields = Fields(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("authorization_code", fields["grant_type"]); Assert.Equal("fixture-code+value", fields["code"]);
            Assert.Equal("sc://auth", fields["redirect_uri"]); Assert.Equal(64, fields["code_verifier"].Length);
            var parameters = Fields(new Uri(attempt!.Request.Url).Query.TrimStart('?'));
            Assert.Equal(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(fields["code_verifier"]))), parameters["code_challenge"]);
            Assert.Equal(Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(options.ClientId + ":" + options.ClientSecret))), request.Headers.GetValues("Authorization").Single());
            Assert.False(fields.ContainsKey("client_secret"));
            return Response(TokenResponse);
        }));
        var oauth = new SoundCloudMobileOAuth(http, options); attempt = oauth.Begin("ru-RU");
        Assert.True(attempt.Request.IsValid()); Assert.DoesNotContain(options.ClientSecret, attempt.Request.Url);
        var result = attempt.Request.ReadCallback("sc://auth?code=fixture-code%2Bvalue&state=" + attempt.Request.State)!;
        var account = await oauth.ExchangeAsync(attempt, result);
        Assert.True(account.IsValid()); Assert.Equal("new-refresh", account.RefreshToken); Assert.NotNull(account.ExpiresAtUtc); Assert.NotNull(account.SessionId);
        Assert.Equal("ru-RU", account.AppLocale);
        Assert.DoesNotContain("fixture-code", result.ToString()); Assert.DoesNotContain("fixture-client-secret", options.ToString());
        Assert.DoesNotContain("new-refresh", account.ToString());
    }

    [Theory]
    [InlineData("sc://auth.evil?code=secret", null)]
    [InlineData("https://evil.test/auth?code=secret", null)]
    [InlineData("sc://auth/other?code=secret", null)]
    [InlineData("sc://auth?code=a&code=b", "invalid_callback")]
    [InlineData("sc://auth?code=abc#secret", "invalid_callback")]
    [InlineData("sc://auth?code=%0Asecret", "invalid_callback")]
    [InlineData("sc://auth?error=access_denied&error_description=private", "access_denied")]
    [InlineData("sc://auth?code=a&state=wrong", "state_mismatch")]
    public void CallbackRejectsLookalikesAmbiguousCodesAndWrongState(string address, string? error)
    {
        using var http = new HttpClient(); var attempt = new SoundCloudMobileOAuth(http, Options()).Begin();
        var callback = attempt.Request.ReadCallback(address);
        if (error == null) Assert.Null(callback);
        else { Assert.Equal(error, callback!.Error); Assert.Null(callback.Code); Assert.True(callback.IsValid()); }
    }

    [Fact]
    public void AndroidCallbackWithoutEchoedStateRemainsBoundToPkceAttempt()
    {
        using var http = new HttpClient(); var oauth = new SoundCloudMobileOAuth(http, Options());
        var first = oauth.Begin(); var second = oauth.Begin();
        Assert.NotEqual(first.Request.Url, second.Request.Url); Assert.NotEqual(first.Request.State, second.Request.State);
        Assert.Equal("code", first.Request.ReadCallback("sc://auth?code=code")!.Code);
    }

    [Fact]
    public async Task RefreshSendsOnlyRefreshTokenAndClientIdAndRotatesBothTokens()
    {
        var previous = Session();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.False(request.Headers.Contains("Authorization"));
            var fields = Fields(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(3, fields.Count); Assert.Equal("refresh_token", fields["grant_type"]); Assert.Equal("old-refresh", fields["refresh_token"]);
            return Response(TokenResponse);
        }));
        var updated = await new SoundCloudMobileOAuth(http, Options()).RefreshAsync(previous);
        Assert.Equal("new-token", updated.AccessToken); Assert.Equal("new-refresh", updated.RefreshToken);
        Assert.Equal(previous, updated); Assert.Equal(previous.GetHashCode(), updated.GetHashCode());
        Assert.NotEqual(previous, updated with { SessionId = Guid.NewGuid().ToString("N") });
        var snapshot = JsonSerializer.Serialize(updated.BrowserSnapshot(), SoundCloudJson.Default.WebSession);
        Assert.DoesNotContain("new-refresh", snapshot); Assert.Null(updated.BrowserSnapshot().RefreshToken);
    }

    [Theory]
    [InlineData("\"3600\"")]
    [InlineData("null")]
    [InlineData("-1")]
    [InlineData("1e999")]
    public async Task InvalidTokenExpiryStopsRefreshWithoutPublishingPrivateResponse(string expiry)
    {
        var current = Session(); var previous = current; var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Response("{\"access_token\":\"private-token\",\"refresh_token\":\"private-refresh\",\"expires_in\":" + expiry + "}"));
        }));
        using var manager = new SoundCloudMobileSessionManager(new(http, Options()), () => current, next => current = next);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => manager.RefreshAsync(current, true));
        await Assert.ThrowsAsync<SoundCloudException>(() => manager.RefreshAsync(current, true));
        Assert.Equal(401, error.StatusCode); Assert.Equal(1, calls); Assert.Same(previous, current);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public async Task ParallelRefreshSharesOneRequestAndCancelledWaiterDoesNotCancelOthers()
    {
        var current = Session(); var expected = current; var calls = 0; var saved = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) =>
        { calls++; entered.SetResult(); await release.Task.WaitAsync(token); return Response(TokenResponse); }));
        using var manager = new SoundCloudMobileSessionManager(new(http, Options()), () => current, next => current = next,
            (next, _) => { saved++; Assert.Equal("new-refresh", next.RefreshToken); return Task.CompletedTask; });
        using var cancellation = new CancellationTokenSource();
        var first = manager.RefreshAsync(expected, true, cancellation.Token); await entered.Task;
        var second = manager.RefreshAsync(expected, true); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult(); var updated = await second;
        Assert.Equal("new-token", updated.AccessToken); Assert.Equal(1, calls); Assert.Equal(1, saved);
        await manager.RefreshAsync(expected, true); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task StorageFailureKeepsTheRotatedTokenInMemory()
    {
        var current = Session(); var old = current; var failures = 0; var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Response(TokenResponse)); }));
        using var manager = new SoundCloudMobileSessionManager(new(http, Options()), () => current, next => current = next,
            (_, _) => throw new IOException("store unavailable"));
        manager.PersistenceFailed += () => failures++;
        await manager.RefreshAsync(old, true); await manager.RefreshAsync(old, true);
        Assert.Equal("new-refresh", current.RefreshToken); Assert.Equal(1, calls); Assert.Equal(1, failures);
    }

    [Fact]
    public async Task InvalidGrantTerminatesFamilyWithoutRepeatedRefreshOrPrivateBody()
    {
        var current = Session(); var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        { calls++; return Task.FromResult(Response("{\"error\":\"invalid_grant\",\"error_description\":\"private-token\"}", 400)); }));
        using var manager = new SoundCloudMobileSessionManager(new(http, Options()), () => current, next => current = next);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => manager.RefreshAsync(current, true));
        await Assert.ThrowsAsync<SoundCloudException>(() => manager.RefreshAsync(current, true));
        Assert.Equal(401, error.StatusCode); Assert.Equal(1, calls); Assert.DoesNotContain("private-token", error.Message);
    }

    [Fact]
    public async Task Api401RefreshesOnceAndRetriesUsingNewCredential()
    {
        var current = Session(); var api = 0; var refresh = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.Host == "secure.soundcloud.com") { refresh++; return Task.FromResult(Response(TokenResponse)); }
            api++; return Task.FromResult(request.Headers.Authorization?.Parameter == "old-token" ? Response("{}", 401) : Response("{\"id\":7}"));
        }));
        using var manager = new SoundCloudMobileSessionManager(new(http, Options()), () => current, next => current = next);
        using var transport = new SoundCloudHttpTransport(http, () => current, authorization: manager);
        using var me = await transport.GetJsonAsync(new("https://api-v2.soundcloud.com/me"));
        Assert.Equal(7, me.RootElement.GetProperty("id").GetInt32()); Assert.Equal(1, refresh); Assert.Equal(2, api);
    }

    [Fact]
    public async Task ExpiringSessionRefreshesBeforeApiRequestAndSecond401IsNotRetried()
    {
        var current = Session() with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(10) }; var api = 0; var refresh = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.Host == "secure.soundcloud.com")
            {
                refresh++; return Task.FromResult(Response("{\"access_token\":\"token-" + refresh + "\",\"refresh_token\":\"refresh-" + refresh + "\",\"expires_in\":3600}"));
            }
            api++; Assert.NotEqual("old-token", request.Headers.Authorization?.Parameter); return Task.FromResult(Response("{}", 401));
        }));
        using var manager = new SoundCloudMobileSessionManager(new(http, Options()), () => current, next => current = next);
        using var transport = new SoundCloudHttpTransport(http, () => current, authorization: manager);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => transport.GetJsonAsync(new("https://api-v2.soundcloud.com/me")));
        Assert.Equal(401, error.StatusCode); Assert.Equal(2, refresh); Assert.Equal(2, api);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task TemporaryAuthFailuresBackOffWithoutRevokingFamily(int status)
    {
        var current = Session(); var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Response("{}", status)); }));
        using var manager = new SoundCloudMobileSessionManager(new(http, Options()), () => current, next => current = next);
        await Assert.ThrowsAsync<SoundCloudOAuthException>(() => manager.RefreshAsync(current, true));
        var error = await Assert.ThrowsAsync<SoundCloudOAuthException>(() => manager.RefreshAsync(current, true));
        Assert.Equal(SoundCloudOAuthFailure.RateLimited, error.Failure); Assert.Equal(1, calls); Assert.Equal("old-refresh", current.RefreshToken);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
