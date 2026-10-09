using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class CollectionActionsTests
{
    private static SoundCloudWebClient Client(HttpClient http) => new(http) { Session = new("fixture", "fixture-token", "Test") };

    [Fact]
    public async Task OwnPlaylistPickerIncludesPrivatePlaylistsButExcludesLikesAndAlbums()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Query.Contains("cursor=")
            ? """{"collection":[{"kind":"playlist","id":4,"title":"Private","sharing":"private","user":{"id":42}}]}"""
            : """{"collection":[{"kind":"playlist","id":1,"title":"Own","user":{"id":42}},{"kind":"playlist","id":2,"title":"Liked","user":{"id":7}},{"kind":"playlist","id":3,"title":"Album","is_album":true,"user":{"id":42}}],"next_href":"https://api-v2.soundcloud.com/users/42/playlists?cursor=next"}"""));
        var playlists = await Client(http).GetOwnedPlaylistsAsync(42);
        Assert.Equal(new long[] { 1, 4 }, playlists.Select(item => item.Playlist!.Id));
        Assert.Equal("private", playlists[1].Playlist!.Sharing);
    }

    [Theory]
    [InlineData("https://evil.test/users/42/playlists")]
    [InlineData("https://api-v2.soundcloud.com/users/7/playlists")]
    [InlineData("https://api-v2.soundcloud.com/users/42/tracks")]
    public async Task PlaylistPaginationCannotLeaveOwner(string next)
    {
        var reads = 0;
        using var http = new HttpClient(new Handler(_ => { reads++; return JsonSerializer.Serialize(new { collection = Array.Empty<object>(), next_href = next }); }));
        await Assert.ThrowsAsync<SoundCloudException>(() => Client(http).GetOwnedPlaylistsAsync(42));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task FollowingIdsReadAllPagesAndRejectForeignPagination()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Query.Contains("cursor=") ? "[7,8,0,9007199254740992]" :
            """{"collection":["7",-1,"bad"],"next_href":"https://api-v2.soundcloud.com/users/42/followings/ids?cursor=next"}"""));
        Assert.Equal(new long[] { 7, 8 }, (await Client(http).GetFollowingIdsAsync(42)).Order());
        using var otherHttp = new HttpClient(new Handler(_ => """{"collection":[],"next_href":"https://api-v2.soundcloud.com/users/7/followings/ids"}"""));
        await Assert.ThrowsAsync<SoundCloudException>(() => Client(otherHttp).GetFollowingIdsAsync(42));
    }

    [Fact]
    public async Task WritesUseDedicatedBrowserCommandsAndNeverHttpFallback()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Unexpected HTTP write")));
        var client = Client(http); var browser = new Transport(); client.BrowserTransport = browser;
        await client.SetFollowingAsync(42, 7, true); await client.SetFollowingAsync(42, 7, false);
        await client.AddToPlaylistAsync(42, 11, 90);
        Assert.Equal(new[] { (42L, 7L, true), (42L, 7L, false) }, browser.Follows);
        Assert.Equal((42L, 11L, 90L), browser.Added);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SetFollowingAsync(42, 42, true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.AddToPlaylistAsync(42, 0, 90));
        client.BrowserTransport = null;
        await Assert.ThrowsAsync<SoundCloudException>(() => client.SetFollowingAsync(42, 7, true));
        await Assert.ThrowsAsync<SoundCloudException>(() => client.AddToPlaylistAsync(42, 11, 90));
    }

    [Fact]
    public async Task CollectionDetailHydratesMetadataAndPreservesTrackOrder()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath == "/tracks"
            ? """[{"id":90,"title":"Resolved"}]"""
            : """{"id":11,"title":"Playlist title","description":"Description","duration":250000,"track_count":3,"sharing":"private","user":{"id":42},"tracks":[{"id":90},{"id":91,"title":"Second"},{"id":90}]}"""));
        var detail = await Client(http).GetCollectionDetailAsync(LibraryData.FromPlaylist(new() { Id = 11 }));
        Assert.Equal("Playlist title", detail.Item.Title);
        Assert.Equal("Description", detail.Item.Playlist!.Description);
        Assert.Equal(250000, detail.Item.Playlist.Duration);
        Assert.Equal(new long[] { 90, 91, 90 }, detail.Tracks.Tracks.Select(track => track.Id));
        Assert.Equal("Resolved", detail.Tracks.Tracks[0].Title);
    }

    [Fact]
    public void ProtocolAllowsOnlyBoundedIdsAndReadPaths()
    {
        var follow = new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "follow", UserId: 42, ArtistId: 7, Following: true);
        var add = new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "playlist-add", UserId: 42, PlaylistId: 11, TrackId: 90);
        Assert.True(follow.IsValid()); Assert.True(add.IsValid());
        Assert.False((follow with { ArtistId = 42 }).IsValid()); Assert.False((add with { PlaylistId = long.MaxValue }).IsValid());
        Assert.False((add with { Url = "https://evil.test" }).IsValid());
        Assert.True(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/users/42/followings/ids"));
        Assert.True(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/users/42/playlists"));
        Assert.False(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/me/followings/7"));
    }

    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
    }
    private sealed class Transport : ISoundCloudBrowserTransport
    {
        public List<(long, long, bool)> Follows { get; } = [];
        public (long, long, long) Added { get; private set; }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task SetFollowingAsync(long userId, long artistId, bool following, CancellationToken cancellationToken = default)
        { Follows.Add((userId, artistId, following)); return Task.CompletedTask; }
        public Task AddToPlaylistAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken = default)
        { Added = (userId, playlistId, trackId); return Task.CompletedTask; }
    }
}
