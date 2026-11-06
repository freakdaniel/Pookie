using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed partial class MobilePlaylistTests
{
    private static WebSession Account()
    {
        var identity = new SoundCloudMobileIdentity("0123456789abcdef", "Fixture", "Model", "15");
        return new("mobileid", "mobile-token", identity.UserAgent) { MobileIdentity = identity, DataDomeClientId = "sdk-cookie" };
    }
    private static string Metadata(string sharing = "private", int count = 1, bool system = false) => $$$"""
        {"urn":"soundcloud:playlists:17","title":"Новый 🎵","sharing":"{{{sharing}}}","track_count":{{{count}}},
         "is_system_playlist":{{{system.ToString().ToLowerInvariant()}}},"_embedded":{"user":{"urn":"soundcloud:users:42","username":"Owner"} } }
        """;
    private static string Info(string sharing = "private", int count = 1, string tracks = "[{\"urn\":\"soundcloud:tracks:123\"}]", string next = "null", bool system = false) => $$$"""
        {"playlist":{{{Metadata(sharing, count, system)}}},"tracks":{"collection":{{{tracks}}},"_links":{"next":{"href":{{{next}}} } } } }
        """;
    private static HttpResponseMessage Json(string json, int status = 200) => new((HttpStatusCode)status)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    public async Task CreationFromFacadeUsesAndroidContractAndConfirmsSeedWithoutBrowser(string sharing)
    {
        var calls = 0; var account = Account();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            Assert.Contains("client_id=mobileid", request.RequestUri.Query);
            Assert.Equal("OAuth", request.Headers.Authorization?.Scheme);
            Assert.Equal("mobile-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("datadome=sdk-cookie", request.Headers.GetValues("Cookie").Single());
            Assert.False(request.Headers.Contains("x-datadome-clientid"));
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/playlists", request.RequestUri.AbsolutePath);
                Assert.Equal("utf-8", request.Content!.Headers.ContentType?.CharSet);
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
                var playlist = body.RootElement.GetProperty("playlist");
                Assert.Equal(2, playlist.EnumerateObject().Count());
                Assert.Equal("Новый 🎵", playlist.GetProperty("title").GetString());
                Assert.Equal(sharing == "public", playlist.GetProperty("public").GetBoolean());
                Assert.Equal("soundcloud:tracks:123", Assert.Single(body.RootElement.GetProperty("track_urns").EnumerateArray()).GetString());
                return Json("{\"playlist\":" + Metadata(sharing) + "}", 201);
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/playlists/soundcloud:playlists:17/info", request.RequestUri.AbsolutePath);
            return Json(Info(sharing));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var created = await client.CreatePlaylistAsync(42, " Новый 🎵 ", sharing, 123);
        Assert.Equal(17, created.Id); Assert.Equal(42, created.User?.Id);
        Assert.Equal(sharing, created.Sharing); Assert.Equal(123, Assert.Single(created.Tracks).Id);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostCreationOrConfirmationResponseNeverRepeatsPost(bool loseConfirmation)
    {
        var posts = 0; var reads = 0; var browser = new Browser();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
                if (!loseConfirmation) throw new HttpRequestException("Lost POST response");
                return Task.FromResult(Json("{\"playlist\":" + Metadata() + "}", 201));
            }
            reads++; throw new HttpRequestException("Lost confirmation response");
        }));
        using var transport = new SoundCloudHttpTransport(http, Account, browser);
        if (loseConfirmation)
        {
            var error = await Assert.ThrowsAsync<PlaylistCreationUnconfirmedException>(() => transport.CreatePlaylistAsync(new(42, 123, "Новый 🎵", "private")));
            Assert.Equal(17, error.PlaylistId); Assert.IsType<HttpRequestException>(error.InnerException);
        }
        else await Assert.ThrowsAsync<HttpRequestException>(() => transport.CreatePlaylistAsync(new(42, 123, "Новый 🎵", "private")));
        Assert.Equal(1, posts); Assert.Equal(loseConfirmation ? 1 : 0, reads);
        Assert.Equal(0, browser.Preparations); Assert.Equal(0, browser.Writes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(259)]
    public async Task AndroidCreationChallengeNeverPreparesWebSessionOrRepeatsMutation(int kind)
    {
        var calls = 0; var browser = new Browser();
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++; var response = Json("{}", 403); response.Headers.Add("X-DD-B", kind.ToString()); return Task.FromResult(response);
        }));
        using var transport = new SoundCloudHttpTransport(http, Account, browser);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => transport.CreatePlaylistAsync(new(42, 123, "Новый 🎵", "private")));
        Assert.True(error.RequiresBrowserVerification);
        Assert.Equal(1, calls); Assert.Equal(0, browser.Preparations); Assert.Equal(0, browser.Writes);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"urn\":\"soundcloud:tracks:999\"}]")]
    public async Task CreationWithoutConfirmedSeedFailsWithoutAnotherPost(string tracks)
    {
        var posts = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) { posts++; return Task.FromResult(Json("{\"playlist\":" + Metadata() + "}", 201)); }
            return Task.FromResult(Json(Info(tracks: tracks)));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<PlaylistCreationUnconfirmedException>(() => transport.CreatePlaylistAsync(new(42, 123, "Новый 🎵", "private")));
        Assert.Equal(1, posts);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task ConfirmationReadRejectionIsNotReportedAsRejectedCreation(int status)
    {
        var posts = 0; var browser = new Browser();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) { posts++; return Task.FromResult(Json("{\"playlist\":" + Metadata() + "}", 201)); }
            var denied = Json("{}", status);
            if (status == 403) denied.Headers.Add("X-DD-B", "2");
            return Task.FromResult(denied);
        }));
        using var transport = new SoundCloudHttpTransport(http, Account, browser);
        var error = await Assert.ThrowsAsync<PlaylistCreationUnconfirmedException>(() => transport.CreatePlaylistAsync(new(42, 123, "Новый 🎵", "private")));
        Assert.Equal(17, error.PlaylistId);
        Assert.Equal(status, Assert.IsType<SoundCloudException>(error.InnerException).StatusCode);
        Assert.Equal(1, posts); Assert.Equal(0, browser.Preparations); Assert.Equal(0, browser.Writes);
    }

    [Fact]
    public async Task AppendTraversesEveryPageBeforeSendingSingleUrn()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            if (calls == 1) return Json(Info(count: 2, next: "\"https://api-mobile.soundcloud.com/playlists/soundcloud:playlists:17/tracks?cursor=next\""));
            if (calls == 2)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("cursor=next", request.RequestUri.Query);
                return Json("""{"collection":[{"urn":"soundcloud:tracks:456"}],"next_href":null}""");
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/playlists/soundcloud:playlists:17/tracks", request.RequestUri.AbsolutePath);
            Assert.Equal("""{"track_urn":"soundcloud:tracks:789"}""", await request.Content!.ReadAsStringAsync(token));
            return Json("{}", 204);
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await transport.AddToPlaylistAsync(42, 17, 789);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("[{\"urn\":\"soundcloud:tracks:bad\"}]", 1, false)]
    [InlineData("[{\"id\":999,\"urn\":\"soundcloud:tracks:123\"}]", 1, false)]
    [InlineData("[{\"urn\":\"soundcloud:tracks:123\"}]", 1, true)]
    public async Task UnconfirmedOrSystemPlaylistCannotBeAppended(string tracks, int count, bool system)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method); return Task.FromResult(Json(Info(count: count, tracks: tracks, system: system)));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.AddToPlaylistAsync(42, 17, 789));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PickerCompletesPartialPreviewThroughMobileInfoAndTrackPages()
    {
        var calls = 0; var account = Account();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath.StartsWith("/users/"))
            {
                Assert.Equal("/users/soundcloud:users:42/playlists/posted", request.RequestUri.AbsolutePath);
                return Task.FromResult(Json("{\"collection\":[{\"playlist\":" + Metadata(count: 2) + "}],\"_links\":{}}"));
            }
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri.Host);
            return Task.FromResult(Json(calls == 2 ? Info(count: 2, next: "\"https://api-mobile.soundcloud.com/playlists/soundcloud:playlists:17/tracks?cursor=next\"") :
                """{"collection":[{"urn":"soundcloud:tracks:789"}],"next_href":null}"""));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var choices = await client.GetPlaylistChoicesForTrackAsync(42, 789);
        Assert.Contains(Assert.Single(choices).Playlist!.Tracks, track => track.Id == 789);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(123)]
    [InlineData(789)]
    public async Task PickerAcceptsTerminalAndroidCollectionWithUnavailableTracksMissingFromCount(long selectedTrack)
    {
        var account = Account(); var calls = 0;
        var tracks = JsonSerializer.Serialize(Enumerable.Range(1, 326).Select(id => new { urn = $"soundcloud:tracks:{id}" }));
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(request.RequestUri!.AbsolutePath.StartsWith("/users/")
                ? "{\"collection\":[{\"playlist\":" + Metadata(count: 330) + "}],\"_links\":{}}"
                : "{\"playlist\":" + Metadata(count: 330) + ",\"tracks\":{\"collection\":" + tracks + ",\"_links\":{}}}"));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var choices = await client.GetPlaylistChoicesForTrackAsync(42, selectedTrack);
        var playlist = Assert.Single(choices).Playlist!;
        Assert.Equal(330, playlist.TrackCount); Assert.Equal(326, playlist.Tracks.Length); Assert.True(playlist.TrackPagesComplete);
        Assert.Equal(selectedTrack == 123, playlist.Tracks.Any(track => track.Id == selectedTrack));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(null, "track-t500x500.jpg")]
    [InlineData("", "track-t500x500.jpg")]
    [InlineData("https://i1.sndcdn.com/playlist-{size}.jpg", "playlist-t500x500.jpg")]
    public async Task PickerResolvesAndroidArtworkAndFallsBackToTrackCover(string? playlistTemplate, string expectedImage)
    {
        var account = Account(); var calls = 0;
        var metadata = Metadata().Replace("\"title\":", "\"artwork_url_template\":" + JsonSerializer.Serialize(playlistTemplate) + ",\"title\":");
        const string track = """
            {"urn":"soundcloud:tracks:123","artwork_url":null,"artwork_url_template":"https://i1.sndcdn.com/track-{size}.jpg",
             "_embedded":{"user":{"urn":"soundcloud:users:42","avatar_url_template":"https://i1.sndcdn.com/avatar-{size}.jpg"}}}
            """;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            return Task.FromResult(Json(request.RequestUri.AbsolutePath.StartsWith("/users/")
                ? "{\"collection\":[{\"playlist\":" + metadata + "}],\"_links\":{}}"
                : "{\"playlist\":" + metadata + ",\"tracks\":{\"collection\":[" + track + "],\"_links\":{}}}"));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var choice = Assert.Single(await client.GetPlaylistChoicesForTrackAsync(42, 789));
        Assert.Equal("https://i1.sndcdn.com/" + expectedImage, choice.ArtworkUrl);
        var parsedTrack = Assert.Single(choice.Playlist!.Tracks);
        Assert.Equal("https://i1.sndcdn.com/track-t500x500.jpg", parsedTrack.ArtworkUrl);
        Assert.Equal("https://i1.sndcdn.com/avatar-t500x500.jpg", parsedTrack.User?.AvatarUrl);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConcreteArtworkUrlRemainsPreferredWhenTemplateAlsoExists()
    {
        var metadata = Metadata().Replace("\"title\":", "\"artwork_url\":\"https://i1.sndcdn.com/custom.jpg\",\"artwork_url_template\":\"https://i1.sndcdn.com/template-{size}.jpg\",\"title\":");
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("{\"collection\":[{\"playlist\":" + metadata + "}],\"_links\":{}}"))));
        using var transport = new SoundCloudHttpTransport(http, Account);
        Assert.Equal("https://i1.sndcdn.com/custom.jpg", Assert.Single(await transport.GetMobileOwnedPlaylistsAsync(42)).ArtworkUrl);
    }

    [Fact]
    public async Task AppendUsesSingleUrnWhenTerminalCollectionOmitsUnavailableTracks()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            if (request.Method == HttpMethod.Get) return Json(Info(count: 330));
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            Assert.Equal("/playlists/soundcloud:playlists:17/tracks", request.RequestUri.AbsolutePath);
            Assert.Equal("""{"track_urn":"soundcloud:tracks:789"}""", await request.Content!.ReadAsStringAsync(token));
            return Json("{}", 204);
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await transport.AddToPlaylistAsync(42, 17, 789);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CountMismatchWithoutTerminalCursorMetadataStillFails()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("{\"playlist\":" + Metadata(count: 330) +
            ",\"tracks\":{\"collection\":[{\"urn\":\"soundcloud:tracks:123\"}]}}"))));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.GetMobilePlaylistAsync(17));
    }

    [Fact]
    public async Task OwnedListFollowsAndroidLinksAndFiltersForeignAndSystemPlaylists()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            if (calls == 1)
                return Task.FromResult(Json("{\"collection\":[{\"playlist\":" + Metadata() +
                    "}],\"_links\":{\"next\":{\"href\":\"https://api-mobile.soundcloud.com/users/soundcloud:users:42/playlists/posted?cursor=next\"}}}"));
            Assert.Contains("cursor=next", request.RequestUri.Query);
            return Task.FromResult(Json("{\"collection\":[{\"playlist\":" + Metadata().Replace("soundcloud:playlists:17", "soundcloud:playlists:18") +
                "},{\"playlist\":" + Metadata(system: true) + "},{\"playlist\":" + Metadata().Replace("soundcloud:users:42", "soundcloud:users:99") + "}],\"_links\":{}}"));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        var items = await transport.GetMobileOwnedPlaylistsAsync(42);
        Assert.Equal(new long[] { 17, 18 }, items.Select(item => item.Playlist!.Id));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AndroidOwnerBadgeArrayIsNormalizedToWebModelFlags()
    {
        var metadata = Metadata().Replace("\"username\":\"Owner\"", "\"username\":\"Owner\",\"badges\":[\"VERIFIED\",\"PRO_UNLIMITED\",\"unknown-future-badge\"]");
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("{\"collection\":[{\"playlist\":" + metadata + "}],\"_links\":{}}"))));
        using var transport = new SoundCloudHttpTransport(http, Account);
        var items = await transport.GetMobileOwnedPlaylistsAsync(42);
        var owner = Assert.Single(items).Playlist!.User!;
        Assert.True(owner.IsVerified); Assert.True(owner.HasArtistPro);
    }

    [Theory]
    [InlineData("https://evil.test/steal")]
    [InlineData("https://api-mobile.soundcloud.com/users/soundcloud:users:99/playlists/posted")]
    [InlineData("https://api-mobile.soundcloud.com/playlists/soundcloud:playlists:17/info")]
    public async Task OwnedListCursorCannotMoveToAnotherResource(string cursor)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json("{\"collection\":[],\"_links\":{\"next\":{\"href\":" + JsonSerializer.Serialize(cursor) + "}}}"));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.GetMobileOwnedPlaylistsAsync(42));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void CreationAllowlistDoesNotPermitOtherMethodsOrHosts()
    {
        var uri = SoundCloudMobileRoutes.Uri("/playlists");
        Assert.True(SoundCloudMobileRoutes.IsAllowed(uri, HttpMethod.Post));
        Assert.False(SoundCloudMobileRoutes.IsAllowed(uri, HttpMethod.Put));
        Assert.False(SoundCloudMobileRoutes.IsAllowed(uri, HttpMethod.Get));
        Assert.False(SoundCloudMobileRoutes.IsAllowed(new("https://evil.test/playlists"), HttpMethod.Post));
    }

    private sealed class Browser : ISoundCloudBrowserTransport, ISoundCloudSessionVerifier
    {
        public int Preparations;
        public int Writes;
        public Task PrepareSessionAsync(CancellationToken cancellationToken = default) { Preparations++; return Task.CompletedTask; }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected browser read");
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) { Writes++; return Task.CompletedTask; }
        public Task<JsonDocument> CreatePlaylistAsync(PlaylistCreateRequest request, CancellationToken cancellationToken = default)
        { Writes++; return Task.FromResult(JsonDocument.Parse("{}")); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
