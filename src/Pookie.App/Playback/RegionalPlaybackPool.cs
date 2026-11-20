using Pookie.Localization;
using System.Collections.Concurrent;
using System.Net;
using Pookie.Logging;
using Pookie.SoundCloud;

namespace Pookie.App.Playback;

internal sealed class RegionalPlaybackPool : ISoundCloudPlaybackResolver, IDisposable
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<RegionalProxyEndpoint>>> candidates;
    private readonly Func<CancellationToken, Task<string>> publicClientId;
    private readonly Func<Uri, string, ISoundCloudPlaybackSource> createSource;
    private readonly TimeProvider clock;
    private readonly HttpClient? discoveryHttp;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim resolution = new(1, 1);
    private readonly ConcurrentDictionary<Uri, DateTimeOffset> cooldowns = new();
    private readonly Dictionary<long, Uri> preferredTracks = [];
    private readonly Dictionary<Uri, DateTimeOffset> healthy = [];
    private string? cachedClientId;
    private DateTimeOffset clientIdExpires;
    internal event Action<string, string, int, string>? ProbeCompleted;

    internal static RegionalPlaybackPool? FromEnvironment(bool enabledByDefault = true, string? cacheDirectory = null)
    {
        var configured = Environment.GetEnvironmentVariable("POOKIE_REGIONAL_PROXY");
        if (string.Equals(configured, "off", StringComparison.OrdinalIgnoreCase) || !enabledByDefault && string.IsNullOrWhiteSpace(configured)) return null;
        Uri? fixedProxy = string.IsNullOrWhiteSpace(configured) ? null : new(configured, UriKind.Absolute);
        if (fixedProxy != null) { using var validation = new RegionalPlaybackSource(fixedProxy); }
        // Discovery has no connection to the account transport, even if the app uses a proxy.
        var http = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(10) };
        var catalog = new RegionalProxyCatalog(http);
        var pool = new RegionalPlaybackPool(fixedProxy == null ? catalog.GetAsync : _ => Task.FromResult<IReadOnlyList<RegionalProxyEndpoint>>([new(fixedProxy, "", 0)]),
            token => new SoundCloudWebClient(http).GetClientIdAsync(token), (proxy, clientId) => new RegionalPlaybackSource(proxy, clientId),
            discoveryHttp: http);
        if (cacheDirectory != null)
        {
            var token = pool.lifetime.Token;
            _ = Task.Run(() => RegionalProxyHealth.MonitorAsync(cacheDirectory, async cancellation =>
                fixedProxy ?? (await catalog.GetAsync(cancellation).ConfigureAwait(false)).FirstOrDefault(p => p.MaintainedRelay)?.Address, token));
        }
        return pool;
    }

    internal RegionalPlaybackPool(Func<CancellationToken, Task<IReadOnlyList<RegionalProxyEndpoint>>> candidates,
        Func<CancellationToken, Task<string>> publicClientId, Func<Uri, string, ISoundCloudPlaybackSource> createSource,
        TimeProvider? timeProvider = null, HttpClient? discoveryHttp = null)
    {
        this.candidates = candidates; this.publicClientId = publicClientId; this.createSource = createSource;
        clock = timeProvider ?? TimeProvider.System; this.discoveryHttp = discoveryHttp;
    }

    public async Task<ResolvedPlayback> ResolveAsync(long trackId, CancellationToken cancellationToken = default, string[]? protocols = null)
    {
        if (trackId <= 0) throw new ArgumentOutOfRangeException(nameof(trackId));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var available = 0;
        var attempted = 0;
        var restricted = 0;
        var failed = 0;
        var full = 0;
        await resolution.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var listTask = candidates(token);
            var clientTask = GetPublicClientIdAsync(token);
            await Task.WhenAll(listTask, clientTask).ConfigureAwait(false);
            var list = Order(await listTask.ConfigureAwait(false), trackId);
            available = list.Count;
            var clientId = await clientTask.ConfigureAwait(false);
            AppLog.For("Pookie.RegionalPlayback").Debug("Трек {TrackId}: начинаем проверку подключений, отобрано {Candidates}", trackId, available);
            using var race = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pending = new ConcurrentQueue<RegionalProxyEndpoint>(list);
            // A worker stops immediately after finding a complete, validated playback link.
            var workers = Enumerable.Range(0, Math.Min(4, list.Count)).Select(_ => WorkerAsync()).ToList();
            while (workers.Count > 0)
            {
                var completed = await Task.WhenAny(workers).ConfigureAwait(false);
                workers.Remove(completed);
                var found = await completed.ConfigureAwait(false);
                if (found is not { } success) continue;
                race.Cancel();
                await Task.WhenAll(workers).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                Remember(trackId, success.Proxy);
                AppLog.For("Pookie.RegionalPlayback").Debug("Полный поток трека {TrackId} найден через анонимное региональное соединение", trackId);
                return success.Playback with { UsedRegionalSource = true };
            }
            token.ThrowIfCancellationRequested();
            throw new SoundCloudException(UiText.Get("audio.region.poolUnavailable"));

            async Task<(Uri Proxy, ResolvedPlayback Playback)?> WorkerAsync()
            {
                while (!race.IsCancellationRequested && pending.TryDequeue(out var endpoint))
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(race.Token);
                    attempt.CancelAfter(TimeSpan.FromSeconds(8));
                    ISoundCloudPlaybackSource? source = null;
                    try
                    {
                        Interlocked.Increment(ref attempted);
                        source = createSource(endpoint.Address, clientId);
                        var playback = await new SoundCloudStreamResolver(source).ResolveAsync(trackId, attempt.Token, protocols).ConfigureAwait(false);
                        Interlocked.Increment(ref full);
                        ProbeCompleted?.Invoke(endpoint.Country, endpoint.Address.Scheme, 200, "full-stream");
                        race.Cancel();
                        return (endpoint.Address, playback);
                    }
                    catch (Exception error) when (error is SoundCloudException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
                    {
                        if (race.IsCancellationRequested) return null;
                        ProbeCompleted?.Invoke(endpoint.Country, endpoint.Address.Scheme, (error as SoundCloudException)?.StatusCode ?? 0,
                            error is SoundCloudException { TrackUnavailable: true } ? "restricted" : error.GetType().Name);
                        // A regional restriction belongs to this track; a network failure belongs to the exit.
                        if (error is SoundCloudException { TrackUnavailable: true }) Interlocked.Increment(ref restricted);
                        else
                        {
                            Interlocked.Increment(ref failed);
                            cooldowns[endpoint.Address] = clock.GetUtcNow().AddMinutes(2);
                        }
                    }
                    finally { (source as IDisposable)?.Dispose(); }
                }
                return null;
            }
        }
        finally
        {
            resolution.Release();
            AppLog.For("Pookie.RegionalPlayback").Debug(
                "Трек {TrackId}: кандидатов {Candidates}, начато {Attempted}, ограничено {Restricted}, ошибок {Failed}, полных потоков {Full}, отмена {Cancelled}, таймаут {TimedOut}",
                trackId, available, attempted, restricted, failed, full, cancellationToken.IsCancellationRequested, deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !lifetime.IsCancellationRequested);
        }
    }

    private IReadOnlyList<RegionalProxyEndpoint> Order(IReadOnlyList<RegionalProxyEndpoint> list, long trackId)
    {
        var now = clock.GetUtcNow();
        foreach (var key in cooldowns.Where(pair => pair.Value <= now).Select(pair => pair.Key)) cooldowns.TryRemove(key, out _);
        foreach (var key in healthy.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray()) healthy.Remove(key);
        preferredTracks.TryGetValue(trackId, out var preferred);
        // Round-robin countries and networks rather than trying a whole country or subnet first.
        var diverse = list.Where(endpoint => !cooldowns.ContainsKey(endpoint.Address)).GroupBy(endpoint => endpoint.Country)
            .SelectMany(country => country.OrderBy(endpoint => endpoint.Address.Scheme == "https" ? 1 : 0).ThenBy(endpoint => endpoint.LatencyMs)
                .DistinctBy(endpoint => string.Join('.', endpoint.Address.Host.Split('.').Take(3)))
                .Select((endpoint, index) => (endpoint, index)))
            .OrderBy(item => item.index).ThenBy(item => CountryPriority(item.endpoint.Country)).ThenBy(item => item.endpoint.LatencyMs)
            .Select(item => item.endpoint);
        var recent = healthy.Keys.Select(address => new RegionalProxyEndpoint(address, "", 0))
            .Concat(list.Where(endpoint => endpoint.Address == preferred || healthy.ContainsKey(endpoint.Address) || endpoint.MaintainedRelay))
            .OrderBy(endpoint => endpoint.Address == preferred ? 0 : healthy.ContainsKey(endpoint.Address) ? 1 : 2);
        return recent.Concat(diverse).DistinctBy(endpoint => endpoint.Address).Where(endpoint => !cooldowns.ContainsKey(endpoint.Address)).Take(24).ToArray();
    }

    private static int CountryPriority(string country) => country switch
    {
        "SE" or "FI" or "NO" or "DK" => 0,
        "NL" or "DE" or "GB" or "US" or "FR" => 1,
        _ => 2
    };

    private void Remember(long trackId, Uri proxy)
    {
        if (preferredTracks.Count >= 128) preferredTracks.Remove(preferredTracks.Keys.First());
        preferredTracks[trackId] = proxy;
        healthy[proxy] = clock.GetUtcNow().AddMinutes(10);
    }

    private async Task<string> GetPublicClientIdAsync(CancellationToken token)
    {
        if (cachedClientId != null && clock.GetUtcNow() < clientIdExpires) return cachedClientId;
        cachedClientId = await publicClientId(token).ConfigureAwait(false);
        clientIdExpires = clock.GetUtcNow().AddMinutes(30);
        return cachedClientId;
    }

    public void Dispose() { lifetime.Cancel(); discoveryHttp?.Dispose(); }
}
