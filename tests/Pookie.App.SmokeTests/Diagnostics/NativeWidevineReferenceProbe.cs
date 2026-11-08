using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Pookie.Audio;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Structs;

namespace Pookie.App.Diagnostics;

internal static class NativeWidevineReferenceProbe
{
    private static readonly Uri Demo = new("https://storage.googleapis.com/shaka-demo-assets/angel-one-widevine-hls/");
    private static readonly Uri LicenseServer = new("https://cwip-shaka-proxy.appspot.com/no_auth");

    internal static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var token = timeout.Token;
        var manifest = new Uri(Demo, "playlist_a-eng-0128k-aac-2c.mp4.m3u8");
        var text = Encoding.UTF8.GetString(await AudioHttp.ReadAsync(http, manifest, 1024 * 1024, token));
        var playlist = ProtectedHlsPlaylist.Parse(text, manifest, uri => Demo.IsBaseOf(uri));
        var initialization = CencAudioInitialization.Parse(await AudioHttp.ReadAsync(http, playlist.Initialization, 1024 * 1024, token));
        // Angel One starts with two clear segments. Check a protected one so that
        // a successful decode necessarily exercised the CDM and its license.
        var fragment = playlist.Segments.Single(segment => segment.Uri == new Uri(Demo, "a-eng-0128k-aac-2c-s3.mp4"));
        var encrypted = await AudioHttp.ReadAsync(http, fragment.Uri, 16 * 1024 * 1024, token);
        using var session = await WidevineSession.OpenAsync(http, playlist.InitData,
            () => new HttpRequestMessage(HttpMethod.Post, LicenseServer), token);
        Console.WriteLine($"NATIVE_WIDEVINE_REFERENCE_LICENSE_OK: {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; CDM interface {CdmAbi.Version}");
        var audio = CencAudioFragment.DecryptAdts(encrypted, initialization, session, token);
        var pcm = new float[8192];
        try
        {
            using var stream = new MemoryStream(audio, writable: false);
            using var decoder = new FFmpegCodecFactory().TryCreateDecoder(stream, out var format, AudioFormat.DvdHq)
                ?? throw new InvalidOperationException("The reference AAC fragment could not be decoded.");
            var samples = 0;
            var peak = 0f;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var count = decoder.Decode(pcm);
                if (count <= 0) break;
                samples += count;
                for (var i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs(pcm[i]));
            }
            if (samples < format.SampleRate * format.Channels || !float.IsFinite(peak) || peak <= .001f)
                throw new InvalidOperationException("The protected reference fragment did not yield non-silent PCM.");
            Console.WriteLine($"NATIVE_WIDEVINE_REFERENCE_PCM_OK: samples={samples}; rate={format.SampleRate}; channels={format.Channels}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(audio);
            Array.Clear(pcm);
        }
    }
}
