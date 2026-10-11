using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class CommentTests
{
    [Fact]
    public async Task MutationsUseBrowserAndValidateConfirmationAndOwnership()
    {
        using var http = new HttpClient(new NoHttp());
        var browser = new CommentsBrowser();
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "token", "Test"), BrowserTransport = browser };
        var comment = await api.CreateTrackCommentAsync(42, "  Музыка 🎧  ", 12345);
        Assert.Equal("Музыка 🎧", browser.Last!.Body);
        Assert.Equal(12345, browser.Last.Timestamp);
        Assert.Equal(3, comment.Id); Assert.Equal(7, comment.User!.Id);
        await Assert.ThrowsAsync<SoundCloudException>(() => api.DeleteTrackCommentAsync(42, comment, 9));
        Assert.Equal(1, browser.Writes);
        await api.DeleteTrackCommentAsync(42, comment, 7);
        Assert.Equal("delete", browser.Last.Kind); Assert.Equal(3, browser.Last.CommentId);
        browser.Reply = """{"data":{"deleteTrackComment":false}}""";
        await Assert.ThrowsAsync<SoundCloudException>(() => api.DeleteTrackCommentAsync(42, comment, 7));
        browser.Reply = """{"errors":[{"message":"rejected"}],"data":null}""";
        await Assert.ThrowsAsync<SoundCloudException>(() => api.CreateTrackCommentAsync(42, "keep draft"));
        browser.Reply = """{"data":{"createTrackComment":{"comment":null}}}""";
        await Assert.ThrowsAsync<SoundCloudException>(() => api.CreateTrackCommentAsync(42, "keep draft"));
        api.BrowserTransport = null;
        await Assert.ThrowsAsync<SoundCloudException>(() => api.CreateTrackCommentAsync(42, "No HTTP fallback"));
    }

    [Fact]
    public async Task SortIsPassedToBrowserOnFirstAndNextPages()
    {
        var browser = new CommentsBrowser();
        using var http = new HttpClient(new NoHttp());
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "token", "Test"), BrowserTransport = browser };
        foreach (var sort in new[] { "NEWEST", "OLDEST", "TIMESTAMP" })
        {
            var first = await api.GetTrackCommentsAsync(42, sort: sort);
            Assert.Equal(sort, browser.Read!.Sort);
            await api.GetTrackCommentsNextAsync(42, first.NextHref!, sort: sort);
            Assert.Equal(sort, browser.Read.Sort); Assert.Equal("next", browser.Read.Cursor);
        }
        await Assert.ThrowsAsync<ArgumentException>(() => api.GetTrackCommentsAsync(42, sort: "unknown"));
    }

    [Fact]
    public void CommandsBoundTextAndRejectInvalidInputsAndMixedOperations()
    {
        var request = new CommentWriteRequest("create", 42, "Текст 🎧", 0);
        Assert.True(request.IsValid()); Assert.DoesNotContain("Текст", request.ToString());
        foreach (var invalid in new[] { request with { TrackId = 0 }, request with { Body = " " }, request with { Body = new('a', 1001) },
            request with { Body = "text\0" }, request with { Timestamp = double.NaN }, request with { Timestamp = -1 }, request with { CommentId = 7 },
            request with { Kind = "delete" }, new("delete", 42, Body: "text", CommentId: 7) }) Assert.False(invalid.IsValid());
        var command = new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "comment-write", Comment: request);
        Assert.True(command.IsValid()); Assert.False((command with { Url = "https://evil.test" }).IsValid());
        Assert.False((command with { TrackId = 9 }).IsValid()); Assert.False((command with { Operation = "me" }).IsValid());
        Assert.True(new CommentWriteRequest("delete", 42, CommentId: 7).IsValid());
        Assert.False(new TrackReadRequest("replies", 42, 7, Sort: "OLDEST").IsValid());
    }

    private sealed class NoHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Unexpected HTTP request");
    }
    private sealed class CommentsBrowser : ISoundCloudBrowserTransport
    {
        public CommentWriteRequest? Last; public TrackReadRequest? Read; public int Writes;
        public string? Reply;
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JsonDocument> WriteCommentAsync(CommentWriteRequest request, CancellationToken cancellationToken = default)
        {
            Last = request; Writes++;
            return Task.FromResult(JsonDocument.Parse(Reply ?? (request.Kind == "delete" ? """{"data":{"deleteTrackComment":true}}""" :
                """{"data":{"createTrackComment":{"comment":{"urn":"soundcloud:comments:3","body":"Музыка 🎧","trackTime":12345,"user":{"urn":"soundcloud:users:7","username":"Listener"}}}}}""")));
        }
        public Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default)
        {
            Read = request;
            return Task.FromResult(JsonDocument.Parse("""{"data":{"trackComments":{"comments":[],"pageInfo":{"endCursor":"next"}}}}"""));
        }
    }
}
