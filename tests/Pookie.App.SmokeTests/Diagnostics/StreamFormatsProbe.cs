using System.Globalization;
using System.Text;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.Audio;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class StreamFormatsProbe
{
    internal static async Task RunAsync(string url)
    {
        if (!SoundCloudWebClient.IsSoundCloudUrl(url)) throw new ArgumentException("Expected a SoundCloud track URL.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? throw new InvalidOperationException("A saved Pookie session is required.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        await using var browser = new NativeBrowserSession(account,
            profilePath: Path.GetFullPath("artifacts/stream-formats-" + Guid.NewGuid().ToString("N")));
        var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
        var resolved = await api.ResolveAsync(url, token);
        PrintMetadata("resolve", resolved);
        // The production playback resolver fetches /tracks/{id}, whose format
        // list can differ from /resolve and the website hydration data.
        var track = await api.GetTrackAsync(resolved.Id, token);
        PrintMetadata("tracks/id", track);
        Console.WriteLine($"STREAM_FORMATS_TRACK: id={track.Id}; duration={track.DurationSeconds:F3}; credentials omitted");
        foreach (var transcoding in track.Media?.Transcodings ?? [])
        {
            var label = $"protocol={Label(transcoding.Format?.Protocol)}; preset={Label(transcoding.Preset)}; snipped={transcoding.Snipped}";
            try
            {
                var stream = await api.ResolveTranscodingAsync(track, transcoding, token);
                if (stream == null) { Console.WriteLine($"STREAM_FORMAT: {label}; resolved=false"); continue; }
                if (stream.Protocol == "progressive")
                {
                    Console.WriteLine($"STREAM_FORMAT: {label}; resolved=true; protected=false");
                    continue;
                }
                using var response = await http.GetAsync(stream.Uri, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"STREAM_FORMAT: {label}; manifest_http={(int)response.StatusCode}");
                    continue;
                }
                var text = Encoding.UTF8.GetString(await HttpRangeStream.ReadBoundedAsync(response.Content, 1024 * 1024, token));
                var lines = text.Split('\n').Select(line => line.Trim()).ToArray();
                var keys = lines.Where(line => line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal)).ToArray();
                var widevine = keys.Any(line => line.Contains("urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed", StringComparison.Ordinal));
                var playready = keys.Any(line => line.Contains("urn:uuid:9a04f079-9840-4286-ab92-e65be0885f95", StringComparison.Ordinal) || line.Contains("com.microsoft.playready", StringComparison.Ordinal));
                var fairplay = keys.Any(line => line.Contains("com.apple.streamingkeydelivery", StringComparison.Ordinal));
                var identity = keys.Any(line => !line.Contains("KEYFORMAT=", StringComparison.Ordinal) || line.Contains("KEYFORMAT=\"identity\"", StringComparison.Ordinal));
                var duration = lines.Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal)).Sum(line =>
                    double.TryParse(line[8..].Split(',')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0);
                // Print classification only, never signed URLs, key URIs, PSSH or license tokens.
                Console.WriteLine($"STREAM_FORMAT: {label}; resolved=true; protected={stream.Protected}; manifest_duration={duration:F3}; widevine={widevine}; playready={playready}; fairplay={fairplay}; identity={identity}; aes128={keys.Any(line => line.Contains("METHOD=AES-128", StringComparison.Ordinal))}");
            }
            catch (SoundCloudException error)
            {
                Console.WriteLine($"STREAM_FORMAT: {label}; resolve_http={error.StatusCode}");
            }
        }
        Console.WriteLine("STREAM_FORMATS_PROBE_OK: advertised formats checked; no CDM, license request or playback history used");
    }

    private static string Label(string? text) => string.IsNullOrEmpty(text) ? "unknown" :
        new string(text.Take(80).Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());

    private static void PrintMetadata(string source, SoundCloudTrack track)
    {
        var formats = track.Media?.Transcodings ?? [];
        Console.WriteLine($"STREAM_METADATA: source={source}; id={track.Id}; formats={formats.Length}; ordinary_protocols={formats.Count(format => format.Format?.Protocol is "hls" or "progressive")}");
        foreach (var format in formats)
            Console.WriteLine($"STREAM_METADATA_FORMAT: source={source}; protocol={Label(format.Format?.Protocol)}; preset={Label(format.Preset)}; snipped={format.Snipped}");
    }
}
