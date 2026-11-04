using System.Net;
using Pookie.SoundCloud;

namespace Pookie.App.Playback;

// Only this separate anonymous API client uses the proxy. Audio and CDM remain
// on their existing transports; account OAuth/cookies are never attached here.
internal sealed class RegionalPlaybackSource : ISoundCloudPlaybackSource, IDisposable
{
    private readonly HttpClient http;
    private readonly SoundCloudWebClient api;

    internal RegionalPlaybackSource(Uri proxy, string? publicClientId = null) : this(ProxyHandler(proxy), publicClientId) { }
    internal RegionalPlaybackSource(HttpMessageHandler handler, string? publicClientId = null)
    {
        http = new(handler) { Timeout = TimeSpan.FromSeconds(8) };
        api = publicClientId == null ? new(http) : new(http, publicClientId);
    }
    private static HttpClientHandler ProxyHandler(Uri proxy)
    {
        if (!proxy.IsAbsoluteUri || proxy.Scheme is not ("http" or "https" or "socks5") ||
            proxy.Host.Length == 0 || proxy.Fragment.Length != 0 || proxy.Query.Length != 0)
            throw new ArgumentException("Региональный прокси должен быть HTTP, HTTPS или SOCKS5 адресом.", nameof(proxy));
        var target = new WebProxy(proxy);
        if (proxy.UserInfo.Length > 0)
        {
            var parts = proxy.UserInfo.Split(':', 2);
            target.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]),
                parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "");
        }
        return new() { Proxy = target, UseProxy = true, UseCookies = false, AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli };
    }
    public Task<SoundCloudTrack> GetTrackAsync(long id, CancellationToken cancellationToken = default) => api.GetTrackAsync(id, cancellationToken);
    public Task<SoundCloudStream?> ResolveTranscodingAsync(SoundCloudTrack track, Transcoding transcoding, CancellationToken cancellationToken = default) =>
        api.ResolveTranscodingAsync(track, transcoding, cancellationToken);
    public void Dispose() => http.Dispose();
}
