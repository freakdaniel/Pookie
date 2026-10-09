using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class TrackDetailTests
{
    [Fact]
    public async Task DetailReadsMetadataTimedCommentsAndRelatedTracks()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; paths.Add(path);
            Assert.Equal("OAuth", request.Headers.Authorization?.Scheme);
            return path switch
            {
                "/tracks/42" => """{"id":42,"title":"Track","description":"Описание\nВторая строка","duration":119000,"user":{"id":7,"username":"Artist","track_count":69}}""",
                "/graphql" => GraphResponse(request),
                "/tracks/42/related" => """{"collection":[{"id":90,"title":"Related"}],"next_href":null}""",
                _ => throw new InvalidOperationException("Unexpected path: " + path)
            };
        }));
        var api = new SoundCloudWebClient(http) { Session = new("test", "token", "Test") };
        var track = await api.GetTrackAsync(42);
        Assert.Equal("Описание\nВторая строка", track.Description);
        Assert.Equal(69, track.User!.TrackCount);
        var comments = await api.GetTrackCommentsAsync(42);
        var comment = Assert.Single(comments.Comments);
        Assert.Equal(12500, comment.Timestamp); // milliseconds, not seconds
        Assert.Equal("Listener", comment.User!.Username);
        Assert.Equal("Reply", Assert.Single(comment.Replies).Body);
        Assert.Null(Assert.Single((await api.GetTrackCommentsNextAsync(42, comments.NextHref!)).Comments).Timestamp);
        Assert.Equal(90, Assert.Single((await api.GetRelatedTracksAsync(42)).Tracks).Id);
        Assert.Equal(4, paths.Count);
    }

    private static string GraphResponse(HttpRequestMessage request)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("graph.soundcloud.com", request.RequestUri!.Host);
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.Contains("PookieTrackComments", body); Assert.DoesNotContain("mutation", body);
        return body.Contains("\"after\"")
            ? """{"data":{"trackComments":{"comments":[{"urn":"soundcloud:comments:2","body":"Next","trackTime":null}],"pageInfo":{"endCursor":null}}}}"""
            : """{"data":{"trackComments":{"comments":[{"urn":"soundcloud:comments:1","body":"Музыка 🎵","trackTime":12500,"user":{"urn":"soundcloud:users:7","username":"Listener"},"replies":{"total":1,"comments":[{"urn":"soundcloud:comments:10","body":"Reply","trackTime":12500}],"pageInfo":{"hasNextPage":false,"endCursor":null}}},null,{"urn":"invalid","body":"invalid"}],"pageInfo":{"endCursor":"next"}}}}""";
    }

    [Fact]
    public async Task SidebarParsesRealUsersAndCollectionsAndRespectsArtistPrivacy()
    {
        var hidden = false;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Contains("limit=4", request.RequestUri!.Query);
                return """{"collection":[{"id":10,"kind":"playlist","title":"Playlist","artwork_url":"https://i1.sndcdn.com/artwork.jpg"}],"next_href":null}""";
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("PookieTrackSidebar", request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return $$$"""
                {"data":{"topFans":{"hasArtistOptedOut":{{{(hidden ? "true" : "false")}}},"allTime":{"fans":[{"fan":{"urn":"soundcloud:users:7","username":"Fan","avatarUrl":"https://i1.sndcdn.com/avatar.jpg"},"totalPlays":210}]}},
                "playlists":[{"urn":"soundcloud:playlists:10","title":"Playlist","artworkUrl":"https://i1.sndcdn.com/artwork.jpg","user":{"urn":"soundcloud:users:7","username":"Artist"}}],
                "albums":[{"urn":"soundcloud:playlists:11","title":"Album","user":{"urn":"soundcloud:users:7","username":"Artist"}}]}}
                """;
        }));
        var api = new SoundCloudWebClient(http) { Session = new("test", "token", "Test") };
        var sidebar = await api.GetTrackSidebarAsync(42);
        var fan = Assert.Single(sidebar.Fans);
        Assert.Equal(210, fan.Plays); Assert.Equal(7, fan.User.Id);
        Assert.Equal("https://i1.sndcdn.com/artwork.jpg", Assert.Single(sidebar.Playlists).ArtworkUrl);
        Assert.True(Assert.Single(sidebar.Albums).IsAlbum);
        hidden = true;
        sidebar = await api.GetTrackSidebarAsync(42);
        Assert.True(sidebar.FansHidden); Assert.Empty(sidebar.Fans);
    }

    [Fact]
    public async Task GraphCursorCannotSwitchTrackOrCarryMalformedOpaqueData()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Unsafe request")));
        var api = new SoundCloudWebClient(http);
        await Assert.ThrowsAsync<SoundCloudException>(() => api.GetTrackCommentsNextAsync(42, "sc-comments:43:bmV4dA=="));
        await Assert.ThrowsAsync<SoundCloudException>(() => api.GetTrackCommentsNextAsync(42, "sc-comments:42:not base64"));
        await Assert.ThrowsAsync<SoundCloudException>(() => api.GetTrackCommentsNextAsync(42, "sc-comments:42:"));
    }

    [Theory]
    [InlineData("comments", 42, 0, null, true)]
    [InlineData("sidebar", 42, 0, null, true)]
    [InlineData("replies", 42, 12, "next", true)]
    [InlineData("mutation", 42, 0, null, false)]
    [InlineData("comments", 0, 0, null, false)]
    [InlineData("comments", 42, 0, "bad\n", false)]
    [InlineData("sidebar", 42, 0, "next", false)]
    [InlineData("replies", 42, 0, null, false)]
    public void BrowserAcceptsOnlyBoundedTrackReadOperations(string kind, long id, long comment, string? cursor, bool allowed)
    {
        var request = new TrackReadRequest(kind, id, comment, cursor);
        Assert.Equal(allowed, new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "track-read", Detail: request).IsValid());
        Assert.False(new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "track-read", Url: "https://evil.test", Detail: request).IsValid());
    }

    [Theory]
    [InlineData("https://evil.test/tracks/42/comments")]
    [InlineData("https://api-v2.soundcloud.com/tracks/43/comments")]
    [InlineData("https://api-v2.soundcloud.com/tracks/42/related")]
    [InlineData("https://api-v2.soundcloud.com/tracks/42/comments/replies")]
    [InlineData("https://api-v2.soundcloud.com/tracks/42/comments#secret")]
    public async Task CommentsCursorCannotChangeTrackOrEndpoint(string cursor)
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Unsafe request")));
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http).GetTrackCommentsNextAsync(42, cursor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9007199254740992)]
    public async Task DetailsRejectInvalidIds(long id)
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Invalid ID request")));
        var api = new SoundCloudWebClient(http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetRelatedTracksAsync(id));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetTrackCommentsAsync(id));
    }

    [Theory]
    [InlineData("/tracks/42/comments", true)]
    [InlineData("/tracks/42/related", true)]
    [InlineData("/tracks/42/reposters", true)]
    [InlineData("/tracks/42/albums", true)]
    [InlineData("/tracks/42/playlists_without_albums", true)]
    [InlineData("/tracks/0/comments", false)]
    [InlineData("/tracks/42/comments/delete", false)]
    [InlineData("/tracks/42/reposts", false)]
    public void BrowserAllowsOnlyDetailReadPaths(string path, bool allowed) =>
        Assert.Equal(allowed, BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com" + path));

    [Fact]
    public async Task SubsectionsKeepTheirDataTypesAndValidatePaginationIdentity()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/tracks/42/reposters" => """{"collection":[{"id":7,"username":"Reposter","kind":"user"}],"next_href":null}""",
            "/tracks/42/albums" => """{"collection":[{"id":10,"title":"Album","artwork_url":"https://i1.sndcdn.com/album.jpg","kind":"playlist","is_album":true}],"next_href":null}""",
            "/tracks/42/playlists_without_albums" => """{"collection":[{"id":11,"title":"Playlist","artwork_url":"https://i1.sndcdn.com/playlist.jpg","kind":"playlist"}],"next_href":"https://api-v2.soundcloud.com/tracks/42/playlists_without_albums?cursor=next"}""",
            "/tracks/42/related" => """{"collection":[{"id":90,"title":"Related","kind":"track"}],"next_href":null}""",
            _ => throw new InvalidOperationException("Unexpected section")
        }));
        var api = new SoundCloudWebClient(http) { Session = new("test", "token", "Test") };
        Assert.Equal("Reposter", Assert.Single((await api.GetTrackSectionAsync(42, "reposts")).Items).User?.Username);
        Assert.True(Assert.Single((await api.GetTrackSectionAsync(42, "albums")).Items).IsAlbum);
        var playlists = await api.GetTrackSectionAsync(42, "playlists");
        Assert.False(Assert.Single(playlists.Items).IsAlbum);
        Assert.NotNull((await api.GetTrackSectionAsync(42, "playlists", playlists.NextHref)).NextHref);
        Assert.Equal(90, Assert.Single((await api.GetTrackSectionAsync(42, "related")).Items).Track?.Id);
        foreach (var cursor in new[] { "https://evil.test/tracks/42/reposters", "https://api-v2.soundcloud.com/tracks/43/reposters", "https://api-v2.soundcloud.com/tracks/42/albums" })
            await Assert.ThrowsAsync<SoundCloudException>(() => api.GetTrackSectionAsync(42, "reposts", cursor));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetTrackSectionAsync(42, "delete"));
    }

    [Fact]
    public async Task SidebarFillsFourPlaylistsAcrossFilteredPagesAndDeduplicates()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/graphql")
                return """{"data":{"playlists":[{"urn":"soundcloud:playlists:1","title":"Only featured"}]}}""";
            Assert.Equal("/tracks/42/playlists_without_albums", request.RequestUri.AbsolutePath);
            var next = request.RequestUri.Query.Contains("cursor=next");
            if (!next) Assert.Contains("limit=4", request.RequestUri.Query);
            return JsonSerializer.Serialize(new {
                collection = Enumerable.Range(next ? 3 : 1, 3).Select(id => new {
                    id, kind = "playlist", title = "Playlist " + id, artwork_url = "https://i1.sndcdn.com/playlist.jpg"
                }), next_href = "https://api-v2.soundcloud.com/tracks/42/playlists_without_albums?cursor=next"
            });
        }));
        var api = new SoundCloudWebClient(http) { Session = new("test", "token", "Test") };
        var sidebar = await api.GetTrackSidebarAsync(42);
        Assert.Equal(4, sidebar.Playlists.Length);
        Assert.Equal("Playlist 4", sidebar.Playlists[^1].Title);
        Assert.True(sidebar.HasMorePlaylists);
    }

    private sealed class Handler(Func<HttpRequestMessage, string> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request)) });
    }
}
