using System.Net;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class PlaylistMembershipTests
{
    [Fact]
    public async Task MembershipUsesAllPagesAndCompletesOnlyAmbiguousTrackPreviews()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var uri = request.RequestUri!; requests.Add(uri.PathAndQuery);
            if (uri.AbsolutePath == "/playlists/2")
                return """{"id":2,"title":"Большой","user":{"id":42},"track_count":3,"tracks":[{"id":8},{"id":9},{"id":90}]}""";
            Assert.Equal("/users/42/playlists", uri.AbsolutePath);
            Assert.Contains("show_tracks=true", uri.Query); Assert.DoesNotContain("show_tracks=false", uri.Query);
            if (uri.Query.Contains("cursor=next"))
                return """{"collection":[{"kind":"playlist","id":3,"title":"Other","user":{"id":42},"track_count":2,"tracks":[{"id":7},{"id":6}]}]}""";
            return """
                {"collection":[
                  {"kind":"playlist","id":1,"title":"Known","user":{"id":42},"track_count":200,"tracks":[{"id":90}]},
                  {"kind":"playlist","id":2,"title":"Partial","user":{"id":42},"track_count":3,"tracks":[{"id":8}]},
                  {"kind":"playlist","id":4,"title":"Empty","user":{"id":42},"track_count":0,"tracks":[]}],
                 "next_href":"https://api-v2.soundcloud.com/users/42/playlists?cursor=next&show_tracks=false"}
                """;
        }));
        var client = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test") };
        var choices = await client.GetPlaylistChoicesForTrackAsync(42, 90);
        Assert.Equal(new long[] { 1, 2 }, choices.Where(item => item.Playlist!.Tracks.Any(track => track.Id == 90)).Select(item => item.Playlist!.Id));
        Assert.Equal(3, requests.Count); Assert.Single(requests, path => path.StartsWith("/playlists/"));
    }
    [Fact]
    public async Task IncompleteMembershipCannotEnableAnAlreadyPresentTrack()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.StartsWith("/users/")
            ? """{"collection":[{"kind":"playlist","id":2,"title":"Partial","user":{"id":42},"track_count":100,"tracks":[]}]}"""
            : """{"id":2,"title":"Still partial","user":{"id":42},"track_count":100,"tracks":[{"id":8}]}"""));
        var client = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test") };
        await Assert.ThrowsAsync<SoundCloudException>(() => client.GetPlaylistChoicesForTrackAsync(42, 90));
    }
    [Fact]
    public async Task CanceledMembershipDoesNotReturnPartialChoices()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Canceled request reached HTTP")));
        var client = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test") };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPlaylistChoicesForTrackAsync(42, 90, stop.Token));
    }
    private sealed class Handler(Func<HttpRequestMessage, string> read) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(read(request)) });
    }
}
