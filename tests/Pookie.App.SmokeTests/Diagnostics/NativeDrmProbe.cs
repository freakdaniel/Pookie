using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.Audio;
using Pookie.SoundCloud;
#if WINDOWS
using Windows.Media.Protection.PlayReady;
#endif

namespace Pookie.App.Diagnostics;

// Acquisition-only experiment. No decoder changes, extracted keys or saved
// license/challenge payloads. A successful license does not prove playback.
internal static class NativeDrmProbe
{
    public static async Task RunAsync(string url)
    {
#if WINDOWS
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? throw new InvalidOperationException("A saved Pookie session is required.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        ResolvedPlayback resolved;
        await using (var browser = new NativeBrowserSession(account,
            profilePath: Path.GetFullPath("artifacts/drm-probe-native-investigation")))
        {
            var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
            var track = await api.ResolveAsync(url, stop.Token);
            resolved = await new SoundCloudStreamResolver(api).ResolveAsync(track.Id, stop.Token, ["ctr-encrypted-hls"]);
        }
        using var response = await http.GetAsync(resolved.Stream.Uri, stop.Token);
        response.EnsureSuccessStatusCode();
        var manifest = System.Text.Encoding.UTF8.GetString(await HttpRangeStream.ReadBoundedAsync(response.Content, 1024 * 1024, stop.Token));
        byte[]? playready = null;
        foreach (var line in manifest.Split('\n').Where(line => line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal)))
        {
            var format = Regex.Match(line, "KEYFORMAT=\"([^\"]+)\"").Groups[1].Value;
            Console.WriteLine($"NATIVE_DRM_MANIFEST: widevine={format == "urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed"}; playready={format is "com.microsoft.playready" or "urn:uuid:9a04f079-9840-4286-ab92-e65be0885f95"}");
            if (format is not ("com.microsoft.playready" or "urn:uuid:9a04f079-9840-4286-ab92-e65be0885f95")) continue;
            var uri = Regex.Match(line, "URI=\"([^\"]+)\"").Groups[1].Value;
            if (!uri.StartsWith("data:", StringComparison.Ordinal) || !uri.Contains(";base64,", StringComparison.Ordinal))
                throw new InvalidDataException("Only inline PlayReady init data is supported by the probe.");
            playready = Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);
        }
        if (playready == null) { Console.WriteLine("NATIVE_PLAYREADY_NO_INIT_DATA: this playlist has no PlayReady declaration"); Environment.ExitCode = 1; return; }
        var stage = "init-data";
        try
        {
            if (playready.Length >= 32 && playready.AsSpan(4, 4).SequenceEqual("pssh"u8))
            {
                var offset = 28;
                if (playready[8] == 1) offset = checked(offset + 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(playready.AsSpan(28, 4)) * 16);
                var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(playready.AsSpan(offset, 4)));
                playready = playready.AsSpan(offset + 4, size).ToArray();
            }
            // Inspect the embedded header record separately from the HLS
            // PlayReady Object's length/record envelope.
            if (playready.Length >= 10 && BinaryPrimitives.ReadUInt32LittleEndian(playready) == playready.Length &&
                BinaryPrimitives.ReadUInt16LittleEndian(playready.AsSpan(4)) == 1 &&
                BinaryPrimitives.ReadUInt16LittleEndian(playready.AsSpan(6)) == 1)
            {
                var size = BinaryPrimitives.ReadUInt16LittleEndian(playready.AsSpan(8));
                playready = playready.AsSpan(10, size).ToArray();
            }
            Console.WriteLine($"NATIVE_PLAYREADY_HEADER: bytes={playready.Length}; utf16_xml={playready.Length > 2 && playready[0] == 60 && playready[1] == 0}; utf8_xml={playready.Length > 2 && playready[0] == 60 && playready[1] != 0}");
            stage = "platform";
            Console.WriteLine($"NATIVE_PLAYREADY_PLATFORM: security_version={PlayReadyStatics.PlayReadySecurityVersion}");
            stage = "content-header";
            var header = new PlayReadyContentHeader(playready);
            stage = "request-create";
            var request = new PlayReadyLicenseAcquisitionServiceRequest { ContentHeader = header };
            Console.WriteLine($"NATIVE_PLAYREADY_AVAILABLE: security_version={PlayReadyStatics.PlayReadySecurityVersion}");
            stage = "challenge";
            var challenge = request.GenerateManualEnablingChallenge();
            var payload = challenge.GetMessageBody();
            try
            {
                using var license = new HttpRequestMessage(HttpMethod.Post,
                    "https://license.media-streaming.soundcloud.cloud/playback/playready?license_token=" + Uri.EscapeDataString(resolved.Stream.LicenseAuthToken!));
                license.Headers.Referrer = new("https://soundcloud.com/");
                license.Headers.TryAddWithoutValidation("Origin", "https://soundcloud.com");
                license.Headers.TryAddWithoutValidation("X-SC-Application-Id", "46941");
                license.Content = new ByteArrayContent(payload);
                foreach (var pair in challenge.MessageHeaders)
                    if (!license.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value.ToString()))
                        license.Headers.TryAddWithoutValidation(pair.Key, pair.Value.ToString());
                using var result = await http.SendAsync(license, stop.Token);
                Console.WriteLine($"NATIVE_PLAYREADY_LICENSE_HTTP: {(int)result.StatusCode}; credentials redacted");
                if (!result.IsSuccessStatusCode) { _ = await DrmLicenseFailure.ReadAsync(result, stop.Token); Environment.ExitCode = 1; return; }
                var body = await HttpRangeStream.ReadBoundedAsync(result.Content, 65536, stop.Token);
                try
                {
                    var failure = request.ProcessManualEnablingResponse(body);
                    if (failure != null) throw failure;
                    Console.WriteLine("NATIVE_PLAYREADY_LICENSE_ACCEPTED: license only; playback and PCM not verified");
                }
                finally { CryptographicOperations.ZeroMemory(body); }
            }
            finally { CryptographicOperations.ZeroMemory(payload); }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Console.WriteLine($"NATIVE_PLAYREADY_FAILED: stage={stage}; type={error.GetType().Name}; hresult=0x{error.HResult:X8}; credentials redacted");
            Environment.ExitCode = 1;
        }
        finally { CryptographicOperations.ZeroMemory(playready); }
#else
        await Task.FromException(new PlatformNotSupportedException("This diagnostic requires Windows PlayReady."));
#endif
    }
}
