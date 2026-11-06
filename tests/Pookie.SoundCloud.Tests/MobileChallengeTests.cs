using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class MobileChallengeTests
{
    private static WebSession Account()
    {
        var identity = new SoundCloudMobileIdentity("0123456789abcdef", "Fixture", "Model", "15");
        return new("mobileid", "private-token", identity.UserAgent) { MobileIdentity = identity, DataDomeClientId = "old-cookie", AppLocale = "ru" };
    }
    private static HttpResponseMessage Reply(string json = "{}", int status = 200) => new((HttpStatusCode)status)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Block(int kind = 259, string url = "https://geo.captcha-delivery.com/captcha/?cid=private-cid")
    {
        var response = Reply(JsonSerializer.Serialize(new { url }), 403); response.Headers.Add("X-DD-B", kind.ToString()); return response;
    }

    [Theory]
    [InlineData(1, SoundCloudChallengeKind.Captcha)]
    [InlineData(3, SoundCloudChallengeKind.DeviceCheck)]
    [InlineData(259, SoundCloudChallengeKind.InvisibleDeviceCheck)]
    public async Task ChallengeRetriesExactMobileMutationWithNewCookieOnce(int marker, SoundCloudChallengeKind kind)
    {
        var account = Account(); var calls = 0; var changed = 0; string? original = null;
        var presenter = new Presenter((challenge, _) =>
        {
            Assert.Equal(kind, challenge.Kind); Assert.Equal("ru", challenge.Language); Assert.True(challenge.IsValid());
            Assert.DoesNotContain("private-token", JsonSerializer.Serialize(challenge, SoundCloudJson.Default.SoundCloudMobileChallenge));
            Assert.DoesNotContain("private-cid", challenge.ToString()); Assert.DoesNotContain("old-cookie", challenge.ToString());
            return Task.FromResult<string?>("verified-cookie");
        });
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            Assert.Equal("/likes/playlists/create", request.RequestUri.AbsolutePath);
            Assert.Equal("private-token", request.Headers.Authorization?.Parameter);
            var body = await request.Content!.ReadAsStringAsync(token);
            if (calls == 1) { original = body; return Block(marker); }
            Assert.Equal(original, body); Assert.Equal("datadome=verified-cookie", request.Headers.GetValues("Cookie").Single());
            return Reply("""{"collection":[{"target_urn":"soundcloud:playlists:17"}]}""");
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, mobileChallenge: presenter);
        transport.SessionChanged += () => changed++;
        await transport.SetPlaylistLikedAsync(42, 17, true);
        Assert.Equal(2, calls); Assert.Equal(1, presenter.Calls); Assert.Equal(1, changed); Assert.Equal("verified-cookie", account.DataDomeClientId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SecondDenialStopsWithoutAnotherChallengeOrWebFallback(int secondMarker)
    {
        var calls = 0; var presenter = new Presenter((_, _) => Task.FromResult<string?>("verified"));
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(++calls == 1 ? Block() : Block(secondMarker))));
        using var transport = new SoundCloudHttpTransport(http, Account, mobileChallenge: presenter);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetLikedAsync(42, 17, true));
        Assert.Equal(2, calls); Assert.Equal(1, presenter.Calls);
    }

    [Theory]
    [InlineData("https://evil.test/captcha")]
    [InlineData("https://captcha-delivery.com.evil.test/")]
    [InlineData("http://geo.captcha-delivery.com/")]
    [InlineData("https://user@geo.captcha-delivery.com/")]
    public async Task UntrustedChallengeUrlCannotReachRenderer(string url)
    {
        var presenter = new Presenter((_, _) => throw new InvalidOperationException("Untrusted page reached renderer"));
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Block(url: url))));
        using var transport = new SoundCloudHttpTransport(http, Account, mobileChallenge: presenter);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetLikedAsync(42, 17, true)); Assert.Equal(0, presenter.Calls);
    }

    [Fact]
    public async Task HardBlockNeverOpensRenderer()
    {
        var presenter = new Presenter((_, _) => throw new InvalidOperationException()); var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Block(2)); }));
        using var transport = new SoundCloudHttpTransport(http, Account, mobileChallenge: presenter);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetLikedAsync(42, 17, true));
        Assert.Equal(SoundCloudChallengeKind.HardBlock, error.ChallengeKind); Assert.Equal(1, calls); Assert.Equal(0, presenter.Calls);
    }

    [Fact]
    public async Task ConcurrentMutationsShareSuccessfulChallengeAndRetainOwnBodies()
    {
        var account = Account(); var initial = 0; var retried = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presenter = new Presenter((_, _) => Task.FromResult<string?>("verified"));
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var urn = body.RootElement.GetProperty("likes")[0].GetProperty("target_urn").GetString();
            if (request.Headers.GetValues("Cookie").Single() == "datadome=old-cookie")
            {
                if (Interlocked.Increment(ref initial) == 2) both.SetResult();
                await both.Task.WaitAsync(token); return Block();
            }
            Interlocked.Increment(ref retried);
            return Reply(JsonSerializer.Serialize(new { collection = new[] { new { target_urn = urn } } }));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, mobileChallenge: presenter);
        await Task.WhenAll(transport.SetLikedAsync(42, 17, true), transport.SetLikedAsync(42, 18, true));
        Assert.Equal(2, initial); Assert.Equal(2, retried); Assert.Equal(1, presenter.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationClosesChallengeAndNeverRetries(bool read)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presenter = new Presenter(async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); return null; }
            finally { canceled.SetResult(); }
        });
        var calls = 0; var account = Account();
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Block()); }));
        using var transport = new SoundCloudHttpTransport(http, () => account, mobileChallenge: presenter);
        using var stop = new CancellationTokenSource();
        async Task Execute()
        {
            if (read) { using var result = await transport.GetJsonAsync(SoundCloudMobileRoutes.Uri("/playlists/soundcloud:playlists:17/info"), stop.Token); }
            else await transport.SetLikedAsync(42, 17, true, stop.Token);
        }
        var pending = Execute(); await entered.Task; stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls); Assert.Equal("old-cookie", account.DataDomeClientId);
    }

    [Fact]
    public async Task AccountChangeDuringChallengeCannotPublishCookieOrReplay()
    {
        var account = Account(); var original = account; var calls = 0;
        var presenter = new Presenter((_, _) => { account = account with { AccessToken = "another-account", DataDomeClientId = "other-cookie" }; return Task.FromResult<string?>("verified"); });
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Block()); }));
        using var transport = new SoundCloudHttpTransport(http, () => account, mobileChallenge: presenter);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetLikedAsync(42, 17, true));
        Assert.Equal(1, calls); Assert.Equal("other-cookie", account.DataDomeClientId); Assert.Equal("old-cookie", original.DataDomeClientId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("old-cookie")]
    [InlineData("invalid;cookie")]
    public async Task UnchangedOrMissingCookieCannotReplay(string? cookie)
    {
        var calls = 0; var presenter = new Presenter((_, _) => Task.FromResult(cookie));
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Block()); }));
        using var transport = new SoundCloudHttpTransport(http, Account, mobileChallenge: presenter);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetLikedAsync(42, 17, true)); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OAuthCodeExchangeRetainsPkceBodyAndClientAuthenticationAcrossChallenge()
    {
        var options = new SoundCloudMobileOAuthOptions("mobileid", "client-secret", Account().MobileIdentity!);
        var calls = 0; string? body = null, auth = null;
        var presenter = new Presenter((challenge, _) =>
        {
            var serialized = JsonSerializer.Serialize(challenge, SoundCloudJson.Default.SoundCloudMobileChallenge);
            Assert.DoesNotContain("private-code", serialized); Assert.DoesNotContain("client-secret", serialized);
            return Task.FromResult<string?>("verified");
        });
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            Assert.Equal("api-auth.soundcloud.com", request.RequestUri!.Host);
            var current = await request.Content!.ReadAsStringAsync(token);
            if (calls == 1)
            {
                Assert.False(request.Headers.Contains("Cookie"));
                body = current; auth = request.Headers.GetValues("Authorization").Single(); return Block(1);
            }
            Assert.Equal(body, current); Assert.Equal(auth, request.Headers.GetValues("Authorization").Single());
            Assert.Equal("datadome=verified", request.Headers.GetValues("Cookie").Single());
            return Reply("""{"access_token":"new-token","refresh_token":"new-refresh","expires_in":3600}""");
        }));
        var oauth = new SoundCloudMobileOAuth(http, options, mobileChallenge: presenter);
        var session = await oauth.ExchangeAsync(oauth.Begin(), new("private-code", DataDomeClientId: "desktop-cookie"));
        Assert.Equal("verified", session.DataDomeClientId); Assert.Equal(2, calls); Assert.Equal(1, presenter.Calls);
    }

    [Theory]
    [InlineData("https://geo.captcha-delivery.com/captcha/")]
    [InlineData("/dduser-challenge/?cid=fixture")]
    public async Task RedirectChallengeIsNotMistakenForApiOrMediaSuccess(string location)
    {
        var account = Account(); var calls = 0;
        var presenter = new Presenter((challenge, _) =>
        {
            Assert.Equal(SoundCloudChallengeKind.Redirect, challenge.Kind); Assert.True(challenge.IsValid());
            Assert.True(Uri.TryCreate(challenge.Url, UriKind.Absolute, out var parsed)); Assert.NotNull(parsed); return Task.FromResult<string?>("verified");
        });
        using var http = new HttpClient(new Handler((_, _) =>
        {
            if (++calls != 1) return Task.FromResult(Reply("""{"features":[]}"""));
            var redirect = Reply(status: 302); redirect.Headers.Location = new(location, UriKind.RelativeOrAbsolute); return Task.FromResult(redirect);
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, mobileChallenge: presenter);
        await transport.GetMobileConfigurationAsync();
        Assert.Equal(2, calls); Assert.Equal(1, presenter.Calls);
    }

    [Fact]
    public async Task HtmlChallengeRetainsTrustedApiBaseWithoutSendingMutationBodyToRenderer()
    {
        var account = Account(); var calls = 0;
        const string html = "<!doctype html><script>Android.onCaptchaSuccess('datadome=new');</script>";
        var presenter = new Presenter((challenge, _) =>
        {
            Assert.Equal(html, challenge.Html); Assert.Null(challenge.Url); Assert.True(challenge.IsValid());
            Assert.Equal("api-mobile.soundcloud.com", new Uri(challenge.RequestUrl).Host); return Task.FromResult<string?>("verified");
        });
        using var http = new HttpClient(new Handler((_, _) =>
        {
            if (++calls != 1) return Task.FromResult(Reply("""{"collection":[{"target_urn":"soundcloud:tracks:17"}]}"""));
            var denied = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };
            denied.Headers.Add("X-DD-B", "1"); return Task.FromResult(denied);
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, mobileChallenge: presenter);
        await transport.SetLikedAsync(42, 17, true); Assert.Equal(2, calls); Assert.Equal(1, presenter.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshChallengeIsBoundedAndDoesNotInvalidateRefreshToken(bool hardBlock)
    {
        var options = new SoundCloudMobileOAuthOptions("mobileid", "client-secret", Account().MobileIdentity!);
        var account = Account() with { RefreshToken = "private-refresh" }; var calls = 0;
        var presenter = new Presenter((_, _) => Task.FromResult<string?>("verified"));
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal("secure.soundcloud.com", request.RequestUri!.Host);
            Assert.False(request.Headers.Contains("Authorization")); return Task.FromResult(Block(hardBlock ? 2 : 1));
        }));
        var oauth = new SoundCloudMobileOAuth(http, options, mobileChallenge: presenter);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => oauth.RefreshAsync(account));
        Assert.True(error.RequiresBrowserVerification); Assert.Equal(hardBlock ? 1 : 2, calls); Assert.Equal(hardBlock ? 0 : 1, presenter.Calls);
        Assert.Equal("private-refresh", account.RefreshToken);
    }

    [Fact]
    public async Task MobileAudioRetriesRangeAndKeepsCookieOffCdn()
    {
        var account = Account(); var calls = 0; var changed = 0;
        var presenter = new Presenter((_, _) => Task.FromResult<string?>("verified"));
        using var http = new HttpClient(new SoundCloudMediaSessionHandler(() => account, presenter, () => changed++)
        {
            InnerHandler = new Handler((request, _) =>
            {
                calls++; Assert.Equal("bytes=200-", request.Headers.Range?.ToString());
                if (calls == 1) return Task.FromResult(Block());
                if (calls == 2)
                {
                    Assert.Equal("datadome=verified", request.Headers.GetValues("Cookie").Single());
                    var redirect = Reply(status: 302); redirect.Headers.Location = new("https://cf-media.sndcdn.com/audio"); return Task.FromResult(redirect);
                }
                Assert.Equal("cf-media.sndcdn.com", request.RequestUri!.Host); Assert.False(request.Headers.Contains("Cookie")); return Task.FromResult(Reply());
            })
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, SoundCloudMobileRoutes.Uri("/tracks/soundcloud:tracks:17/streams/https"));
        request.Headers.Range = new(200, null);
        using var result = await http.SendAsync(request);
        Assert.Equal(3, calls); Assert.Equal(1, presenter.Calls); Assert.Equal(1, changed);
    }

    private sealed class Presenter(Func<SoundCloudMobileChallenge, CancellationToken, Task<string?>> solve) : ISoundCloudMobileChallengeHandler
    {
        public int Calls;
        public Task<string?> SolveAsync(SoundCloudMobileChallenge challenge, CancellationToken token) { Interlocked.Increment(ref Calls); return solve(challenge, token); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
