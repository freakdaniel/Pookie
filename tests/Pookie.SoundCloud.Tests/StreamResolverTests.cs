using System.Net;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class StreamResolverTests
{
    [Theory]
    [InlineData(404, false, true)]
    [InlineData(410, false, true)]
    [InlineData(403, true, false)]
    [InlineData(401, false, false)]
    [InlineData(429, false, false)]
    [InlineData(503, false, false)]
    public async Task OnlyMissingTrackMetadataEstablishesUnavailableStatus(int status, bool challenge, bool unavailable)
    {
        var source = new Source(Track("auth")) { MetadataFailure = new("Fixture", status, challenge) };
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudStreamResolver(source).ResolveAsync(42));
        Assert.Equal(unavailable, error.TrackUnavailable);
        Assert.Equal(challenge, error.RequiresBrowserVerification);
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task MissingTranscodingDoesNotMarkTrackAsDeleted()
    {
        var source = new Source(Track("auth")) { Failure = new("Expired stream", 404) };
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudStreamResolver(source).ResolveAsync(42));
        Assert.False(error.TrackUnavailable);
    }
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
        var source = new Source(full with { Duration = 30000, FullDuration = 114625, Policy = policy, Media = new() { Transcodings = full.Media!.Transcodings.Select(t => t with { Snipped = snipped }).ToArray() } });
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
    [InlineData("SNIP", "")]
    [InlineData("", "preview")]
    public async Task PreviewFallsThroughToFreshRegionalMetadata(string policy, string access)
    {
        var local = new Source(new() { Id = 42, Policy = policy, Access = access, Duration = 30000 });
        var region = new Source(Track("regional-authorization") with { Duration = 114625 });
        var result = await new SoundCloudStreamResolver(local, region).ResolveAsync(42);
        Assert.True(result.UsedRegionalSource);
        Assert.Equal(114.625, result.Track.DurationSeconds, 3);
        Assert.Equal(0, local.StreamCalls);
        Assert.Equal("regional-authorization", region.ReceivedAuthorization);
    }

    [Fact]
    public async Task HealthyPrimaryNeverContactsRegionalProvider()
    {
        var region = new Source(Track("regional"));
        var result = await new SoundCloudStreamResolver(new Source(Track("local")), region).ResolveAsync(42);
        Assert.False(result.UsedRegionalSource);
        Assert.Equal(0, region.MetadataCalls);
    }

    [Fact]
    public async Task AllRegionalPreviewsRemainUnavailableAndNeverResolveAudio()
    {
        var local = new Source(new() { Id = 42, Policy = "SNIP" });
        var region = new Source(new() { Id = 42, Policy = "SNIP" });
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudStreamResolver(local, region).ResolveAsync(42));
        Assert.True(error.TrackUnavailable);
        Assert.Equal(0, local.StreamCalls + region.StreamCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegionalNetworkFailureDoesNotPoisonTrackAvailability(bool timeout)
    {
        var local = new Source(new() { Id = 42, Policy = "SNIP" });
        var region = new Source(Track("region")) { NetworkFailure = timeout ? new TaskCanceledException() : new HttpRequestException("Proxy unavailable") };
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudStreamResolver(local, region).ResolveAsync(42));
        Assert.False(error.TrackUnavailable);
        Assert.Equal(1, region.MetadataCalls);
        Assert.Contains("региональное соединение", error.Message);
        var final = new Source(Track("final"));
        var recovered = await new SoundCloudStreamResolver(local, region, final).ResolveAsync(42);
        Assert.True(recovered.UsedRegionalSource);
        Assert.Equal("final", final.ReceivedAuthorization);
    }

    [Fact]
    public async Task UserCancellationDuringFallbackIsNeverSwallowed()
    {
        using var stop = new CancellationTokenSource();
        var region = new Source(Track("region")) { OnMetadata = stop.Cancel, NetworkFailure = new TaskCanceledException() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SoundCloudStreamResolver(new Source(new() { Id = 42, Policy = "SNIP" }), region).ResolveAsync(42, stop.Token));
    }

    [Theory]
    [InlineData("https://playback.media-streaming.soundcloud.cloud/x", true)]
    [InlineData("https://playback.media-streaming.soundcloud.cloud.evil.test/x", false)]
    [InlineData("https://evil.soundcloud.cloud/x", false)]
    [InlineData("https://token@playback.media-streaming.soundcloud.cloud/x", false)]
    [InlineData("https://playback.media-streaming.soundcloud.cloud:444/x", false)]
    public void OnlyExactProtectedMediaHostIsAccepted(string url, bool accepted) =>
        Assert.Equal(accepted, SoundCloudWebClient.IsMediaUri(new(url)));

    [Theory]
    [InlineData("ALLOW", true, false)]
    [InlineData("ALLOW", false, false)]
    [InlineData("SNIP", false, true)]
    [InlineData("BLOCK", false, true)]
    public async Task AutomaticPoolOnlyRunsForFreshRegionalRestrictions(string policy, bool playable, bool expected)
    {
        var pool = new Pool();
        var track = Track("account") with { Policy = policy, Media = playable ? Track("account").Media : null };
        var resolver = new SoundCloudStreamResolver(new Source(track)) { RegionalResolver = pool };
        if (policy == "ALLOW" && !playable) await Assert.ThrowsAsync<SoundCloudException>(() => resolver.ResolveAsync(42));
        else Assert.Equal(expected, (await resolver.ResolveAsync(42)).UsedRegionalSource);
        Assert.Equal(expected ? 1 : 0, pool.Calls);
    }

    [Fact]
    public async Task PoolCancellationPropagatesWithoutChangingTrackAvailability()
    {
        using var cancel = new CancellationTokenSource();
        var pool = new Pool { Resolve = token => { cancel.Cancel(); return Task.FromCanceled<ResolvedPlayback>(token); } };
        var resolver = new SoundCloudStreamResolver(new Source(new() { Id = 42, Policy = "SNIP" })) { RegionalResolver = pool };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(42, cancel.Token));
    }

    private sealed class Pool : ISoundCloudPlaybackResolver
    {
        internal int Calls;
        internal Func<CancellationToken, Task<ResolvedPlayback>>? Resolve;
        public Task<ResolvedPlayback> ResolveAsync(long id, CancellationToken cancellationToken = default, string[]? protocols = null)
        {
            Calls++;
            Assert.Contains("progressive", protocols!);
            return Resolve?.Invoke(cancellationToken) ?? Task.FromResult(new ResolvedPlayback(Track("regional"), new(new("https://cf-media.sndcdn.com/full"), "progressive", 120)));
        }
    }

    [Theory]
    [InlineData("https://evil.test/full", "ALLOW", 42)]
    [InlineData("https://cf-media.sndcdn.com/full", "SNIP", 42)]
    [InlineData("https://cf-media.sndcdn.com/full", "ALLOW", 99)]
    public async Task PoolCannotReturnUntrustedHostPreviewOrDifferentTrack(string url, string policy, long id)
    {
        var pool = new Pool { Resolve = _ => Task.FromResult(new ResolvedPlayback(Track("regional") with { Id = id, Policy = policy },
            new(new(url), "progressive", 120))) };
        var resolver = new SoundCloudStreamResolver(new Source(new() { Id = 42, Policy = "SNIP" })) { RegionalResolver = pool };
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => resolver.ResolveAsync(42));
        Assert.False(error.TrackUnavailable);
    }

    private static SoundCloudTrack Track(string authorization) => new()
    {
        Id = 42, TrackAuthorization = authorization, Media = new() { Transcodings = [new()
        { Url = "https://api-v2.soundcloud.com/stream", Format = new() { Protocol = "progressive" } }] }
    };

    private sealed class Source(SoundCloudTrack track) : ISoundCloudPlaybackSource
    {
        public int StreamCalls, MetadataCalls;
        public Exception? NetworkFailure;
        public Action? OnMetadata;
        public string? ReceivedAuthorization;
        public SoundCloudException? Failure;
        public SoundCloudException? MetadataFailure;
        public Task<SoundCloudTrack> GetTrackAsync(long id, CancellationToken cancellationToken = default)
        {
            MetadataCalls++; OnMetadata?.Invoke();
            var error = (Exception?)MetadataFailure ?? NetworkFailure;
            return error != null ? Task.FromException<SoundCloudTrack>(error) : Task.FromResult(track);
        }
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
