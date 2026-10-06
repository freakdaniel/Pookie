using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class LibraryTests
{
    [Fact]
    public void MixedHistoryAndLibraryResourcesKeepTheirTypes()
    {
        using var json = JsonDocument.Parse("""
            {"collection":[
              {"track":{"id":1,"title":"Track","user":{"username":"Artist"}}},
              {"type":"playlist-like","playlist":{"id":2,"title":"Album","is_album":true,"user":{"username":"Artist"},"tracks":[]}},
              {"system_playlist":{"urn":"soundcloud:system-playlists:artist-stations:1:2","title":"Station","playlist_type":"ARTIST_STATION","tracks":[]}},
              {"context":{"user":{"id":3,"username":"User","followers_count":1234}}},
              {"track":{"id":1,"title":"Track"}},null],"next_href":"https://api-v2.soundcloud.com/me/library/all?cursor=next"}
            """);
        var page = LibraryData.Parse(json.RootElement);
        Assert.Equal(4, page.Items.Length);
        Assert.NotNull(page.Items[0].Track);
        Assert.True(page.Items[1].IsAlbum);
        Assert.False(page.Items[2].IsAlbum);
        Assert.Contains("1234", page.Items[3].Subtitle.Replace(",", "").Replace(" ", "").Replace(" ", ""));
        Assert.NotNull(page.NextHref);
    }

    [Fact]
    public void HistoryTrackWithoutArtworkRetainsItsAuthorAvatarAsFallback()
    {
        using var json = JsonDocument.Parse("""
            {"collection":[{"track":{"id":8,"title":"Track","artwork_url":null,
              "user":{"username":"Artist","avatar_url":"https://i1.sndcdn.com/avatar-large.jpg"}}}]}
            """);
        var item = Assert.Single(LibraryData.Parse(json.RootElement).Items);
        Assert.Equal("https://i1.sndcdn.com/avatar-large.jpg", item.ArtworkUrl);
        Assert.Null(item.Track!.ArtworkUrl);
    }

    [Theory]
    [InlineData("me/library/all")]
    [InlineData("me/library/stations")]
    [InlineData("me/play-history/contexts")]
    [InlineData("me/play-history/tracks")]
    [InlineData("users/42/followings")]
    [InlineData("users/42/tracks")]
    [InlineData("playlists/42")]
    [InlineData("tracks?ids=1,2")]
    [InlineData("system-playlists/soundcloud%3Asystem-playlists%3Aartist-stations%3A1%3A2")]
    public void LibraryReadsAreExplicitlyAllowed(string path) => Assert.True(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/" + path));

    [Theory]
    [InlineData("me/play-history")]
    [InlineData("me/followings/42")]
    [InlineData("users/42/followings/not_followed_by/1")]
    [InlineData("playlists/42/privacy")]
    [InlineData("system-playlists/invalid")]
    public void NewLibraryRoutesCannotEnableMutationEndpoints(string path) => Assert.False(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/" + path));

    [Fact]
    public async Task IdOnlyHistoryIsHydratedInOneBatchInOriginalOrder()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath == "/me/play-history/tracks"
                ? "{\"collection\":[{\"track_id\":2},{\"track_id\":1},{\"track_id\":2}]}"
                : "[{\"id\":1,\"title\":\"One\"},{\"id\":2,\"title\":\"Two\"}]") };
        }));
        var api = new SoundCloudWebClient(http) { Session = new("publicid", "web-token", "Pookie test") };
        var result = await api.GetLibraryAsync("history", 42);
        Assert.Equal(new long?[] { 2, 1 }, result.Items.Select(item => item.Track?.Id));
        Assert.Equal(new[] { "/me/play-history/tracks", "/tracks" }, calls);
    }

    [Fact]
    public void WebsiteSystemPlaylistIdsAndMinimalPlaylistsAreNotParsedAsTracks()
    {
        using var json = JsonDocument.Parse("""
            {"collection":[
            {"context_urn":"soundcloud:system-playlists:your-moods:42:1","kind":"system-playlist",
              "system_playlist":{"id":"soundcloud:system-playlists:your-moods:42:1","title":"Your Mix 1","short_title":"Mix 1","short_description":"Made for user"}},
            {"type":"playlist","playlist":{"id":2,"title":"Playlist","user":{"username":"Artist"}}},
            {"kind":"user","user":{"id":3,"username":"Artist"}}]}
            """);
        var result = LibraryData.Parse(json.RootElement);
        Assert.Equal(3, result.Items.Length);
        Assert.Equal("soundcloud:system-playlists:your-moods:42:1", result.Items[0].Playlist!.Urn);
        Assert.Equal("Mix 1", result.Items[0].Title);
        Assert.Equal("Made for user", result.Items[0].Subtitle);
        Assert.NotNull(result.Items[1].Playlist);
        Assert.Null(result.Items[1].Track);
        Assert.NotNull(result.Items[2].User);
    }

    [Fact]
    public async Task ShortPlaylistsLoadFullMetadataAndUseFirstTrackArtwork()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; calls.Add(path);
            var body = path switch {
                "/me/library/all" => """{"collection":[{"type":"playlist","playlist":{"id":2,"title":"Playlist","user":{"username":"Artist"}}}]}""",
                "/playlists/2" => """{"id":2,"title":"Playlist","is_album":true,"artwork_url":null,"tracks":[{"id":8}]}""",
                "/tracks/8" => """{"id":8,"title":"First track","artwork_url":"https://i1.sndcdn.com/artworks-2bperYuPGcEroQWA-B613Jg-large.png"}""",
                _ => throw new InvalidOperationException("Unexpected endpoint " + path) };
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }));
        var api = new SoundCloudWebClient(http) { Session = new("publicid", "web-token", "Pookie test") };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var item = Assert.Single((await api.GetLibraryAsync("collections", 42)).Items);
            Assert.True(item.IsAlbum);
            Assert.EndsWith("-large.png", item.ArtworkUrl);
            Assert.NotNull(item.Playlist);
        }
        Assert.Equal(new[] { "/me/library/all", "/playlists/2", "/tracks/8", "/me/library/all" }, calls);
    }

    [Fact]
    public async Task RecentResourcesPreserveServerOrderAndUseCalculatedArtwork()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path == "/me/play-history/contexts" ? """
                {"collection":[
                 {"kind":"system-playlist","context_urn":"soundcloud:system-playlists:your-moods:42:1","system_playlist":{"id":"soundcloud:system-playlists:your-moods:42:1","title":"Mix"}},
                 {"kind":"playlist","playlist":{"id":2,"title":"Album"}},
                 {"kind":"user","user":{"id":3,"username":"Artist","avatar_url":"https://i1.sndcdn.com/avatar-large.jpg"}}]}
                """ : path.StartsWith("/system-playlists/") ? """
                {"id":"soundcloud:system-playlists:your-moods:42:1","title":"Mix","calculated_artwork_url":"https://i1.sndcdn.com/mix-large.jpg","tracks":[]}
                """ : """{"id":2,"title":"Album","artwork_url":"https://i1.sndcdn.com/album-large.jpg","tracks":[]}""";
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }));
        var api = new SoundCloudWebClient(http) { Session = new("publicid", "web-token", "Pookie test") };
        var result = await api.GetLibraryAsync("recent", 42);
        Assert.Equal(new[] { "Mix", "Album", "Artist" }, result.Items.Select(item => item.Title));
        Assert.All(result.Items, item => Assert.NotNull(item.ArtworkUrl));
        Assert.Equal("https://i1.sndcdn.com/mix-large.jpg", result.Items[0].ArtworkUrl);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
