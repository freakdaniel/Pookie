using System.Net;
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
                "/tracks/42/comments" when request.RequestUri.Query.Contains("cursor=") => """[{"id":2,"body":"Next","timestamp":null}]""",
                "/tracks/42/comments" => """{"collection":[{"id":1,"body":"Музыка 🎵","timestamp":12500,"user":{"id":7,"username":"Listener"}},null,{"id":0,"body":"invalid"}],"next_href":"https://api-v2.soundcloud.com/tracks/42/comments?cursor=next"}""",
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
        Assert.Null(Assert.Single((await api.GetTrackCommentsNextAsync(42, comments.NextHref!)).Comments).Timestamp);
        Assert.Equal(90, Assert.Single((await api.GetRelatedTracksAsync(42)).Tracks).Id);
        Assert.Equal(4, paths.Count);
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
    [InlineData("/tracks/0/comments", false)]
    [InlineData("/tracks/42/comments/delete", false)]
    [InlineData("/tracks/42/reposts", false)]
    public void BrowserAllowsOnlyDetailReadPaths(string path, bool allowed) =>
        Assert.Equal(allowed, BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com" + path));

    private sealed class Handler(Func<HttpRequestMessage, string> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request)) });
    }
}
