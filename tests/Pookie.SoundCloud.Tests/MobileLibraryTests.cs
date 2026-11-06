using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed partial class MobilePlaylistTests
{
    private static string MobileTrackEntry(long id) => JsonSerializer.Serialize(new
    {
        track = new { urn = $"soundcloud:tracks:{id}", title = "Track", artwork_url_template = "https://i1.sndcdn.com/art-{size}.jpg",
            _embedded = new { user = new { urn = "soundcloud:users:99", username = "Artist", badges = new[] { "VERIFIED" } },
                stats = new { playback_count = 65400, likes_count = 321, comments_count = 14, reposts_count = 7 } } }
    });
    private static string MobilePage(string entries, string? next = null) =>
        "{\"collection\":[" + entries + "],\"_links\":{\"next\":{\"href\":" + JsonSerializer.Serialize(next) + "}}}";

    [Fact]
    public async Task MobileLikesAndCursorUseAndroidSchemaWithoutWebCollectionRequests()
    {
        var calls = 0; var account = Account();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            if (request.RequestUri!.AbsolutePath == "/me") return Task.FromResult(Json("{\"id\":42}"));
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri.Host);
            Assert.Equal("/users/soundcloud:users:42/likes", request.RequestUri.AbsolutePath);
            return Task.FromResult(Json(request.RequestUri.Query.Contains("cursor=next") ? MobilePage(MobileTrackEntry(789)) :
                MobilePage(MobileTrackEntry(123) + ",{\"playlist\":" + Metadata() + "}", "https://api-mobile.soundcloud.com/users/soundcloud:users:42/likes?cursor=next")));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var first = await client.GetLikesAsync(42);
        var track = Assert.Single(first.Tracks); Assert.Equal(123, track.Id); Assert.True(track.User!.IsVerified);
        Assert.Equal("https://i1.sndcdn.com/art-t500x500.jpg", track.ArtworkUrl);
        Assert.Equal(65400, track.PlaybackCount); Assert.Equal(321, track.LikesCount);
        Assert.Equal(14, track.CommentCount); Assert.Equal(7, track.RepostsCount);
        var second = await client.GetNextPageAsync(first.NextHref!);
        Assert.Equal(789, Assert.Single(second.Tracks).Id); Assert.Null(second.NextHref); Assert.Equal(3, calls);
        Assert.Equal(65400, second.Tracks[0].PlaybackCount); Assert.Equal(14, second.Tracks[0].CommentCount);
    }

    [Theory]
    [InlineData("{\"playback_count\":0,\"likes_count\":0,\"comments_count\":0,\"reposts_count\":0}", 0L)]
    [InlineData("{\"playback_count\":null,\"likes_count\":null,\"comments_count\":null,\"reposts_count\":null}", null)]
    [InlineData("{}", null)]
    [InlineData("null", null)]
    public async Task MobileCountersPreserveZeroAndUnknownValues(string stats, long? expected)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(MobilePage(
            "{\"track\":{\"urn\":\"soundcloud:tracks:123\",\"title\":\"Track\",\"_embedded\":{\"stats\":" + stats + "}}}")))));
        using var transport = new SoundCloudHttpTransport(http, Account);
        var track = Assert.Single((await transport.GetMobileLibraryPageAsync(42, "likes")).Items).Track!;
        Assert.Equal(expected, track.PlaybackCount); Assert.Equal(expected, track.LikesCount);
        Assert.Equal(expected, track.CommentCount); Assert.Equal(expected, track.RepostsCount);
    }

    [Fact]
    public async Task MobilePlaylistTracksRetainEmbeddedCounters()
    {
        var account = Account();
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(Info(tracks:
            "[{\"urn\":\"soundcloud:tracks:123\",\"title\":\"Track\",\"_embedded\":{\"stats\":{\"playback_count\":65400,\"likes_count\":321,\"comments_count\":14,\"reposts_count\":7}}}]")))));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var detail = await client.GetCollectionDetailAsync(LibraryData.FromPlaylist(new() { Id = 17 }));
        var track = Assert.Single(detail.Tracks.Tracks);
        Assert.Equal(65400, track.PlaybackCount); Assert.Equal(321, track.LikesCount);
        Assert.Equal(14, track.CommentCount); Assert.Equal(7, track.RepostsCount);
    }

    [Fact]
    public async Task LibraryCombinesOwnedLikedRepostedPlaylistsAndAlbumsAndDeduplicates()
    {
        var account = Account(); var paths = new List<string>();
        var metadata = Metadata().Replace("\"title\":", "\"artwork_url_template\":\"https://i1.sndcdn.com/playlist-{size}.jpg\",\"title\":");
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("api-mobile.soundcloud.com", request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath; paths.Add(path);
            var entries = path.EndsWith("playlists/posted") ? "{\"playlist\":" + metadata + "}" :
                path.EndsWith("albums/posted") ? "{\"playlist\":" + metadata.Replace("soundcloud:playlists:17", "soundcloud:playlists:20").Replace("\"title\":", "\"is_album\":true,\"title\":") + "}" :
                path.EndsWith("reposts") ? "{\"playlist\":" + metadata.Replace("soundcloud:playlists:17", "soundcloud:playlists:19") + "}" :
                "{\"playlist\":" + metadata + "},{\"playlist\":" + metadata.Replace("soundcloud:playlists:17", "soundcloud:playlists:18") + "}," + MobileTrackEntry(123);
            return Task.FromResult(Json(MobilePage(entries)));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var library = await client.GetLibraryAsync("collections", 42);
        Assert.Equal(new long[] { 17, 20, 18, 19 }, library.Items.Select(item => item.Playlist!.Id));
        Assert.True(library.Items[1].IsAlbum); Assert.Null(library.NextHref); Assert.Equal(4, paths.Count);
        Assert.Equal(4, (await transport.GetMobileCollectionsAsync(42)).Length); Assert.Equal(4, paths.Count);
    }

    [Fact]
    public async Task MobileActionReadsShareSnapshotAndInvalidateAfterConfirmedLike()
    {
        var likesReads = 0; var changed = false; var account = Account();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/me") return Task.FromResult(Json("{\"id\":42}"));
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri.Host);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/likes/tracks/create", path); changed = true;
                return Task.FromResult(Json("{\"collection\":[{\"target_urn\":\"soundcloud:tracks:789\"}]}"));
            }
            likesReads++;
            return Task.FromResult(Json(MobilePage(MobileTrackEntry(changed ? 789 : 123) + ",{\"playlist\":" + Metadata() + "}")));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        Assert.Equal(new long[] { 123 }, await client.GetLikedIdsAsync());
        Assert.Equal(new long[] { 17 }, await client.GetPlaylistActionIdsAsync(false)); Assert.Equal(1, likesReads);
        await client.SetLikedAsync(42, 789, true);
        Assert.Equal(new long[] { 789 }, await client.GetLikedIdsAsync()); Assert.Equal(2, likesReads);
    }

    [Fact]
    public async Task MobileSubscriptionsAndRepostsUseProfileCollections()
    {
        var account = Account();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Task.FromResult(Json("{\"id\":42}"));
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri.Host);
            return Task.FromResult(Json(request.RequestUri.AbsolutePath == "/followings/soundcloud:users:42/users"
                ? MobilePage("{\"urn\":\"soundcloud:users:99\",\"username\":\"Artist\",\"avatar_url_template\":\"https://i1.sndcdn.com/avatar-{size}.jpg\"}")
                : MobilePage(MobileTrackEntry(123) + ",{\"playlist\":" + Metadata() + "}")));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var client = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        Assert.Equal(new long[] { 99 }, await client.GetFollowingIdsAsync(42));
        Assert.Equal(new long[] { 123 }, await client.GetRepostedIdsAsync());
        Assert.Equal(new long[] { 17 }, await client.GetPlaylistActionIdsAsync(true));
        var following = await client.GetLibraryAsync("following", 42);
        Assert.Equal("https://i1.sndcdn.com/avatar-t500x500.jpg", Assert.Single(following.Items).ArtworkUrl);
    }

    [Theory]
    [InlineData("https://evil.test/steal")]
    [InlineData("https://api-mobile.soundcloud.com/users/soundcloud:users:99/likes")]
    [InlineData("https://api-mobile.soundcloud.com/users/soundcloud:users:42/reposts")]
    public async Task MobileLibraryRejectsCursorBeforeAnotherRequest(string next)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Json(MobilePage(MobileTrackEntry(123), next))); }));
        using var transport = new SoundCloudHttpTransport(http, Account);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.GetMobileLibraryPageAsync(42, "likes"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ConcurrentWritePreventsOldLibraryReadFromBecomingConfirmedSnapshot()
    {
        var account = Account(); var reads = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/me") return Json("{\"id\":42}");
            if (request.Method == HttpMethod.Post) return Json("{\"collection\":[{\"target_urn\":\"soundcloud:tracks:789\"}]}");
            if (++reads == 1) { entered.SetResult(); await release.Task.WaitAsync(token); return Json(MobilePage(MobileTrackEntry(123))); }
            return Json(MobilePage(MobileTrackEntry(789)));
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var pending = transport.GetMobileActionIdsAsync("tracks"); await entered.Task;
        await transport.SetLikedAsync(42, 789, true); release.SetResult();
        Assert.Equal(new long[] { 789 }, await pending); Assert.Equal(2, reads);
        Assert.Equal(new long[] { 789 }, await transport.GetMobileActionIdsAsync("tracks")); Assert.Equal(2, reads);
    }
}
