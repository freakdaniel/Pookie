using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pookie.Logging;

namespace Pookie.App.Playback;

internal sealed partial class RegionalProxyCatalog
{
    // Read only the current public relay configuration. Never install or execute extension code.
    private async Task<IReadOnlyList<RegionalProxyEndpoint>> LoadMaintainedRelayAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var metadata = JsonDocument.Parse(await ReadBoundedAsync(new("https://addons.mozilla.org/api/v5/addons/addon/sound-unblocker/"),
                256 * 1024, deadline.Token).ConfigureAwait(false));
            if (!metadata.RootElement.TryGetProperty("current_version", out var version) || version.ValueKind != JsonValueKind.Object ||
                !version.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.Object ||
                !file.TryGetProperty("url", out var link) || link.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(link.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "addons.mozilla.org" ||
                uri.Port != 443 || uri.UserInfo.Length != 0 || !uri.AbsolutePath.StartsWith("/firefox/downloads/file/", StringComparison.Ordinal)) return [];
            using var archive = new ZipArchive(new MemoryStream(await ReadBoundedAsync(uri, 256 * 1024, deadline.Token).ConfigureAwait(false)));
            var entry = archive.GetEntry("background.js");
            if (entry == null || entry.Length > 16384) return [];
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return ParseMaintainedRelay(await reader.ReadToEndAsync(deadline.Token).ConfigureAwait(false));
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            AppLog.For("Pookie.RegionalPlayback").Debug("Конфигурация поддерживаемого реле недоступна: {ErrorType}", error.GetType().Name);
            return [];
        }
    }

    [GeneratedRegex("type\\s*:\\s*[\"'](http|https|socks5)[\"']\\s*,\\s*host\\s*:\\s*[\"']([0-9a-fA-F:.]+)[\"']\\s*,\\s*port\\s*:\\s*([0-9]{1,5})\\s*[,}]", RegexOptions.CultureInvariant)]
    private static partial Regex RelayConfigurationPattern();

    internal static IReadOnlyList<RegionalProxyEndpoint> ParseMaintainedRelay(string script)
    {
        if (script.Length > 16384) return [];
        var match = RelayConfigurationPattern().Match(script);
        if (!match.Success || !IPAddress.TryParse(match.Groups[2].Value, out var ip) || !IsPublicAddress(ip) ||
            !int.TryParse(match.Groups[3].Value, out var port) || port is < 1 or > 65535) return [];
        return [new(new UriBuilder(match.Groups[1].Value, ip.ToString(), port).Uri, "", 0, MaintainedRelay: true)];
    }
}
