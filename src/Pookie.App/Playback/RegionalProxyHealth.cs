using System.Text.Json;
using System.Text.Json.Serialization;
using Pookie.Logging;

namespace Pookie.App.Playback;

internal static partial class RegionalProxyHealth
{
    private sealed record Check(string Address, DateTimeOffset CheckedAt);
    [JsonSerializable(typeof(Check))]
    private sealed partial class CacheJson : JsonSerializerContext;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    // Check the configured proxy or the operator's published relay against a
    // public HTTPS resource. Track availability is decided separately at playback.
    internal static async Task MonitorAsync(string directory, Func<CancellationToken, Task<Uri?>> endpoint, CancellationToken token)
    {
        var path = Path.Combine(directory, "regional-proxy-health.json");
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var address = await endpoint(token).ConfigureAwait(false);
                    if (address != null)
                        await RefreshAsync(path, address, async (uri, cancellation) =>
                        {
                            using var source = new RegionalPlaybackSource(uri);
                            return await source.CheckConnectionAsync(cancellation).ConfigureAwait(false);
                        }, TimeProvider.System, token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or JsonException ||
                    error is OperationCanceledException && !token.IsCancellationRequested)
                {
                    AppLog.For("Pookie.RegionalPlayback").Debug("Фоновая проверка подключения: {ErrorType}", error.GetType().Name);
                }
                await Task.Delay(Interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
    }

    internal static async Task RefreshAsync(string path, Uri endpoint, Func<Uri, CancellationToken, Task<bool>> probe,
        TimeProvider clock, CancellationToken token)
    {
        var address = new UriBuilder(endpoint.Scheme, endpoint.Host, endpoint.Port).Uri.AbsoluteUri;
        if (new FileInfo(path) is { Exists: true, Length: <= 4096 })
        {
            try
            {
                var last = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), CacheJson.Default.Check);
                var now = clock.GetUtcNow();
                if (last?.Address == address && last.CheckedAt <= now && last.CheckedAt > now - Interval) return;
            }
            catch (JsonException) { /* Replace an unreadable cache after a successful check. */ }
            catch (FileNotFoundException) { }
        }
        bool connected;
        try { connected = await probe(endpoint, token).ConfigureAwait(false); }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            File.Delete(path);
            throw;
        }
        if (!connected)
        {
            File.Delete(path);
            return;
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Check(address, clock.GetUtcNow()), CacheJson.Default.Check), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
