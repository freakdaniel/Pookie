using System.Net;
using System.Text;
using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class RegionalPlaybackPoolTests
{
    [Fact]
    public void BothFeedSchemasExcludeLocalAddressesCredentialsAndUnsupportedProtocols()
    {
        var endpoints = RegionalProxyCatalog.Parse(Encoding.UTF8.GetBytes("""
            [{"protocol":"http","ip":"8.8.8.8","port":8080,"ssl":true,"country_code":"SE","latency_ms":80},
             {"protocol":"socks5","ip":"1.1.1.1","port":1080,"geolocation":{"country":"us"}},
             {"protocol":"http","ip":"127.0.0.1","port":80,"country_code":"SE"},
             {"protocol":"http","ip":"192.168.1.2","port":80,"country_code":"SE"},
             {"protocol":"http","ip":"169.254.169.254","port":80,"country_code":"SE"},
             {"protocol":"http","ip":"::ffff:10.0.0.1","port":80,"country_code":"SE"},
             {"protocol":"http","ip":"fc00::1","port":80,"country_code":"SE"},
             {"protocol":"socks4","ip":"8.8.4.4","port":80,"country_code":"SE"},
             {"protocol":"http","ip":"attacker.test","port":80,"country_code":"SE"},
             {"protocol":"http","ip":"8.8.4.4","port":"invalid","country_code":"SE"},
             {"protocol":"http","ip":"8.8.4.4","port":80,"ssl":false,"country_code":"SE"},
             {"protocol":"http","ip":"8.8.8.8","port":8080,"ssl":true,"country_code":"SE"}]
            """));
        Assert.Equal(2, endpoints.Count);
        Assert.Equal(new[] { "SE", "US" }, endpoints.Select(endpoint => endpoint.Country));
        Assert.All(endpoints, endpoint => Assert.Empty(endpoint.Address.UserInfo));
    }

    [Fact]
    public async Task IndependentFeedFailureDoesNotPreventRefreshAndOldDataSurvivesOutage()
    {
        var clock = new Clock(); var reads = 0; var available = true;
        using var http = new HttpClient(new Handler(request =>
        {
            reads++;
            if (request.RequestUri!.Host == "bad.test" || !available) return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK) { Content = new StringContent("""[{"protocol":"socks5","ip":"8.8.8.8","port":1080,"country_code":"SE"}]""") };
        }));
        var catalog = new RegionalProxyCatalog(http, [new("https://bad.test/list"), new("https://good.test/list")], clock);
        Assert.Single(await catalog.GetAsync(default));
        Assert.Single(await catalog.GetAsync(default));
        Assert.Equal(2, reads);
        clock.Now += TimeSpan.FromMinutes(11); available = false;
        Assert.Single(await catalog.GetAsync(default));
        Assert.Equal(4, reads);
    }

    [Fact]
    public async Task WorkingMetadataWithoutUsableStreamDoesNotWinAndFailedExitCoolsDown()
    {
        var clock = new Clock(); var brokenCalls = 0; var winningCalls = 0;
        var endpoints = new[] { Endpoint(1), Endpoint(2) };
        using var pool = Pool(endpoints, uri => uri == endpoints[0].Address
            ? new Source(() => { brokenCalls++; return new SoundCloudException("Missing stream", 404); })
            : new Source(() => { winningCalls++; return null; }), clock);
        var result = await pool.ResolveAsync(42, protocols: ["ctr-encrypted-hls"]);
        Assert.True(result.UsedRegionalSource); Assert.Equal("regionalLicense", result.Stream.LicenseAuthToken);
        Assert.Equal(1, brokenCalls); Assert.Equal(1, winningCalls);
        await pool.ResolveAsync(42);
        Assert.Equal(1, brokenCalls); Assert.Equal(2, winningCalls);
    }

    [Fact]
    public void MaintainedRelayIsParsedAsDataWithoutAcceptingLocalOrExecutableValues()
    {
        var relay = Assert.Single(RegionalProxyCatalog.ParseMaintainedRelay("return { type: 'http', host: '8.8.8.8', port: 8080, };"));
        Assert.True(relay.MaintainedRelay);
        Assert.Equal("http://8.8.8.8:8080/", relay.Address.AbsoluteUri);
        Assert.Empty(RegionalProxyCatalog.ParseMaintainedRelay("return { type: 'http', host: '127.0.0.1', port: 8080, };"));
        Assert.Empty(RegionalProxyCatalog.ParseMaintainedRelay("return { type: 'http', host: getHost(), port: 8080, };"));
    }

    [Fact]
    public async Task HealthyExitAndPublicClientIdSurviveTemporaryCatalogOutage()
    {
        var catalogs = 0; var ids = 0; var endpoints = new[] { Endpoint(1) };
        using var pool = new RegionalPlaybackPool(_ => Task.FromResult<IReadOnlyList<RegionalProxyEndpoint>>(++catalogs == 1 ? endpoints : []),
            _ => { ids++; return Task.FromResult("publicId"); }, (_, _) => new Source(() => null));
        Assert.True((await pool.ResolveAsync(42)).UsedRegionalSource);
        Assert.True((await pool.ResolveAsync(42)).UsedRegionalSource);
        Assert.Equal(2, catalogs); Assert.Equal(1, ids);
    }

    [Fact]
    public async Task PoolCancellationStopsEveryWorkerAndDoesNotStartMoreThanFour()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var disposed = 0;
        using var cancel = new CancellationTokenSource();
        using var pool = Pool(Enumerable.Range(1, 20).Select(Endpoint).ToArray(), _ => new WaitingSource(
            () => { if (Interlocked.Increment(ref active) == 4) started.SetResult(); }, () => Interlocked.Increment(ref disposed)));
        var pending = pool.ResolveAsync(42, cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(4, active); Assert.Equal(4, disposed);
    }

    [Fact]
    public async Task EmptyOrRestrictedPoolDoesNotPermanentlyMarkTrackUnavailable()
    {
        using var pool = Pool([Endpoint(1)], _ => new Source(() => null, preview: true));
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => pool.ResolveAsync(42));
        Assert.False(error.TrackUnavailable);
    }

    private static RegionalProxyEndpoint Endpoint(int index) => new(new($"socks5://8.8.{index}.1:1080"), "SE", index);
    private static RegionalPlaybackPool Pool(IReadOnlyList<RegionalProxyEndpoint> endpoints, Func<Uri, ISoundCloudPlaybackSource> create, TimeProvider? clock = null) =>
        new(_ => Task.FromResult(endpoints), _ => Task.FromResult("publicId"), (uri, clientId) =>
        { Assert.Equal("publicId", clientId); return create(uri); }, clock);

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private sealed class Source(Func<Exception?> streamFailure, bool preview = false) : ISoundCloudPlaybackSource
    {
        public Task<SoundCloudTrack> GetTrackAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(new SoundCloudTrack
        {
            Id = id, Policy = preview ? "SNIP" : "MONETIZE", Duration = 114641, TrackAuthorization = "regionalAuth",
            Media = new() { Transcodings = [new() { Url = "https://api-v2.soundcloud.com/stream", Format = new() { Protocol = "ctr-encrypted-hls" } }] }
        });
        public Task<SoundCloudStream?> ResolveTranscodingAsync(SoundCloudTrack track, Transcoding transcoding, CancellationToken cancellationToken = default)
        {
            var failure = streamFailure();
            return failure == null ? Task.FromResult<SoundCloudStream?>(new(new("https://cf-media.sndcdn.com/full.m3u8"), "ctr-encrypted-hls", 114.641) { LicenseAuthToken = "regionalLicense" })
                : Task.FromException<SoundCloudStream?>(failure);
        }
    }
    private sealed class WaitingSource(Action started, Action disposed) : ISoundCloudPlaybackSource, IDisposable
    {
        public async Task<SoundCloudTrack> GetTrackAsync(long id, CancellationToken cancellationToken = default)
        {
            started();
            return await new TaskCompletionSource<SoundCloudTrack>().Task.WaitAsync(cancellationToken);
        }
        public Task<SoundCloudStream?> ResolveTranscodingAsync(SoundCloudTrack track, Transcoding transcoding, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Cancelled metadata must never reach a stream request.");
        public void Dispose() => disposed();
    }
}
