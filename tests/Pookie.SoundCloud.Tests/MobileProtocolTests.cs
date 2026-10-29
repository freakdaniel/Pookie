using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class MobileProtocolTests
{
    private static SoundCloudMobileOAuthOptions Options() => new("mobileid", "destination-secret", new("0123456789abcdef", "Pookie", "Fixture", "15"))
        { ExchangeClient = new("legacyid", "source-secret") };
    private static WebSession Account() => new("mobileid", "token", Options().MobileIdentity.UserAgent) { MobileIdentity = Options().MobileIdentity };
    private static HttpResponseMessage Reply(string json = "{}", int status = 200) => new((HttpStatusCode)status)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(false, true, "tracks", "create")]
    [InlineData(false, false, "tracks", "delete")]
    [InlineData(true, true, "playlists", "create")]
    [InlineData(true, false, "playlists", "delete")]
    public async Task AndroidBatchLikesUseMobileHostInsteadOfMissingV2Route(bool playlist, bool liked, string kind, string action)
    {
        var calls = 0; SoundCloudHttpObservation? observed = null;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            if (request.RequestUri!.Host == "api-v2.soundcloud.com") return Reply(status: 404);
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri.Host); Assert.Equal(443, request.RequestUri.Port);
            Assert.Equal($"/likes/{kind}/{action}", request.RequestUri.AbsolutePath); Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal($"soundcloud:{kind}:42", json.RootElement.GetProperty("likes")[0].GetProperty("target_urn").GetString());
            return Reply($$"""{"collection":[{"target_urn":"soundcloud:{{kind}}:42"}]}""");
        }));
        using var native = new SoundCloudHttpTransport(http, Account); native.Observed += message => observed = message;
        if (playlist) await native.SetPlaylistLikedAsync(7, 42, liked); else await native.SetLikedAsync(7, 42, liked);
        Assert.Equal(1, calls); Assert.Equal("api-mobile.soundcloud.com", observed?.Host); Assert.Equal(200, observed?.Status);
    }

    [Fact]
    public async Task GenuineMobileLike404DoesNotTryAnotherWriteRouteOrMarkTrackUnavailable()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Reply(status: 404)); }));
        using var native = new SoundCloudHttpTransport(http, Account);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => native.SetLikedAsync(7, 42, true));
        Assert.Equal(404, error.StatusCode); Assert.False(error.TrackUnavailable); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LegacyExchangeAuthenticatesSourceAndIssuesForDestinationWithoutRelabellingWebToken()
    {
        var calls = 0; var options = Options();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++; Assert.False(request.Headers.Contains("Authorization"));
            var fields = (await request.Content!.ReadAsStringAsync(token)).Split('&').Select(part => part.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
            Assert.Equal("token_exchange", fields["grant_type"]); Assert.Equal("legacy-token", fields["token"]);
            Assert.Equal("legacyid", fields["client_id"]); Assert.Equal("source-secret", fields["client_secret"]);
            Assert.Equal("mobileid", fields["issue_for_client_id"]);
            return Reply("""{"access_token":"mobile-token","refresh_token":"mobile-refresh","expires_in":3600}""");
        }));
        var oauth = new SoundCloudMobileOAuth(http, options);
        var source = new WebSession("legacyid", "legacy-token", "Fixture/1") { DataDomeClientId = "web-protection" };
        var mobile = await oauth.ExchangeTokenAsync(source);
        Assert.Equal("mobileid", mobile.ClientId); Assert.Equal(options.MobileIdentity, mobile.MobileIdentity); Assert.NotNull(mobile.SessionId);
        Assert.Null(mobile.DataDomeClientId); Assert.Equal("legacy-token", source.AccessToken);
        await Assert.ThrowsAsync<SoundCloudOAuthException>(() => oauth.ExchangeTokenAsync(source with { ClientId = "webclient" }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MobileConfigurationIsCachedAndSduiUsesFixedAndroidPersistedQuery()
    {
        var configuration = 0; var graph = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.Equal("377050", request.Headers.GetValues("App-Version").Single());
            if (request.RequestUri!.AbsolutePath == "/configuration/android")
            { configuration++; return Reply("""{"features":[{"name":"sdui_graph_home","enabled":true},{"name":"sdui_persisted_queries","enabled":true}]}"""); }
            graph++; Assert.Equal("android", request.Headers.GetValues("apollographql-client-name").Single());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = json.RootElement; var catalog = root.GetProperty("operationName").GetString() == "CatalogSduiQuery";
            Assert.Equal(catalog ? "34dd1fa2b0cfcf0763822051190d187b22af82dc7f643970a9beac0a4c74c084" :
                "a2e83962341cd01e02a0bda9141f3c2c47c9002e9110114c3b24565e5cedc248", root.GetProperty("extensions").GetProperty("persistedQuery").GetProperty("sha256Hash").GetString());
            if (!catalog)
            {
                var input = root.GetProperty("variables").GetProperty("input"); Assert.True(input.GetProperty("useLiteLayout").GetBoolean());
                Assert.Equal("cursor", input.GetProperty("after").GetString()); Assert.Equal(20, input.GetProperty("first").GetInt32());
            }
            Assert.False(root.TryGetProperty("query", out _)); return Reply("""{"data":{}}""");
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        using var home = await transport.GetMobileSduiAsync(after: "cursor");
        using var catalog = await transport.GetMobileSduiAsync(catalog: true);
        Assert.Equal(1, configuration); Assert.Equal(2, graph);
    }

    [Fact]
    public async Task DisabledSduiFallsBackToRestWithoutSendingGraphRequest()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(Reply(request.RequestUri.AbsolutePath == "/configuration/android"
                ? """{"features":[{"name":"sdui_persisted_queries","enabled":false}]}""" : """{"collection":[]}"""));
        }));
        var account = Account(); using var transport = new SoundCloudHttpTransport(http, () => account);
        var api = new SoundCloudWebClient(http) { Session = account, Transport = transport, RequireSessionTransport = true };
        Assert.Empty(await api.GetPersonalMixesAsync(7));
        Assert.Equal(["/configuration/android", "/mixed-selections"], paths);
    }

    [Fact]
    public async Task UnknownPersistedQueryIsNotRetriedOnEveryHomeLoad()
    {
        var graph = 0; var rest = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/configuration/android") return Task.FromResult(Reply("""{"features":[]}"""));
            if (request.RequestUri.Host == "graph.soundcloud.com")
            { graph++; return Task.FromResult(Reply("""{"errors":[{"message":"PersistedQueryNotFound"}]}""")); }
            rest++; return Task.FromResult(Reply("""{"collection":[]}"""));
        }));
        var account = Account(); using var native = new SoundCloudHttpTransport(http, () => account);
        var api = new SoundCloudWebClient(http) { Session = account, Transport = native };
        await api.GetPersonalMixesAsync(7); await api.GetPersonalMixesAsync(7);
        Assert.Equal(1, graph); Assert.Equal(2, rest);
    }

    [Fact]
    public void MobileHomeExtractsOwnSystemPlaylistsWithoutCrossAccountMixes()
    {
        using var json = JsonDocument.Parse("""
            {"data":{"home":{"sections":[{"items":[
             {"urn":"soundcloud:system-playlists:weekly:1","title":"Weekly","made_for":{"id":7}},
             {"urn":"soundcloud:system-playlists:weekly:2","title":"Foreign","made_for":{"id":8}}]}]}}}
            """);
        Assert.Equal("Weekly", Assert.Single(DiscoverData.MobilePersonalMixes(json.RootElement, 7)).Title);
    }

    [Fact]
    public async Task MissingMobileMediaUsesSecureHttpsThenHlsFallbackWithoutRequestingPreview()
    {
        var paths = new List<string>(); var account = Account();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (paths.Count == 1) return Task.FromResult(Reply("""{"id":42,"duration":180000,"policy":"ALLOW"}"""));
            Assert.Contains("secure=true", request.RequestUri.Query); Assert.Contains("can_snip=false", request.RequestUri.Query);
            return Task.FromResult(paths.Count == 2 ? Reply(status: 404) : Reply("""{"url":"https://cf-media.sndcdn.com/full","snipped":false}"""));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var api = new SoundCloudWebClient(http) { Session = account, Transport = transport, RequireSessionTransport = true };
        var result = await new SoundCloudStreamResolver(api).ResolveAsync(42);
        Assert.Equal("hls", result.Stream.Protocol); Assert.Equal(180, result.Stream.Duration);
        Assert.Equal(["/tracks/42", "/tracks/soundcloud:tracks:42/streams/https", "/tracks/soundcloud:tracks:42/streams/hls"], paths);
    }

    [Fact]
    public async Task MobileStreamResponseCannotSmuggleSnippetAsFullPlayback()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Reply("""{"url":"https://cf-media.sndcdn.com/preview","snipped":true}"""))));
        var account = Account(); using var transport = new SoundCloudHttpTransport(http, () => account);
        var api = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var track = SoundCloudMobileStreams.WithFallbacks(new() { Id = 42 });
        Assert.Null(await api.ResolveTranscodingAsync(track, track.Media!.Transcodings[0]));
    }

    [Fact]
    public async Task MobileFallbackRetainsServerDrmProtocolInsteadOfTreatingItAsPlainHls()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Reply(
            """{"url":"https://cf-media.sndcdn.com/protected","protocol":"ctr-encrypted-hls","licenseAuthToken":"license-token","snipped":false}"""))));
        var account = Account(); using var native = new SoundCloudHttpTransport(http, () => account);
        var api = new SoundCloudWebClient(http) { Session = account, Transport = native };
        var track = SoundCloudMobileStreams.WithFallbacks(new() { Id = 42 });
        var stream = await api.ResolveTranscodingAsync(track, track.Media!.Transcodings[1]);
        Assert.NotNull(stream); Assert.True(stream.Protected); Assert.Equal("license-token", stream.LicenseAuthToken);
    }

    [Fact]
    public async Task CdnAuthenticationUsesRotatedTokenAndNeverLeaksToForeignOrLicenseHosts()
    {
        var account = Account(); var headers = new List<string?>();
        using var http = new HttpClient(new SoundCloudMediaSessionHandler(() => account)
        {
            InnerHandler = new Handler((request, _) =>
            {
                headers.Add(request.Headers.Authorization?.Parameter);
                if (request.Headers.Authorization?.Scheme == "OAuth") Assert.Equal(2, request.Version.Major);
                return Task.FromResult(Reply());
            })
        });
        using var first = await http.GetAsync("https://cf-media.sndcdn.com/first");
        account = account with { AccessToken = "rotated" };
        using var next = await http.GetAsync("https://cf-media.sndcdn.com/next");
        using var foreign = await http.GetAsync("https://sndcdn.com.evil.test/next");
        using var license = await http.GetAsync("https://license.soundcloud.com/next");
        using var licenseRequest = new HttpRequestMessage(HttpMethod.Post, "https://license.sndcdn.com/widevine");
        licenseRequest.Headers.Authorization = new("Bearer", "license-authorization");
        using var post = await http.SendAsync(licenseRequest);
        Assert.Equal(["token", "rotated", null, null, "license-authorization"], headers);
    }

    [Fact]
    public void AdvancedSnapshotSurvivesStorageWithStableProfileEqualityAndRejectsCredentialOverrides()
    {
        var signals = new SoundCloudMobileSignals("product", "device", "hardware", "fingerprint", "release-keys", "V", 35, 1080, 2400, 3, "{}")
            { AdvancedCollectionEnabled = true, Advanced = new() { ["r_up"] = "10000", ["d_n_type"] = "wifi" } };
        var account = Account() with { MobileIdentity = Options().MobileIdentity with { Signals = signals } };
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(account, SoundCloudJson.Default.WebSession), SoundCloudJson.Default.WebSession)!;
        Assert.True(restored.IsValid()); Assert.Equal(account, restored); Assert.Equal(account.GetHashCode(), restored.GetHashCode());
        Assert.False((signals with { Advanced = new() { ["cid"] = "override" } }).IsValid());
        Assert.False((signals with { Advanced = new() { ["r_up"] = "100\r\nsecret" } }).IsValid());
        Assert.False((signals with { AdvancedCollectionEnabled = false }).IsValid());
        Assert.NotEqual(signals with { Advanced = null }, signals with { Advanced = null, AdvancedCollectionEnabled = false });
    }

    [Fact]
    public async Task AdvancedTelemetryUsesProvidedSnapshotAndTenMinuteCadenceWithoutOAuth()
    {
        var clock = new Clock(); var sends = 0;
        var signals = new SoundCloudMobileSignals("p", "d", "h", "f", "release-keys", "V", 35, 1080, 2400, 3, "{}")
            { AdvancedCollectionEnabled = true, Advanced = new() { ["d_n_type"] = "wifi", ["r_up"] = "12345" } };
        var account = Account() with { MobileIdentity = Options().MobileIdentity with { Signals = signals } };
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.Host != "api-sdk.datadome.co") return Reply("""{"id":7}""");
            sends++; Assert.Null(request.Headers.Authorization);
            var body = await request.Content!.ReadAsStringAsync(token);
            Assert.Contains("d_n_type=wifi", body); Assert.Contains("r_up=12345", body); Assert.DoesNotContain("token", body);
            Assert.Contains("ddvv=2026.10.14-release", body); Assert.Contains("d_s_is_w=1080", body);
            Assert.DoesNotContain("ddvc=", body); Assert.DoesNotContain("screen_x=", body); Assert.DoesNotContain("screen_d=", body);
            return Reply("""{"cookie":"datadome=prepared; Path=/"}""");
        }));
        using var native = new SoundCloudHttpTransport(http, () => account, timeProvider: clock);
        for (var i = 0; i < 6; i++) { using var read = await native.GetJsonAsync(new("https://api-v2.soundcloud.com/me")); }
        clock.Now = clock.Now.AddSeconds(10);
        using (var read = await native.GetJsonAsync(new("https://api-v2.soundcloud.com/me"))) Assert.Equal(1, sends);
        clock.Now = clock.Now.AddMinutes(10);
        using (var read = await native.GetJsonAsync(new("https://api-v2.soundcloud.com/me"))) Assert.Equal(2, sends);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
