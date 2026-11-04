using System.Net;
using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class RegionalPlaybackSourceTests
{
    [Fact]
    public async Task SeparateAnonymousClientDiscoversMetadataAndPreservesDrmAuthorization()
    {
        var paths = new List<string>();
        using var source = new RegionalPlaybackSource(new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains("x-datadome-clientid"));
            Assert.Empty(request.RequestUri!.UserInfo);
            var host = request.RequestUri.Host; var path = request.RequestUri.AbsolutePath;
            paths.Add(host + path);
            if (host == "soundcloud.com" && path == "/") return Text("""
                {"hydratable":"apiClient","data":{"id":"anonymousClient"}}
                """);
            Assert.Equal("api-v2.soundcloud.com", host);
            Assert.Contains("client_id=anonymousClient", request.RequestUri.Query);
            if (path == "/tracks/42") return Text("""
                {"id":42,"policy":"MONETIZE","duration":114641,"track_authorization":"regionalAuth",
                 "media":{"transcodings":[{"url":"https://api-v2.soundcloud.com/regional-stream?track_authorization=stale",
                 "snipped":false,"format":{"protocol":"ctr-encrypted-hls"}}]}}
                """);
            Assert.Equal("/regional-stream", path);
            Assert.Contains("track_authorization=regionalAuth", request.RequestUri.Query);
            Assert.DoesNotContain("stale", request.RequestUri.Query);
            return Text("""{"url":"https://playback.media-streaming.soundcloud.cloud/full.m3u8","licenseAuthToken":"regionalLicense"}""");
        }));
        var track = await source.GetTrackAsync(42);
        var stream = await source.ResolveTranscodingAsync(track, Assert.Single(track.Media!.Transcodings));
        Assert.NotNull(stream);
        Assert.True(stream.Protected);
        Assert.Equal("regionalLicense", stream.LicenseAuthToken);
        Assert.Equal(114.641, stream.Duration, 3);
        Assert.Equal(new[] { "soundcloud.com/", "api-v2.soundcloud.com/tracks/42", "api-v2.soundcloud.com/regional-stream" }, paths);
    }

    [Theory]
    [InlineData("file:///proxy")]
    [InlineData("ftp://proxy.test")]
    [InlineData("http://proxy.test/#fragment")]
    [InlineData("http://proxy.test/?query")]
    public void InvalidProxyConfigurationIsRejected(string value) =>
        Assert.Throws<ArgumentException>(() => new RegionalPlaybackSource(new Uri(value)));

    [Fact]
    public async Task SeededPublicClientIdNeverBringsAccountHeadersOrLoadsHomepage()
    {
        using var source = new RegionalPlaybackSource(new Handler(request =>
        {
            Assert.Equal("api-v2.soundcloud.com", request.RequestUri!.Host);
            Assert.Contains("client_id=publicId", request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains("x-datadome-clientid"));
            return Text("""{"id":42,"policy":"ALLOW"}""");
        }), "publicId");
        Assert.Equal(42, (await source.GetTrackAsync(42)).Id);
    }

    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
