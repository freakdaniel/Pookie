using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class UserProfileTests
{
    private sealed class Handler(Func<Uri, string> read) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(read(request.RequestUri!)) });
    }
    private static SoundCloudWebClient Client(HttpClient http) => new(http) { Session = new("fixture", "fixture-token", "Test") };
    [Fact]
    public async Task HydratesProfileAndFiltersWebLinks()
    {
        using var http = new HttpClient(new Handler(uri => uri.AbsolutePath.EndsWith("web-profiles") ? """
            [{"url":"https://example.com","title":"Website","service":"personal"},{"url":"javascript:alert(1)"},{"url":"https://example.com"}]
            """ : """
            {"id":7,"username":"Artist","full_name":"Full name","description":"Bio 🎧","city":"Berlin","country_code":"DE","verified":true,
             "followers_count":21,"followings_count":4,"track_count":12,"likes_count":3,"playlist_count":2,
             "visuals":{"visuals":[{"visual_url":"https://i1.sndcdn.com/banner.jpg"}]}}
            """));
        var client = Client(http); var user = await client.GetUserAsync(7);
        Assert.Equal("Full name", user.FullName); Assert.True(user.Verified); Assert.Equal(4, user.FollowingsCount);
        Assert.Equal("https://i1.sndcdn.com/banner.jpg", user.BannerUrl);
        var link = Assert.Single(await client.GetUserLinksAsync(7)); Assert.Equal("https://example.com", link.Url);
    }
    [Theory]
    [InlineData("{}", false, false)]
    [InlineData("{\"verified\":true}", true, false)]
    [InlineData("{\"badges\":{\"verified\":true}}", true, false)]
    [InlineData("{\"badges\":{\"pro_unlimited\":true}}", false, true)]
    [InlineData("{\"badges\":{\"pro\":true,\"creator_mid_tier\":true}}", false, false)]
    [InlineData("{\"creator_subscription\":{\"product\":{\"id\":\"creator-pro-unlimited\"}}}", false, true)]
    [InlineData("{\"creator_subscriptions\":[null,{\"product\":{\"id\":\"creator-pro-unlimited\"}}]}", false, true)]
    [InlineData("{\"creator_subscription\":{\"product\":{\"id\":\"go-plus\"}},\"creator_subscriptions\":[{\"product\":null}]}", false, false)]
    [InlineData("{\"verified\":true,\"badges\":{\"pro_unlimited\":true,\"verified\":true},\"creator_subscription\":{\"product\":{\"id\":\"creator-pro-unlimited\"}}}", true, true)]
    public void VerificationAndArtistProRemainIndependent(string fields, bool verified, bool artistPro)
    {
        var user = JsonSerializer.Deserialize(fields, SoundCloudJson.Default.SoundCloudUser)!;
        Assert.Equal(verified, user.IsVerified);
        Assert.Equal(artistPro, user.HasArtistPro);
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(user, SoundCloudJson.Default.SoundCloudUser), SoundCloudJson.Default.SoundCloudUser)!;
        Assert.Equal(verified, restored.IsVerified);
        Assert.Equal(artistPro, restored.HasArtistPro);
    }

    [Theory]
    [InlineData("activity", "/stream/users/7")]
    [InlineData("tracks", "/users/7/tracks")]
    [InlineData("albums", "/users/7/albums")]
    [InlineData("playlists", "/users/7/playlists_without_albums")]
    [InlineData("reposts", "/stream/users/7/reposts")]
    [InlineData("likes", "/users/7/likes")]
    [InlineData("followers", "/users/7/followers")]
    [InlineData("following", "/users/7/followings")]
    public async Task SectionsUseScopedReadRoutes(string section, string path)
    {
        Assert.True(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com" + path));
        using var http = new HttpClient(new Handler(uri =>
        {
            Assert.Equal(path, uri.AbsolutePath);
            return section is "followers" or "following" ? """{"collection":[{"id":9,"username":"Listener"}]}""" :
                """{"collection":[{"track":{"id":1,"title":"One"}},{"track":{"id":1,"title":"Duplicate"}}]}""";
        }));
        Assert.Single((await Client(http).GetUserSectionAsync(7, section)).Items);
    }
    [Fact]
    public async Task ActivityKeepsServerOrderAndRepostDateThroughEnrichment()
    {
        using var http = new HttpClient(new Handler(uri => uri.AbsolutePath == "/playlists/2" ? """{ "id":2,"title":"Collection","artwork_url":"https://i1.sndcdn.com/cover.jpg","tracks":[] }""" : """
            {"collection":[
                {"type":"track-repost","created_at":"2026-10-22T15:00:00Z","origin":{"id":3,"title":"Older release","created_at":"2020-01-01T00:00:00Z"}},
                {"type":"playlist","created_at":"2026-10-20T00:00:00Z","origin":{"kind":"playlist","id":2,"title":"Collection","tracks":[]}}
            ],"next_href":"https://api-v2.soundcloud.com/stream/users/7?cursor=next"}
            """));
        var result = await Client(http).GetUserSectionAsync(7, "activity");
        Assert.Equal(new[] { "track:3", "playlist:2" }, result.Items.Select(item => item.Key));
        Assert.Equal("track-repost", result.Items[0].ActivityType);
        Assert.Equal("2026-10-22T15:00:00Z", result.Items[0].ActivityAt);
        Assert.Equal("2020-01-01T00:00:00Z", result.Items[0].Track!.CreatedAt);
        Assert.Equal("playlist", result.Items[1].ActivityType);
        Assert.Equal("https://i1.sndcdn.com/cover.jpg", result.Items[1].ArtworkUrl);
        Assert.Contains("cursor=next", result.NextHref);
        await Assert.ThrowsAsync<SoundCloudException>(() => Client(http).GetUserSectionAsync(7, "activity", "https://api-v2.soundcloud.com/stream/users/8?cursor=next"));
    }
    [Theory]
    [InlineData("https://api-v2.soundcloud.com/users/8/tracks")]
    [InlineData("https://api-v2.soundcloud.com/users/7/likes")]
    [InlineData("https://evil.test/users/7/tracks")]
    public async Task RejectsForeignPaginationBeforeTransport(string cursor)
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Request must be rejected before sending")));
        await Assert.ThrowsAsync<SoundCloudException>(() => Client(http).GetUserSectionAsync(7, "tracks", cursor));
    }
    [Fact]
    public async Task UserCommentsHydrateTracksKeepUnavailableCommentsAndScopeCursor()
    {
        using var http = new HttpClient(new Handler(uri => uri.AbsolutePath == "/tracks" ? """[{"id":10,"title":"A track"}]""" : """
            {"collection":[{"id":1,"body":"Comment 🎧","track_id":10},{"id":2,"body":"Unavailable","track_id":11},{"id":1,"body":"Duplicate","track_id":10},{"id":3,"body":""}],
             "next_href":"https://api-v2.soundcloud.com/users/7/comments?cursor=next"}
            """));
        var result = await Client(http).GetUserCommentsAsync(7);
        Assert.Equal(2, result.Comments.Length); Assert.Equal("A track", result.Comments[0].Track!.Title);
        Assert.Equal("Unavailable", result.Comments[1].Comment.Body); Assert.Equal(11, result.Comments[1].Track!.Id);
        Assert.True(BrowserRequestCommand.IsReadUrl(result.NextHref));
        await Assert.ThrowsAsync<SoundCloudException>(() => Client(http).GetUserCommentsAsync(7, "https://api-v2.soundcloud.com/users/8/comments?cursor=next"));
        await Assert.ThrowsAsync<SoundCloudException>(() => Client(http).GetUserCommentsAsync(7, "https://api-v2.soundcloud.com/users/7/tracks?cursor=next"));
    }
    [Fact]
    public async Task SupportsProfileAndLinksButRejectsArbitraryUserEndpoints()
    {
        Assert.True(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/users/7"));
        Assert.True(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/users/soundcloud:users:7/web-profiles"));
        Assert.False(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/users/7/emails"));
        using var http = new HttpClient(new Handler(_ => """{"id":8,"username":"Wrong user"}"""));
        await Assert.ThrowsAsync<SoundCloudException>(() => Client(http).GetUserAsync(7));
    }
}
