using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Hosting;
using Pookie.App.Playback;
using Pookie.App.Storage;
using Pookie.Audio;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    internal static async Task VerifyProtectedAudioAsync(string[] urls, bool direct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This check uses Media Foundation.");
        if (!urls.All(SoundCloudWebClient.IsSoundCloudUrl)) throw new ArgumentException("Expected SoundCloud track URLs.");
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? throw new InvalidOperationException("No saved SoundCloud session for the playback check.");
        Task? verification = null;
        await AppHost.RunAsync(new()
        {
            RequireSignIn = direct, SkipSessionRestore = !direct, SilentAudio = true,
            DiscordPresence = false, SystemMediaSession = false, IsolatedData = true
        }, window =>
        {
            if (direct)
            {
                using var isolatedVault = SessionVault.Open(window.dataPaths);
                isolatedVault.Save(account);
            }
            window.Window.Loaded += () => verification = window.VerifyProtectedAudioAsync(urls, account, direct);
        });
        await (verification ?? throw new InvalidOperationException("The playback check did not start."));
    }

    private async Task VerifyProtectedAudioAsync(string[] urls, WebSession account, bool direct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            NativeBrowserSession connected;
            EmbeddedWebWorker worker;
            if (direct)
            {
                while (startupSplash.IsVisible) await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
                if (!signedIn.Value || api.Transport is not SoundCloudHttpTransport || browser is not { Embedded: { } embedded })
                    throw new InvalidOperationException("Pookie did not restore the saved account through native HTTP.");
                connected = browser;
                worker = embedded;
                if (worker.DiagnosticInstance != "uninitialized")
                    throw new InvalidOperationException("WebView2 started while restoring the saved session.");
            }
            else
            {
                var profile = Path.Combine(dataPaths.Browser, "ProtectedAudioProbe");
                connected = new(account, profilePath: profile);
                worker = new(Window, account, profilePath: profile);
                connected.UseEmbedded(worker);
                AttachBrowser(connected, useNativeHttp: false);
                signedIn.Value = true;
            }
            var browserAudioEvents = 0;
            connected.Changed += message =>
            {
                if (message.Audio != null) Interlocked.Increment(ref browserAudioEvents);
            };
            preferences = preferences with { SendPlaybackHistory = false };
            var audioPlayer = player as WindowsPlayReadyAudioPlayer ?? throw new InvalidOperationException("Pookie is not using its native Windows player.");
            audioPlayer.Volume(15);
            var uiThread = Environment.CurrentManagedThreadId;
            foreach (var url in urls)
            {
                var track = await api.ResolveAsync(url, timeout.Token);
                if (!direct)
                {
#if WINDOWS
                    if (worker.View.WebViewWasShown) throw new InvalidOperationException("WebView2 was shown while resolving the track.");
#endif
                    await worker.ResetAsync(); // Start playback with a cold API worker.
                }
                SetQueue(track);
                await PlayAsync(track).WaitAsync(timeout.Token);
                await WaitForStateAsync(state => state is { Playing: true, Position: >= .5 });
                if (!audioPlayer.HasProtectedSession) throw new InvalidOperationException($"Track {track.Id} did not use PlayReady.");

                audioPlayer.Pause(true);
                await WaitForStateAsync(state => !state.Playing && !state.Buffering);
                var target = audioPlayer.Poll().Duration * .7;
                await audioPlayer.SeekAsync(target, timeout.Token);
                if (audioPlayer.Poll() is not { Playing: false } paused || Math.Abs(paused.Position - target) > 1)
                    throw new InvalidOperationException("The paused PlayReady seek did not settle at its destination.");
                audioPlayer.Pause(false);
                await WaitForStateAsync(state => state.Playing && state.Position >= target + .3 && state.BufferedEnd > state.Position);
                await audioPlayer.SeekAsync(5, timeout.Token);
                await WaitForStateAsync(state => state.Playing && state.Position is > 5.2 and < 8);
                if (Volatile.Read(ref browserAudioEvents) != 0) throw new InvalidOperationException("The browser handled an audio command.");
                if (direct)
                {
                    if (worker.DiagnosticInstance != "uninitialized")
                        throw new InvalidOperationException("WebView2 started during protected playback.");
                }
#if WINDOWS
                else if (worker.View.WebViewWasShown) throw new InvalidOperationException("WebView2 was shown during protected playback.");
#endif
                var browserState = direct ? "WebView uninitialized" : "no WebView presentation or audio events";
                Console.WriteLine($"NATIVE_PLAYREADY_UI_OK: track {track.Id}; process {Environment.ProcessId}, UI thread {uiThread}; buffering, paused forward seek and backward seek; {browserState}");
            }
            await audioPlayer.StopAsync();
            if (audioPlayer.HasProtectedSession || audioPlayer.Poll().Playing)
                throw new InvalidOperationException("PlayReady did not stop.");

            async Task WaitForStateAsync(Func<AudioState, bool> condition)
            {
                using var samples = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
                while (!condition(audioPlayer.Poll())) await samples.WaitForNextTickAsync(timeout.Token);
            }
        }
        finally { Window.Close(); }
    }
}
