using System.Net;
using System.Text.Json;
using Pookie.Logging;

namespace Pookie.App.Playback;

internal sealed record RegionalProxyEndpoint(Uri Address, string Country, double LatencyMs, bool MaintainedRelay = false);

// Lists are discovery data, never an authority on playback availability or TLS trust.
internal sealed partial class RegionalProxyCatalog(HttpClient http, Uri[]? feeds = null, TimeProvider? timeProvider = null)
{
    private static readonly Uri[] DefaultFeeds = [
        new("https://raw.githubusercontent.com/proxyscrape/free-proxy-list/main/proxies/all/data.json"),
        new("https://raw.githubusercontent.com/proxifly/free-proxy-list/main/proxies/all/data.json")];
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim refresh = new(1, 1);
    private IReadOnlyList<RegionalProxyEndpoint> cached = [];
    private DateTimeOffset nextRefresh;

    internal async Task<IReadOnlyList<RegionalProxyEndpoint>> GetAsync(CancellationToken token)
    {
        await refresh.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (clock.GetUtcNow() < nextRefresh) return cached;
            var downloads = (feeds ?? DefaultFeeds).Select(feed => LoadAsync(feed, token)).ToList();
            if (feeds == null) downloads.Add(LoadMaintainedRelayAsync(token));
            var results = await Task.WhenAll(downloads).ConfigureAwait(false);
            var combined = results.SelectMany(result => result).OrderByDescending(proxy => proxy.MaintainedRelay)
                .DistinctBy(proxy => proxy.Address).Take(16384).ToArray();
            if (combined.Length > 0) cached = combined;
            nextRefresh = clock.GetUtcNow().AddMinutes(combined.Length > 0 ? 10 : 1);
            return cached;
        }
        finally { refresh.Release(); }
    }

    private async Task<IReadOnlyList<RegionalProxyEndpoint>> LoadAsync(Uri feed, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            return Parse(await ReadBoundedAsync(feed, 4 * 1024 * 1024, deadline.Token).ConfigureAwait(false));
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            AppLog.For("Pookie.RegionalPlayback").Debug("Источник списка прокси недоступен: {ErrorType}", error.GetType().Name);
            return [];
        }
    }

    private async Task<byte[]> ReadBoundedAsync(Uri uri, int maximum, CancellationToken token)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new HttpRequestException("Источник списка прокси слишком большой.");
        await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await body.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > maximum) throw new HttpRequestException("Источник списка прокси слишком большой.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    internal static IReadOnlyList<RegionalProxyEndpoint> Parse(ReadOnlyMemory<byte> bytes)
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (json.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Ожидался список прокси.");
        var result = new List<RegionalProxyEndpoint>();
        foreach (var row in json.RootElement.EnumerateArray().Take(16384))
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("protocol", out var protocol) || protocol.ValueKind != JsonValueKind.String ||
                protocol.GetString() is not ("http" or "https" or "socks5") ||
                !row.TryGetProperty("ip", out var host) || host.ValueKind != JsonValueKind.String || !IPAddress.TryParse(host.GetString(), out var ip) ||
                !IsPublicAddress(ip) || !row.TryGetProperty("port", out var port) || port.ValueKind != JsonValueKind.Number ||
                !port.TryGetInt32(out var number) || number is < 1 or > 65535) continue;
            if (protocol.GetString() == "http" && row.TryGetProperty("ssl", out var ssl) && ssl.ValueKind == JsonValueKind.False) continue;
            var country = row.TryGetProperty("country_code", out var countryValue) && countryValue.ValueKind == JsonValueKind.String ? countryValue.GetString() :
                row.TryGetProperty("geolocation", out var geo) && geo.ValueKind == JsonValueKind.Object && geo.TryGetProperty("country", out countryValue) &&
                countryValue.ValueKind == JsonValueKind.String ? countryValue.GetString() : null;
            if (country is not { Length: 2 } || !country.All(char.IsAsciiLetter)) continue;
            var latency = row.TryGetProperty("latency_ms", out var latencyValue) && latencyValue.ValueKind == JsonValueKind.Number &&
                latencyValue.TryGetDouble(out var ms) && double.IsFinite(ms) && ms >= 0 ? ms : 2000;
            var address = new UriBuilder(protocol.GetString()!, ip.ToString(), number).Uri;
            result.Add(new(address, country.ToUpperInvariant(), latency));
        }
        return result.DistinctBy(proxy => proxy.Address).ToArray();
    }

    private static bool IsPublicAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        var b = ip.GetAddressBytes();
        return b.Length == 4
            ? b[0] is not (0 or 10 or 127) && b[0] < 224 && !(b[0] == 169 && b[1] == 254) &&
              !(b[0] == 172 && b[1] is >= 16 and <= 31) && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] is >= 64 and <= 127)
            : b[0] is >= 0x20 and <= 0x3f; // Global unicast; excludes local, multicast and unspecified addresses.
    }
}
