using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class RepostTests
{
    private static SoundCloudWebClient Client(HttpClient http) => new(http) { Session = new("test", "fixture", "Test") };

    [Fact]
    public async Task RepostedIdsReadAllPagesAndDeduplicateSafeIds()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/me/track_reposts/ids", request.RequestUri!.AbsolutePath);
            return requests == 1
                ? """{"collection":[42,"90",0,-1,"invalid",9007199254740992],"next_href":"https://api-v2.soundcloud.com/me/track_reposts/ids?cursor=next"}"""
                : "[90,123]";
        }));
        Assert.Equal(new long[] { 42, 90, 123 }, (await Client(http).GetRepostedIdsAsync()).Order());
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("https://evil.test/me/track_reposts/ids")]
    [InlineData("https://api-v2.soundcloud.com/me/track_reposts/42")]
    [InlineData("https://api-v2.soundcloud.com/me/track_likes/ids")]
    public async Task PaginationCannotBecomeAMutationOrChangeIdentity(string next)
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(_ => { requests++; return JsonSerializer.Serialize(new { collection = new[] {42}, next_href = next }); }));
        await Assert.ThrowsAsync<SoundCloudException>(() => Client(http).GetRepostedIdsAsync());
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task MutationUsesDedicatedBrowserCommandAndNeverFallsBackToHttp()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("HTTP fallback")));
        var browser = new Transport(); var api = Client(http); api.BrowserTransport = browser;
        await api.SetRepostedAsync(42, true); await api.SetRepostedAsync(42, false);
        Assert.Equal(new[] { (42L, true), (42L, false) }, browser.Writes);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.SetRepostedAsync(0, true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.SetRepostedAsync(9007199254740992, true));
        api.Session = null;
        await Assert.ThrowsAsync<SoundCloudException>(() => api.SetRepostedAsync(42, true));
        await Assert.ThrowsAsync<SoundCloudException>(() => api.GetRepostedIdsAsync());
    }

    [Fact]
    public async Task StandaloneWritesUsePutAndDeleteWithAuthentication()
    {
        var methods = new List<HttpMethod>();
        using var http = new HttpClient(new Handler(request => {
            Assert.Equal("/me/track_reposts/42", request.RequestUri!.AbsolutePath);
            Assert.Equal("OAuth", request.Headers.Authorization?.Scheme);
            methods.Add(request.Method); return "{}";
        }));
        var api = Client(http);
        await api.SetRepostedAsync(42, true); await api.SetRepostedAsync(42, false);
        Assert.Equal(new[] { HttpMethod.Put, HttpMethod.Delete }, methods);
    }

    [Fact]
    public void ProtocolSeparatesRepostWriteFromReadAndArbitraryUrls()
    {
        var command = new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "repost", TrackId: 42, Reposted: true);
        Assert.True(command.IsValid());
        Assert.Contains("\"reposted\":true", JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand));
        Assert.False((command with { UserId = 7 }).IsValid());
        Assert.False((command with { TrackId = long.MaxValue }).IsValid());
        Assert.False((command with { Url = "https://api-v2.soundcloud.com/me/track_reposts/42" }).IsValid());
        Assert.True(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/me/track_reposts/ids"));
        Assert.False(BrowserRequestCommand.IsReadUrl("https://api-v2.soundcloud.com/me/track_reposts/42"));
    }

    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
    }
    private sealed class Transport : ISoundCloudBrowserTransport
    {
        public List<(long, bool)> Writes { get; } = [];
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected read");
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected like");
        public Task SetRepostedAsync(long trackId, bool reposted, CancellationToken cancellationToken = default) { Writes.Add((trackId, reposted)); return Task.CompletedTask; }
    }
}
