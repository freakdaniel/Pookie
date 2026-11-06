using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class PlaylistEditTests
{
    private static WebSession Session() => new("webid", "fixture-token", "Pookie-Test/1");
    private static HttpResponseMessage Json(string value = "{}") => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    [Fact]
    public async Task WebUpdatePreservesDuplicateAndUnavailableIdsInOriginalOrder()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++;
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/playlists/11", request.RequestUri!.AbsolutePath);
                Assert.Contains("representation=full", request.RequestUri.Query);
                return Json("""{"id":11,"user":{"id":42},"track_count":3,"tracks":[{"id":8},{"id":7},{"id":8}]}""");
            }
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/playlists/11", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(new long[] { 8, 7, 8, 90 }, body.RootElement.GetProperty("playlist").GetProperty("tracks").EnumerateArray().Select(t => t.GetInt64()));
            return Json();
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await transport.AddToPlaylistAsync(42, 11, 90);
        Assert.Equal(2, calls);
    }
    [Theory]
    [InlineData("{\"id\":11,\"user\":{\"id\":8},\"track_count\":0,\"tracks\":[]}")]
    [InlineData("{\"id\":11,\"user\":{\"id\":42},\"is_album\":true,\"track_count\":0,\"tracks\":[]}")]
    [InlineData("{\"id\":11,\"user\":{\"id\":42},\"track_count\":2,\"tracks\":[{\"id\":8}]}")]
    [InlineData("{\"id\":11,\"user\":{\"id\":42},\"track_count\":1,\"tracks\":[{\"id\":0}]}")]
    [InlineData("{\"id\":11,\"user\":{\"id\":42},\"track_count\":null,\"tracks\":[]}")]
    public async Task UnconfirmedWebSnapshotCannotBeOverwritten(string playlist)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        { calls++; Assert.Equal(HttpMethod.Get, request.Method); return Task.FromResult(Json(playlist)); }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.AddToPlaylistAsync(42, 11, 90));
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task DuplicateDoesNotWriteAndLostPutIsNotReplayed()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Put) throw new HttpRequestException("Lost PUT response");
            return Task.FromResult(Json("""{"id":11,"user":{"id":42},"track_count":1,"tracks":[{"id":90}]}"""));
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await transport.AddToPlaylistAsync(42, 11, 90); Assert.Equal(1, calls);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.AddToPlaylistAsync(42, 11, 91));
        Assert.Equal(3, calls);
    }
    [Fact]
    public async Task ConcurrentEditsReadAgainAfterThePreviousWrite()
    {
        var ids = new List<long> { 8 }; var reads = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                reads++;
                return Json(JsonSerializer.Serialize(new { id = 11, user = new { id = 42 }, track_count = ids.Count, tracks = ids.Select(id => new { id }).ToArray() }));
            }
            if (!started.Task.IsCompleted) { started.SetResult(); await release.Task; }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            ids = body.RootElement.GetProperty("playlist").GetProperty("tracks").EnumerateArray().Select(t => t.GetInt64()).ToList();
            return Json();
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        var first = transport.AddToPlaylistAsync(42, 11, 90); await started.Task;
        var second = transport.AddToPlaylistAsync(42, 11, 91);
        Assert.Equal(1, reads);
        release.SetResult(); await Task.WhenAll(first, second);
        Assert.Equal(new long[] { 8, 90, 91 }, ids); Assert.Equal(2, reads);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
