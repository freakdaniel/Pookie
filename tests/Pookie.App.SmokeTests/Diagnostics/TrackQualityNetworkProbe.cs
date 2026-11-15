using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class TrackQualityNetworkProbe
{
    internal static async Task RunAsync(string url)
    {
        if (!SoundCloudWebClient.IsSoundCloudUrl(url)) throw new ArgumentException("Expected a SoundCloud track URL.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? throw new InvalidOperationException("A saved Pookie session is required.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        await using var browser = new NativeBrowserSession(account,
            profilePath: Path.GetFullPath("artifacts/track-quality-" + Guid.NewGuid().ToString("N")));
        var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
        var track = await api.ResolveAsync(url, timeout.Token);
        var upload = await api.GetTrackUploadAsync(track.Id, timeout.Token);
        if (upload == null) throw new InvalidOperationException("SoundCloud returned no original-upload metadata.");
        Console.WriteLine($"TRACK_UPLOAD: codec={upload.Codec}; container={upload.Container}; bitrate={upload.BitRate}; sample_rate={upload.SampleRate}; channels={upload.Channels}; lossless={upload.IsLossless}");
        using var native = new SoundCloudHttpTransport(http, () => browser.Account, browser);
        var nativeApi = new SoundCloudWebClient(http) { Session = browser.Account, Transport = native };
        var nativeUpload = await nativeApi.GetTrackUploadAsync(track.Id, timeout.Token);
        if (upload != nativeUpload) throw new InvalidOperationException("Browser and native original-upload metadata differ.");
        Console.WriteLine("TRACK_QUALITY_NETWORK_OK: matching browser/native metadata; no playback or account writes");
    }
}
