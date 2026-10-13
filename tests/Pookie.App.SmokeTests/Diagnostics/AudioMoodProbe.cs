using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Playback;
using Pookie.App.Storage;
using Pookie.Audio;
using Pookie.Recommendations;
using Pookie.Recommendations.Audio;
using Pookie.SoundCloud;
using System.Text.Json;

namespace Pookie.App.Diagnostics;

internal static class AudioMoodProbe
{
    internal static async Task RunAsync(string[] urls, bool cacheResults = false, bool preview = false)
    {
        if (urls.Length == 0) throw new ArgumentException("Provide a SoundCloud URL.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var vault = SessionVault.Open(new AppDataPaths());
        var saved = vault.Load() ?? throw new InvalidOperationException("The mood probe requires a saved session.");
        await using var browser = new NativeBrowserSession(saved);
        using var http = new HttpClient();
        var api = new SoundCloudWebClient(http) { Session = saved, BrowserTransport = browser };
        using var encoder = new MusicnnEncoder();
        var account = cacheResults ? (await browser.GetMeAsync(timeout.Token)).Id : 0;
        using var cache = cacheResults ? new Pookie.Recommendations.Storage.ListeningEventStore(
            Path.Combine(new AppDataPaths().Root, "Wave", "events.sqlite3")) : null;
        foreach (var url in urls)
        {
            var track = await api.ResolveAsync(url, timeout.Token);
            var resolved = await new SoundCloudStreamResolver(api).ResolveAsync(track.Id, timeout.Token, ["progressive", "hls", "ctr-encrypted-hls"]);
            var stream = resolved.Stream;
            var source = new AudioSource(stream.Uri.AbsoluteUri,
                stream.Protected ? AudioTransport.WidevineHls : stream.Protocol == "hls" ? AudioTransport.Hls : AudioTransport.Progressive, stream.Duration)
                { CaptureAnalysis = true, BrowserPage = track.PermalinkUrl, LicenseAuthToken = stream.LicenseAuthToken };
            AudioSample? sample;
            var candidatePreview = preview && !stream.Protected;
            if (candidatePreview) sample = await AudioPreviewSampler.CaptureAsync(http, source, SoundCloudWebClient.IsMediaUri, timeout.Token);
            else
            {
                IAudioPlayer audio = new SoundFlowPlayer(silent: true, SoundCloudWebClient.IsMediaUri);
                if (OperatingSystem.IsWindows()) audio = new WindowsAudioPlayer(audio, () => browser);
                await using var player = audio;
                player.Volume(0);
                await player.PlayAsync(source, timeout.Token);
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                while (player.Poll().Position < 9) await timer.WaitForNextTickAsync(timeout.Token);
                sample = player.TakeAnalysisSample();
                await player.StopAsync();
            }
            if (sample == null) throw new InvalidOperationException("No decoded analysis sample.");
            var vector = encoder.Encode(sample) ?? throw new InvalidOperationException("No usable audio features.");
            cache?.PutVector(account, Pookie.App.Home.WaveTrack.From(resolved.Track), vector, DateTimeOffset.UtcNow);
            // Only public track identity and analysis scores are printed; stream/session credentials are omitted.
            Console.WriteLine("AUDIO_MOOD_PROBE: " + JsonSerializer.Serialize(new { track.Id, track.Title, stream.Protocol, CandidatePreview = candidatePreview, Samples = sample.Mono.Length,
                vector.Mood, EnergyFit = vector.Mood!.Match(WaveMood.Energetic), CalmFit = vector.Mood.Match(WaveMood.Calm) }));
        }
    }
}
