using System.Text.Json;
using Pookie.App.Storage;
using Pookie.Logging;
using Pookie.SoundCloud;

namespace Pookie.App.Playback;

internal sealed class WaveformGainResolver(HttpClient http, AppDataPaths paths)
{
    internal static Uri? WaveformUri(string? value)
    {
        if (value is not { Length: > 0 and <= 8192 } || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !SoundCloudWebClient.IsMediaUri(uri) || uri.Fragment != "") return null;
        if (uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            uri = new UriBuilder(uri) { Path = uri.AbsolutePath[..^4] + ".json" }.Uri;
        return uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? uri : null;
    }

    public async Task<double> ResolveAsync(string? waveformUrl, CancellationToken token)
    {
        if (WaveformUri(waveformUrl) is not { } uri) return 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var bytes = await ReadAsync(uri, timeout.Token);
            if (bytes == null) return 0;
            using var json = JsonDocument.Parse(bytes);
            return WaveformNormalization.EstimateGainDb(json.RootElement);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { AppLog.For("Pookie.Playback").Debug("Waveform не получен вовремя; трек запускается без поправки громкости"); return 0; }
        catch (Exception error) when (error is JsonException or HttpRequestException or IOException)
        { AppLog.For("Pookie.Playback").Debug("Поправка громкости недоступна: {Type}", error.GetType().Name); return 0; }
    }

    public async Task<byte[]?> ReadAsync(Uri uri, CancellationToken token)
    {
        if (WaveformUri(uri.AbsoluteUri) != uri) throw new ArgumentException("Invalid waveform URI.", nameof(uri));
        var cache = new WaveformDiskCache(paths);
        var bytes = await cache.ReadAsync(uri.AbsoluteUri, token);
        if (bytes != null)
        {
            try { using var cached = JsonDocument.Parse(bytes); WaveformData.Parse(cached.RootElement); return bytes; }
            catch (JsonException) { AppLog.For("Pookie.Playback").Debug("Повреждённый waveform в кеше будет загружен заново"); }
        }
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > WaveformDiskCache.MaxEntrySize) return null;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > WaveformDiskCache.MaxEntrySize) return null;
            output.Write(buffer, 0, count);
        }
        bytes = output.ToArray();
        using var json = JsonDocument.Parse(bytes);
        WaveformData.Parse(json.RootElement);
        await cache.WriteAsync(uri.AbsoluteUri, bytes, token);
        return bytes;
    }
}
