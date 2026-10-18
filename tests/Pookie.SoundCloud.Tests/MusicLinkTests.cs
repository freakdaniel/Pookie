using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class MusicLinkTests
{
    [Theory]
    [InlineData("https://soundcloud.com/artist/song?secret_token=s-fixture", "track")]
    [InlineData("https://soundcloud.com/artist/sets/list?si=share", "playlist")]
    public async Task MobileShareLinksExpandBeforeTheBrowserApiResolvesThem(string canonical, string kind)
    {
        var redirects = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            redirects++;
            Assert.Equal("on.soundcloud.com", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("x-datadome-clientid"));
            Assert.Equal(HttpMethod.Get, request.Method);
            return Redirect(canonical);
        }));
        var browser = new Browser(canonical, kind);
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test"), BrowserTransport = browser, RequireBrowserTransport = true };
        var item = await api.ResolveChatMusicAsync("https://on.soundcloud.com/InK8dhUq5aceNUcteq");
        Assert.Equal(1, redirects); Assert.Equal(1, browser.Reads);
        Assert.Equal(kind == "playlist", item.Playlist != null);
        Assert.Equal(kind == "track", item.Track != null);
    }

    [Fact]
    public async Task RelativeRedirectsAndTrackSearchUseTheSameExpansion()
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            if (requests.Count == 1) return Redirect("/next");
            if (requests.Count == 2) return Redirect("https://soundcloud.com/artist/song");
            Assert.Equal("api-v2.soundcloud.com", request.RequestUri!.Host);
            Assert.Contains("url=https%3A%2F%2Fsoundcloud.com%2Fartist%2Fsong", request.RequestUri.Query);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"kind":"track","id":42,"title":"Song"}""") };
        }));
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test") };
        Assert.Equal(42, (await api.ResolveAsync("https://on.soundcloud.com/first")).Id);
        Assert.Equal("/next", requests[1].AbsolutePath); Assert.Equal(3, requests.Count);
    }

    [Theory]
    [InlineData("https://example.com/artist/song")]
    [InlineData("http://soundcloud.com/artist/song")]
    [InlineData("https://user@soundcloud.com/artist/song")]
    [InlineData("https://soundcloud.com:444/artist/song")]
    public async Task RedirectsOutsideSoundCloudNeverReachTheApi(string location)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return Redirect(location); }));
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http).ResolveChatMusicAsync("https://on.soundcloud.com/example"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RedirectLoopsAndLongChainsAreBounded()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => Redirect("https://on.soundcloud.com/" + ++calls)));
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http).ResolveChatMusicAsync("https://on.soundcloud.com/0"));
        Assert.Equal(5, calls);
        calls = 0;
        using var loop = new HttpClient(new Handler(_ => { calls++; return Redirect("https://on.soundcloud.com/same"); }));
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(loop).ResolveChatMusicAsync("https://on.soundcloud.com/same"));
        Assert.Equal(1, calls);
    }

    private static HttpResponseMessage Redirect(string url) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(url, UriKind.RelativeOrAbsolute) } };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
    private sealed class Browser(string canonical, string kind) : ISoundCloudBrowserTransport
    {
        internal int Reads;
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Reads++;
            Assert.Equal("api-v2.soundcloud.com", uri.Host);
            Assert.Equal("/resolve", uri.AbsolutePath);
            Assert.Equal("?url=" + Uri.EscapeDataString(canonical), uri.Query);
            return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(new { kind, id = 42, title = "Music" })));
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
