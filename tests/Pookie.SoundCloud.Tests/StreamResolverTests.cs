using System.Net;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class StreamResolverTests
{
    [Fact]
    public async Task FreshMetadataOverridesBlockedSearchResultAndStaleAuthorization()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (request.RequestUri!.AbsolutePath == "/tracks/42") return Text("""
                {"id":42,"policy":"ALLOW","track_authorization":"fresh",
                 "media":{"transcodings":[{"url":"https://api-v2.soundcloud.com/stream?track_authorization=old",
                 "format":{"protocol":"progressive","mime_type":"audio/mpeg"}}]}}
                """);
            Assert.Contains("track_authorization=fresh", request.RequestUri.Query);
            Assert.DoesNotContain("track_authorization=old", request.RequestUri.Query);
            return Text("""{"url":"https://cf-media.sndcdn.com/audio"}""");
        }));
        var api = new SoundCloudWebClient(http) { Session = new("id", "token", "Test/1") };
        var stream = await api.GetStreamAsync(new() { Id = 42, Access = "blocked", TrackAuthorization = "old" });
        Assert.False(stream.Protected);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Plain404FallsThroughToFullDrmStreamAndRetainsLicenseAuthorization()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            return path switch
            {
                "/tracks/42" => Text("""
                    {"id":42,"policy":"MONETIZE","duration":114046,"media":{"transcodings":[
                    {"url":"https://api-v2.soundcloud.com/drm","format":{"protocol":"ctr-encrypted-hls"}},
                    {"url":"https://api-v2.soundcloud.com/plain","format":{"protocol":"hls"}}]}}
                    """),
                "/plain" => new(HttpStatusCode.NotFound),
                "/drm" => Text("""{"url":"https://playback.media-streaming.soundcloud.cloud/track.m3u8?signature=secret","licenseAuthToken":"license-secret"}"""),
                _ => throw new Xunit.Sdk.XunitException("CDN/audio request made by resolver")
            };
        }));
        var result = await new SoundCloudStreamResolver(new SoundCloudWebClient(http) { Session = new("id", "token", "Test/1") }).ResolveAsync(42);
        Assert.Equal(new[] { "/tracks/42", "/plain", "/drm" }, paths);
        Assert.True(result.Stream.Protected);
        Assert.Equal("license-secret", result.Stream.LicenseAuthToken);
        Assert.Equal(114.046, result.Stream.Duration, 3);
        Assert.DoesNotContain("secret", result.ToString());
        Assert.DoesNotContain("secret", result.Stream.ToString());
    }

    [Fact]
    public async Task RegionalSourceUsesItsOwnMetadataAndAuthorizationWithoutLoadingMedia()
    {
        var local = new Source(new() { Id = 42, Policy = "BLOCK" });
        var region = new Source(Track("regional-authorization"));
        var result = await new SoundCloudStreamResolver(local, region).ResolveAsync(42);
        Assert.Equal(0, local.StreamCalls);
        Assert.Equal(1, region.StreamCalls);
        Assert.Equal("regional-authorization", region.ReceivedAuthorization);
        Assert.Equal("progressive", result.Stream.Protocol);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "SNIP")]
    public async Task PreviewCannotBeReturnedAsFullPlayback(bool snipped, string policy)
    {
        var full = Track("auth");
        var source = new Source(full with { Policy = policy, Media = new() { Transcodings = full.Media!.Transcodings.Select(t => t with { Snipped = snipped }).ToArray() } });
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudStreamResolver(source).ResolveAsync(42));
        Assert.Equal(0, source.StreamCalls);
    }

    [Fact]
    public async Task BrowserChallengeIsPropagatedInsteadOfMasqueradingAsMissingStream()
    {
        var source = new Source(Track("auth")) { Failure = new("Challenge", 403, true) };
        var region = new Source(Track("regional"));
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudStreamResolver(source, region).ResolveAsync(42));
        Assert.True(error.RequiresBrowserVerification);
        Assert.Equal(0, region.StreamCalls);
    }

    [Theory]
    [InlineData("https://playback.media-streaming.soundcloud.cloud/x", true)]
    [InlineData("https://playback.media-streaming.soundcloud.cloud.evil.test/x", false)]
    [InlineData("https://evil.soundcloud.cloud/x", false)]
    [InlineData("https://token@playback.media-streaming.soundcloud.cloud/x", false)]
    [InlineData("https://playback.media-streaming.soundcloud.cloud:444/x", false)]
    public void OnlyExactProtectedMediaHostIsAccepted(string url, bool accepted) =>
        Assert.Equal(accepted, SoundCloudWebClient.IsMediaUri(new(url)));

    private static SoundCloudTrack Track(string authorization) => new()
    {
        Id = 42, TrackAuthorization = authorization, Media = new() { Transcodings = [new()
        { Url = "https://api-v2.soundcloud.com/stream", Format = new() { Protocol = "progressive" } }] }
    };

    private sealed class Source(SoundCloudTrack track) : ISoundCloudPlaybackSource
    {
        public int StreamCalls;
        public string? ReceivedAuthorization;
        public SoundCloudException? Failure;
        public Task<SoundCloudTrack> GetTrackAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(track);
        public Task<SoundCloudStream?> ResolveTranscodingAsync(SoundCloudTrack value, Transcoding transcoding, CancellationToken cancellationToken = default)
        {
            StreamCalls++; ReceivedAuthorization = value.TrackAuthorization;
            if (Failure != null) throw Failure;
            return Task.FromResult<SoundCloudStream?>(new(new("https://cf-media.sndcdn.com/audio"), "progressive", 120));
        }
    }
    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
