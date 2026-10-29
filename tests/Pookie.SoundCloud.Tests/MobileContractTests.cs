using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class MobileContractTests
{
    private static WebSession Account()
    {
        var identity = new SoundCloudMobileIdentity("0123456789abcdef", "Pookie", "Fixture", "15");
        return new("client", "token", identity.UserAgent) { MobileIdentity = identity, DataDomeClientId = "cookie" };
    }
    private static HttpResponseMessage Json(string body = "{}", int status = 200) => new((HttpStatusCode)status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("f=true,g=false", true)]
    [InlineData("system_playlist_in_library=false", true)]
    [InlineData("f", false)]
    [InlineData("f=1", false)]
    [InlineData("f=true,f=false", false)]
    [InlineData("f=true,", false)]
    [InlineData("f=true\r\nsecret", false)]
    public void FeaturesUseBooleanEntriesAndForceAndroidLibraryCapability(string value, bool valid)
    {
        var identity = Account().MobileIdentity! with { RequestedFeatures = value };
        Assert.Equal(valid, identity.IsValid());
        if (valid)
        {
            Assert.EndsWith("system_playlist_in_library=true", identity.FeaturesHeader);
            Assert.DoesNotContain("system_playlist_in_library=false", identity.FeaturesHeader);
        }
    }

    [Theory]
    [InlineData("follow", true, "/follows/users/soundcloud:users:42", "POST")]
    [InlineData("follow", false, "/follows/users/soundcloud:users:42", "DELETE")]
    [InlineData("track", true, "/reposts/tracks/soundcloud:tracks:42", "POST")]
    [InlineData("track", false, "/reposts/tracks/soundcloud:tracks:42", "DELETE")]
    [InlineData("playlist", true, "/reposts/playlists/soundcloud:playlists:42", "POST")]
    [InlineData("playlist", false, "/reposts/playlists/soundcloud:playlists:42", "DELETE")]
    public async Task ActionsUsePrivateMobileRoutesWithoutWebSignature(string kind, bool enabled, string path, string method)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            Assert.Equal(path, request.RequestUri.AbsolutePath); Assert.Equal(method, request.Method.Method);
            Assert.DoesNotContain("signature", request.RequestUri.Query); Assert.Null(request.Content);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(Json(status: 204));
        }));
        using var native = new SoundCloudHttpTransport(http, Account);
        if (kind == "follow") await native.SetFollowingAsync(7, 42, enabled);
        else if (kind == "track") await native.SetRepostedAsync(42, enabled);
        else await native.SetPlaylistRepostedAsync(42, enabled);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"collection\":[]}")]
    [InlineData("{\"collection\":[{\"target_urn\":\"soundcloud:tracks:99\"}]}")]
    [InlineData("{\"collection\":[{\"target_urn\":\"soundcloud:playlists:42\"}]}")]
    [InlineData("{\"collection\":[{}]}")]
    public async Task LikeWithoutMatchingReceiptFailsOnceAndDoesNotReplay(string body)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Json(body)); }));
        using var native = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => native.SetLikedAsync(7, 42, true));
        Assert.Equal(1, calls);
    }

    private static string Playlist(string owner = "soundcloud:users:7", bool album = false, int count = 2, string tracks = "[]", string next = "null") =>
        $$$"""{"playlist":{"urn":"soundcloud:playlists:11","_embedded":{"user":{"urn":"{{{owner}}}"}},"is_album":{{{album.ToString().ToLowerInvariant()}}},"track_count":{{{count}}}},"tracks":{"collection":{{{tracks}}},"next_href":{{{next}}}}}""";

    [Fact]
    public async Task PlaylistAppendReadsAndroidWrapperAndSendsSingleTrackUrn()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++; Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/playlists/soundcloud:playlists:11/info", request.RequestUri.AbsolutePath);
                return Json(Playlist());
            }
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/playlists/soundcloud:playlists:11/tracks", request.RequestUri.AbsolutePath);
            Assert.Equal("""{"track_urn":"soundcloud:tracks:42"}""", await request.Content!.ReadAsStringAsync(token));
            return Json(status: 204);
        }));
        using var native = new SoundCloudHttpTransport(http, Account);
        await native.AddToPlaylistAsync(7, 11, 42); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("soundcloud:users:9", false, 2)]
    [InlineData("soundcloud:users:7", true, 2)]
    [InlineData("soundcloud:users:7", false, 500)]
    public async Task PlaylistOwnershipAlbumAndCapacityRemainEnforced(string owner, bool album, int count)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        { calls++; Assert.Equal(HttpMethod.Get, request.Method); return Task.FromResult(Json(Playlist(owner, album, count))); }));
        using var native = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => native.AddToPlaylistAsync(7, 11, 42)); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PlaylistUrnDuplicateOnSecondPageDoesNotWrite()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(calls == 1 ? Playlist(next: "\"https://api-mobile.soundcloud.com/playlists/soundcloud:playlists:11/tracks?cursor=next\"") :
                """{"collection":[{"urn":"soundcloud:tracks:42"}],"next_href":null}"""));
        }));
        using var native = new SoundCloudHttpTransport(http, Account);
        await native.AddToPlaylistAsync(7, 11, 42); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("https://evil.test/steal")]
    [InlineData("https://api-mobile.soundcloud.com/playlists/soundcloud:playlists:12/tracks")]
    [InlineData("https://api-mobile.soundcloud.com/likes/tracks/create")]
    public async Task PlaylistCursorCannotChooseDifferentResource(string cursor)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Json(Playlist(next: JsonSerializer.Serialize(cursor)))); }));
        using var native = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => native.AddToPlaylistAsync(7, 11, 42)); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ConfigurationUsesMobileHostAndExactBooleanFeatureHeader()
    {
        var account = Account(); account = account with { MobileIdentity = account.MobileIdentity! with { RequestedFeatures = "optional=false" } };
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            Assert.Equal("/configuration/android", request.RequestUri.AbsolutePath);
            Assert.Equal("optional=false,system_playlist_in_library=true", request.Headers.GetValues("App-Requested-Features").Single());
            return Task.FromResult(Json("""{"features":[{"name":"datadome","enabled":true}]}"""));
        }));
        using var native = new SoundCloudHttpTransport(http, () => account);
        Assert.True((await native.GetMobileConfigurationAsync()).Enabled("datadome"));
        Assert.True((await native.GetMobileConfigurationAsync()).Enabled("datadome")); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MobileOnlyReadsRecoverSessionWithoutExecutingEndpointInBrowser(bool stream, bool accepted)
    {
        var calls = 0; var browser = new SessionBrowser();
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            if (calls == 1 || !accepted)
            {
                var blocked = Json(status: 403); blocked.Headers.Add("X-DD-B", "259"); return Task.FromResult(blocked);
            }
            var response = stream ? Json(status: 302) : Json("""{"features":[]}""");
            if (stream) response.Headers.Location = new("https://cf-media.sndcdn.com/full");
            return Task.FromResult(response);
        }));
        using var native = new SoundCloudHttpTransport(http, Account, browser);
        async Task ReadAsync()
        {
            if (stream) { using var result = await native.ResolveMobileStreamAsync(SoundCloudMobileRoutes.Uri("/tracks/soundcloud:tracks:42/streams/https")); }
            else await native.GetMobileConfigurationAsync();
        }
        if (accepted) await ReadAsync();
        else Assert.True((await Assert.ThrowsAsync<SoundCloudException>(ReadAsync)).RequiresBrowserVerification);
        Assert.Equal(1, browser.Preparations); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("https://cf-media.sndcdn.com/full", true)]
    [InlineData("https://playback.media-streaming.soundcloud.cloud/full", true)]
    [InlineData("https://evil.test/full", false)]
    [InlineData("https://cf-media.sndcdn.com/preview/full", false)]
    [InlineData("http://cf-media.sndcdn.com/full", false)]
    [InlineData("https://api-mobile.soundcloud.com/tracks/soundcloud:tracks:99/streams/https", false)]
    public async Task MobileStreamRedirectIsValidatedBeforePlayback(string location, bool valid)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            var response = Json(status: 302); response.Headers.Location = new(location); return Task.FromResult(response);
        }));
        using var native = new SoundCloudHttpTransport(http, Account);
        var uri = SoundCloudMobileRoutes.Uri("/tracks/soundcloud:tracks:42/streams/https");
        if (valid) { using var stream = await native.ResolveMobileStreamAsync(uri); Assert.Equal(location, stream.RootElement.GetProperty("url").GetString()); }
        else await Assert.ThrowsAsync<SoundCloudException>(() => native.ResolveMobileStreamAsync(uri));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DirectMobileAudioIsNotBufferedOrParsedAsJson()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnreadableAudio() })));
        var account = Account(); using var native = new SoundCloudHttpTransport(http, () => account);
        var api = new SoundCloudWebClient(http) { Session = account, Transport = native };
        var track = SoundCloudMobileStreams.WithFallbacks(new() { Id = 42, Duration = 180000 });
        var stream = await api.ResolveTranscodingAsync(track, track.Media!.Transcodings[0]);
        Assert.NotNull(stream); Assert.True(SoundCloudMobileRoutes.IsStream(stream.Uri));
        Assert.Equal("progressive", stream.Protocol); Assert.Contains("client_id=client", stream.Uri.Query);
    }

    [Theory]
    [InlineData("https://cf-media.sndcdn.com/full", true)]
    [InlineData("https://evil.test/full", false)]
    public async Task AudioRedirectsRetainRangeButScopeDeviceCookieToMobileApi(string location, bool valid)
    {
        var calls = 0;
        using var http = new HttpClient(new SoundCloudMediaSessionHandler(Account)
        {
            InnerHandler = new Handler((request, _) =>
            {
                calls++; Assert.Equal("token", request.Headers.Authorization?.Parameter); Assert.Equal("bytes=100-", request.Headers.Range?.ToString());
                if (calls == 1)
                {
                    Assert.Equal("datadome=cookie", request.Headers.GetValues("Cookie").Single());
                    var response = Json(status: 302); response.Headers.Location = new(location); return Task.FromResult(response);
                }
                Assert.Equal("cf-media.sndcdn.com", request.RequestUri!.Host);
                Assert.False(request.Headers.Contains("Cookie")); Assert.False(request.Headers.Contains("UDID")); return Task.FromResult(Json());
            })
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, SoundCloudMobileRoutes.Uri("/tracks/soundcloud:tracks:42/streams/https"));
        request.Headers.Range = new RangeHeaderValue(100, null);
        if (valid) { using var response = await http.SendAsync(request); Assert.Equal(2, calls); }
        else { await Assert.ThrowsAsync<SoundCloudException>(() => http.SendAsync(request)); Assert.Equal(1, calls); }
    }

    [Fact]
    public async Task AudioRedirectLoopIsBounded()
    {
        var calls = 0;
        using var http = new HttpClient(new SoundCloudMediaSessionHandler(Account)
        {
            InnerHandler = new Handler((_, _) => { calls++; var reply = Json(status: 302); reply.Headers.Location = new("https://cf-media.sndcdn.com/loop"); return Task.FromResult(reply); })
        });
        await Assert.ThrowsAsync<SoundCloudException>(() => http.GetAsync("https://cf-media.sndcdn.com/loop")); Assert.Equal(6, calls);
    }

    private sealed class UnreadableAudio : HttpContent
    {
        public UnreadableAudio() { Headers.ContentType = new("audio/mpeg"); Headers.ContentLength = 100_000_000; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("Audio must not be buffered by resolver");
        protected override bool TryComputeLength(out long length) { length = 100_000_000; return true; }
    }
    private sealed class SessionBrowser : ISoundCloudBrowserTransport, ISoundCloudSessionVerifier
    {
        public int Preparations;
        public Task PrepareSessionAsync(CancellationToken cancellationToken = default) { Preparations++; return Task.CompletedTask; }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Mobile operation must remain native");
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Read recovery must not mutate account");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
