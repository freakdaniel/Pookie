using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed partial class MobilePlaylistTests
{
    private static string EditInfo(string title = "Новый 🎵", string description = "Описание", string sharing = "private", int count = 2,
        long[]? ids = null, long owner = 42, string? artwork = null)
    {
        return JsonSerializer.Serialize(new
        {
            playlist = new
            {
                urn = "soundcloud:playlists:17", title, description, sharing, track_count = count, genre = "Ambient",
                user_tags = new[] { "calm", "night" }, updated_at = "2026-11-06T00:00:00Z", artwork_url_template = artwork,
                _embedded = new { user = new { urn = $"soundcloud:users:{owner}", username = "Owner" } }
            },
            tracks = new { collection = (ids ?? [123, 456]).Select(id => new { urn = $"soundcloud:tracks:{id}", title = "Track", duration = 1000 }), _links = new { } }
        });
    }

    [Fact]
    public async Task LibraryPlaylistOpensThroughMobileInfoAndPreservesCardKeyAndUnavailableCount()
    {
        var calls = 0; var account = Account();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            Assert.Equal("/playlists/soundcloud:playlists:17/info", request.RequestUri.AbsolutePath);
            return Task.FromResult(Json(EditInfo(count: 330)));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var detail = await client.GetCollectionDetailAsync(new("playlist:17", "Stale title", "", null, Playlist: new() { Id = 17 }));
        Assert.Equal("playlist:17", detail.Item.Key); Assert.Equal("Новый 🎵", detail.Item.Title);
        Assert.Equal(330, detail.Item.Playlist!.TrackCount); Assert.True(detail.Item.Playlist.TrackPagesComplete);
        Assert.Equal(new long[] { 123, 456 }, detail.Tracks.Tracks.Select(track => track.Id)); Assert.Null(detail.Tracks.NextHref);
        Assert.Equal("2026-11-06T00:00:00Z", detail.Item.Playlist.LastModified); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AndroidUpdatePreservesGenreTagsAndConfirmsMetadataAndOrder()
    {
        var puts = 0; var reads = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Json("{\"id\":42}");
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri.Host);
            if (request.Method == HttpMethod.Get) return Json(++reads == 1 ? EditInfo() : EditInfo("Вечер", "Две строки\n🎵", "public", ids: [456, 123]));
            puts++; Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/playlists/soundcloud:playlists:17", request.RequestUri.AbsolutePath);
            Assert.Equal("utf-8", request.Content!.Headers.ContentType?.CharSet);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
            var root = body.RootElement;
            Assert.False(root.TryGetProperty("playlist", out _)); Assert.Equal(6, root.EnumerateObject().Count());
            Assert.Equal(new[] { "soundcloud:tracks:456", "soundcloud:tracks:123" }, root.GetProperty("track_urns").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal("Ambient", root.GetProperty("genre").GetString()); Assert.Equal("calm night", root.GetProperty("tag_list").GetString());
            Assert.True(root.GetProperty("public").GetBoolean()); Assert.Equal("Две строки\n🎵", root.GetProperty("description").GetString());
            return Json("{}", 204);
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        var updated = await transport.UpdateMobilePlaylistAsync(new(42, 17, "Вечер", "Две строки\n🎵", "public", [456, 123], [123, 456]));
        Assert.Equal("Вечер", updated.Title); Assert.Equal(1, puts); Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(330, 42, false)]
    [InlineData(2, 99, false)]
    [InlineData(2, 42, true)]
    public async Task UnsafePlaylistCannotBeReplaced(int count, long owner, bool system)
    {
        var writes = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Task.FromResult(Json("{\"id\":42}"));
            if (request.Method != HttpMethod.Get) writes++;
            return Task.FromResult(Json(EditInfo(count: count, owner: owner).Replace("\"title\":\"", "\"is_system_playlist\":" + system.ToString().ToLowerInvariant() + ",\"title\":\"")));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.UpdateMobilePlaylistAsync(new(42, 17, "Вечер", "", "private", [456, 123])));
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(330, 326)]
    [InlineData(4, 0)]
    [InlineData(2, 2)]
    public async Task MetadataOnlyEditNeverSerializesTrackOrderAndPreservesHiddenEntries(int declared, int available)
    {
        var backingOrder = Enumerable.Range(1, declared).Select(id => (long)id).ToArray();
        var original = backingOrder.ToArray(); var writes = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Json("{\"id\":42}");
            if (request.Method == HttpMethod.Put)
            {
                writes++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var root = body.RootElement;
                Assert.Equal(3, root.EnumerateObject().Count());
                Assert.False(root.TryGetProperty("track_urns", out _)); Assert.False(root.TryGetProperty("tracks", out _));
                Assert.False(root.TryGetProperty("genre", out _)); Assert.False(root.TryGetProperty("tag_list", out _));
                Assert.Equal("Вечер", root.GetProperty("title").GetString()); Assert.True(root.GetProperty("public").GetBoolean());
                // Simulate the backend's replacement behavior if a track field
                // ever regresses into this metadata-only request.
                if (root.TryGetProperty("track_urns", out var tracks)) backingOrder = tracks.EnumerateArray().Select(item => long.Parse(item.GetString()!.Split(':').Last())).ToArray();
                return Json("{}", 204);
            }
            return Json(EditInfo(title: writes == 0 ? "Новый 🎵" : "Вечер", description: writes == 0 ? "Описание" : "Сохранено", sharing: writes == 0 ? "private" : "public",
                count: backingOrder.Length, ids: backingOrder.Take(available).ToArray()));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        // A stale UI preview is irrelevant to metadata: it must not replace the
        // fresh server order even when the visible tracks changed meanwhile.
        var updated = await transport.UpdateMobilePlaylistAsync(new(42, 17, "Вечер", "Сохранено", "public", ExpectedTrackIds: [999]));
        Assert.Equal(original, backingOrder); Assert.Equal(declared, updated.TrackCount);
        Assert.Equal(available, updated.Tracks.Length); Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleOrIncompleteReorderDoesNotSendPut(bool stale)
    {
        var writes = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/me" ? "{\"id\":42}" : EditInfo()));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.UpdateMobilePlaylistAsync(new(42, 17, "Вечер", "", "private",
            stale ? [456, 123] : [123], stale ? [123, 789] : [123, 456])));
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndividualRemovalWorksWithUnavailableTracksAndRequiresConfirmation(bool confirmed)
    {
        var deletes = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Task.FromResult(Json("{\"id\":42}"));
            if (request.Method == HttpMethod.Delete)
            {
                deletes++; Assert.Equal("/playlists/soundcloud:playlists:17/tracks/soundcloud:tracks:123", request.RequestUri.AbsolutePath);
                Assert.Null(request.Content); return Task.FromResult(Json("{}", 204));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(EditInfo(count: deletes > 0 ? 329 : 330, ids: deletes > 0 && confirmed ? [456] : [123, 456])));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        if (confirmed) await transport.RemoveMobilePlaylistTrackAsync(42, 17, 123);
        else await Assert.ThrowsAsync<SoundCloudException>(() => transport.RemoveMobilePlaylistTrackAsync(42, 17, 123));
        Assert.Equal(1, deletes);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(200)]
    public async Task PlaylistDeletionChecksOwnerAndServerDisappearance(int confirmationStatus)
    {
        var deletes = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Task.FromResult(Json("{\"id\":42}"));
            if (request.Method == HttpMethod.Delete)
            {
                deletes++; Assert.Equal("/playlists/soundcloud:playlists:17", request.RequestUri.AbsolutePath);
                return Task.FromResult(Json("{}", 204));
            }
            return Task.FromResult(Json(EditInfo(), deletes == 0 ? 200 : confirmationStatus));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        if (confirmationStatus == 200) await Assert.ThrowsAsync<SoundCloudException>(() => transport.DeleteMobilePlaylistAsync(42, 17));
        else await transport.DeleteMobilePlaylistAsync(42, 17);
        Assert.Equal(1, deletes);
    }

    [Fact]
    public async Task DeletionCanBeConfirmedByCompleteOwnerCollectionWhenInfoIsStale()
    {
        var deletes = 0; var collectionReads = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Task.FromResult(Json("{\"id\":42}"));
            if (request.Method == HttpMethod.Delete) { deletes++; return Task.FromResult(Json("{}", 204)); }
            if (request.RequestUri.AbsolutePath.StartsWith("/users/"))
            { collectionReads++; return Task.FromResult(Json("{\"collection\":[],\"_links\":{}}")); }
            return Task.FromResult(Json(EditInfo()));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await transport.DeleteMobilePlaylistAsync(42, 17);
        Assert.Equal(1, deletes); Assert.Equal(1, collectionReads);
    }

    [Fact]
    public async Task LostUpdateResponseIsNeverAutomaticallySentAgain()
    {
        var writes = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Put) { writes++; throw new HttpRequestException("Lost response"); }
            return Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/me" ? "{\"id\":42}" : EditInfo()));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.UpdateMobilePlaylistAsync(new(42, 17, "Вечер", "", "private")));
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ArtworkUsesNativeBase64JpegContractWithoutRewritingHiddenTracks()
    {
        byte[] jpeg = [0xff, 0xd8, 0xff, 0xe0, 0xff, 0xd9]; var writes = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Json("{\"id\":42}");
            if (request.Method == HttpMethod.Put)
            {
                writes++; Assert.Equal("/playlists/soundcloud:playlists:17/artwork", request.RequestUri.AbsolutePath);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Single(body.RootElement.EnumerateObject());
                Assert.Equal(jpeg, Convert.FromBase64String(body.RootElement.GetProperty("image_data").GetString()!));
                return Json("{}", 204);
            }
            return Json(EditInfo(count: 330, artwork: writes == 0 ? null : "https://i1.sndcdn.com/new-{size}.jpg"));
        }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        Assert.Equal("https://i1.sndcdn.com/new-t500x500.jpg", (await transport.SetMobilePlaylistArtworkAsync(42, 17, jpeg)).ArtworkUrl);
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData("/playlists/soundcloud:playlists:17", "PUT", true)]
    [InlineData("/playlists/soundcloud:playlists:17", "DELETE", true)]
    [InlineData("/playlists/soundcloud:playlists:17/artwork", "PUT", true)]
    [InlineData("/playlists/soundcloud:playlists:17/artwork", "POST", false)]
    [InlineData("/playlists/soundcloud:playlists:17/tracks/soundcloud:tracks:123", "DELETE", true)]
    [InlineData("/playlists/soundcloud:playlists:17/tracks/soundcloud:tracks:123", "PUT", false)]
    [InlineData("/playlists/soundcloud:playlists:17/info", "DELETE", false)]
    public void AndroidEditAllowlistIsMethodAndResourceSpecific(string path, string method, bool allowed) =>
        Assert.Equal(allowed, SoundCloudMobileRoutes.IsAllowed(SoundCloudMobileRoutes.Uri(path), new(method)));

    [Fact]
    public async Task ChangedAccountCannotUsePreviouslyVerifiedPlaylistOwnership()
    {
        var account = Account(); var writes = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            if (request.RequestUri!.AbsolutePath == "/me") return Task.FromResult(Json("{\"id\":42}"));
            account = account with { AccessToken = "another-account-token" };
            return Task.FromResult(Json(EditInfo()));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.DeleteMobilePlaylistAsync(42, 17));
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task PlaylistUpdateChallengeReplaysExactPutOnceWithoutDesktopFallback()
    {
        var account = Account(); var writes = 0; string? original = null;
        var browser = new Browser(); var challenge = new PlaylistEditChallenge();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Json("{\"id\":42}");
            if (request.Method == HttpMethod.Get) return Json(writes < 2 ? EditInfo() : EditInfo(title: "Вечер"));
            Assert.Equal(HttpMethod.Put, request.Method); writes++;
            var body = await request.Content!.ReadAsStringAsync(token);
            if (writes == 1)
            {
                original = body;
                var blocked = Json("{\"url\":\"https://geo.captcha-delivery.com/captcha/?cid=fixture\"}", 403);
                blocked.Headers.Add("X-DD-B", "1"); return blocked;
            }
            Assert.Equal(original, body);
            Assert.Equal("datadome=verified-edit-cookie", request.Headers.GetValues("Cookie").Single());
            return Json("{}", 204);
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, browser, mobileChallenge: challenge);
        Assert.Equal("Вечер", (await transport.UpdateMobilePlaylistAsync(new(42, 17, "Вечер", "Описание", "private"))).Title);
        Assert.Equal(2, writes); Assert.Equal(1, challenge.Calls); Assert.Equal(0, browser.Preparations); Assert.Equal(0, browser.Writes);
    }

    private sealed class PlaylistEditChallenge : ISoundCloudMobileChallengeHandler
    {
        internal int Calls;
        public Task<string?> SolveAsync(SoundCloudMobileChallenge challenge, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult<string?>("verified-edit-cookie"); }
    }
}
