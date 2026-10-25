using System.Net;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class FeedTests
{
    private sealed class Handler(Func<Uri, string> read) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(read(request.RequestUri!)) });
    }
    [Fact] public async Task FeedKeepsActorsEventOrderAndSeparateRepostsOfTheSameTrack()
    {
        using var http = new HttpClient(new Handler(_ => """
            {"collection":[
              {"type":"track-repost","created_at":"2026-10-24T12:00:00Z","user":{"id":7,"username":"Reposter"},"origin":{"id":1,"title":"Song","user":{"id":8,"username":"Author"}}},
              {"type":"track","created_at":"2026-10-24T11:00:00Z","user":{"id":8,"username":"Author"},"origin":{"id":1,"title":"Song","user":{"id":8,"username":"Author"}}},
              {"type":"track-repost","created_at":"2026-10-24T12:00:00Z","user":{"id":7,"username":"Reposter"},"origin":{"id":1,"title":"Song"}},
              {"type":"playlist","user":{"id":8,"username":"Author"},"origin":{"kind":"playlist","id":2,"title":"Album","track_count":3,"artwork_url":"https://i1.sndcdn.com/album.jpg","tracks":[{"id":1,"title":"Song"}]}},
              {"type":"playlist","origin":{"kind":"playlist","id":3,"title":"Empty","track_count":0,"artwork_url":"https://i1.sndcdn.com/empty.jpg","tracks":[]}},
              {"type":"playlist","origin":{"kind":"playlist","id":4,"title":"Empty null tracks","track_count":0,"artwork_url":"https://i1.sndcdn.com/empty-null.jpg","tracks":null}}
            ],"next_href":"https://api-v2.soundcloud.com/stream?cursor=next"}
            """));
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test") };
        var page = await api.GetFeedActivitiesAsync();
        Assert.Equal(3, page.Entries.Length);
        Assert.True(page.Entries[0].IsRepost); Assert.Equal(7, page.Entries[0].Actor!.Id);
        Assert.Equal(8, page.Entries[0].Item.Track!.User!.Id);
        Assert.False(page.Entries[1].IsRepost); Assert.NotEqual(page.Entries[0].Key, page.Entries[1].Key);
        Assert.NotNull(page.Entries[2].Item.Playlist);
        Assert.Equal("https://api-v2.soundcloud.com/stream?cursor=next", page.NextHref);
        Assert.Equal(3, (await api.GetFeedActivitiesAsync(page.NextHref)).Entries.Length);
    }
    [Theory]
    [InlineData("https://evil.test/stream")]
    [InlineData("https://api-v2.soundcloud.com/stream/users/7")]
    [InlineData("https://api-v2.soundcloud.com/users/7/likes")]
    public async Task RejectsForeignCursorsBeforeTransport(string cursor)
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No request expected")));
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test") };
        await Assert.ThrowsAsync<SoundCloudException>(() => api.GetFeedActivitiesAsync(cursor));
    }
}
