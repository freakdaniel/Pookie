using System.Net;
using Pookie.App.Auth;
using Pookie.Audio;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class DiagnosticsRunner
{
    public static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == 2 && args[0] == "--clipboard-fixture") { ClipboardFixture.Run(args[1]); return true; }
        if (args.Contains("--storage-smoke-test")) { await StorageSmokeTest.RunAsync(); return true; }
        if (args.Contains("--browser-persistence-smoke-test")) { await BrowserPersistenceSmokeTest.RunAsync(); return true; }
        if (args.Contains("--login-smoke-test")) { await LoginSmokeTest.RunAsync(); return true; }
        if (args.Contains("--browser-worker-smoke-test")) { await BrowserWorkerSmokeTest.RunAsync(); return true; }
        if (args.Contains("--protected-audio-smoke-test"))
        {
            var flag = Array.IndexOf(args, "--protected-audio-smoke-test");
            if (flag + 1 >= args.Length) throw new ArgumentException("После --protected-audio-smoke-test нужна ссылка на трек SoundCloud.");
            using var vault = new LinuxSessionVault();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var protectedHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            var protectedAccount = vault.Load();
            await using var protectedBrowser = protectedAccount != null ? new NativeBrowserSession(protectedAccount) : null;
            var protectedApi = new SoundCloudWebClient(protectedHttp)
                { Session = protectedAccount, BrowserTransport = protectedBrowser, RequireBrowserTransport = true };
            var track = await protectedApi.ResolveAsync(args[flag + 1], timeout.Token);
            var resolved = await new SoundCloudStreamResolver(protectedApi).ResolveAsync(track.Id, timeout.Token, ["ctr-encrypted-hls"]);
            Console.WriteLine($"PROTECTED_STREAM_OK: track {track.Id}; {resolved.Stream.Protocol}; credentials redacted");
            await using var player = new SoundFlowPlayer(silent: true, SoundCloudWebClient.IsMediaUri);
            player.Volume(0);
            var protectedSource = new AudioSource(resolved.Stream.Uri.AbsoluteUri, AudioTransport.WidevineHls, resolved.Stream.Duration)
                { LicenseAuthToken = resolved.Stream.LicenseAuthToken };
            await player.PlayAsync(protectedSource, timeout.Token);
            await WaitForPlaybackAsync(player);
            if (Math.Abs(player.Poll().Duration - resolved.Stream.Duration) > 2) throw new InvalidOperationException("DRM duration mismatch.");
            await player.SeekAsync(Math.Min(30, resolved.Stream.Duration / 2), timeout.Token);
            await WaitForPlaybackAsync(player);
            if (player.Poll().Position < Math.Min(30, resolved.Stream.Duration / 2) - .2) throw new InvalidOperationException("DRM seek failed.");
            player.Pause(true); await Task.Delay(150, timeout.Token);
            if (player.Poll().Playing) throw new InvalidOperationException("DRM pause failed.");
            player.Pause(false); await WaitForPlaybackAsync(player);
            await player.SeekAsync(Math.Max(0, resolved.Stream.Duration - 1.5), timeout.Token);
            while (!player.Poll().Ended) await Task.Delay(50, timeout.Token);
            if (player.Poll().Ended) throw new InvalidOperationException("DRM end signaled twice.");
            await player.PlayAsync(protectedSource, timeout.Token);
            await WaitForPlaybackAsync(player);
            player.Stop();
            if (player.Poll().Playing || player.Poll().Position != 0) throw new InvalidOperationException("DRM stop failed.");
            Console.WriteLine("PROTECTED_AUDIO_SMOKE_OK: real SoundCloud license, native CDM decryption, AAC decode, full duration, seek, pause, resume, end, restart and stop; no browser");
            return true;
        }
        if (args.Contains("--session-smoke-test"))
        {
            using var vault = new LinuxSessionVault();
            var account = vault.Load();
            if (account == null)
            {
                Console.WriteLine("SESSION_SMOKE_SKIPPED: no saved Pookie account; protected request was not sent");
                return true;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using var browser = new NativeBrowserSession(account);
            browser.Changed += message => Console.WriteLine($"BROWSER_EVENT: {message.Kind}; interactive={message.Interactive}; type={message.ChallengeType}");
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            var web = new SoundCloudWebClient(client) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
            try
            {
                var user = await browser.GetMeAsync(timeout.Token);
                Console.WriteLine("SESSION_PROFILE_OK: saved account accepted inside live WebView");
                var liked = await web.GetLikesAsync(user.Id, timeout.Token);
                var track = liked.Tracks.First();
                // Idempotent PUT verifies the protected endpoint without removing an existing like.
                await browser.SetLikedAsync(user.Id, track.Id, true, timeout.Token);
                Console.WriteLine("SESSION_SMOKE_OK: real protected like PUT accepted inside persistent WebView, original like preserved");
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                Console.WriteLine("SESSION_SMOKE_UNCONFIRMED: website check did not finish within 90 seconds; protected write not confirmed");
                Environment.ExitCode = 2;
            }
            return true;
        }
        if (args.Contains("--audio-smoke-test"))
        {
            var file = DemoAudio.Create();
            try
            {
                await using var player = new SoundFlowPlayer(silent: !args.Contains("--system-audio"));
                player.Volume(0);
                await player.PlayAsync(new(file, AudioTransport.File));
                await WaitForPlaybackAsync(player);
                if (player.Poll().Duration < 10) throw new InvalidOperationException("Некорректная длительность WAV.");
                await player.SeekAsync(4);
                await WaitForPlaybackAsync(player);
                if (player.Poll().Position < 3.8) throw new InvalidOperationException("Перемотка не сработала.");
                player.Pause(true); await Task.Delay(150);
                if (player.Poll().Playing) throw new InvalidOperationException("Пауза не сработала.");
                player.Volume(0);
                player.Pause(false); await WaitForPlaybackAsync(player);
                player.Stop();
                if (player.Poll().Playing || player.Poll().Position != 0) throw new InvalidOperationException("Остановка не сработала.");
                Console.WriteLine("AUDIO_SMOKE_OK: SoundFlow WAV playback, duration, seek, pause, resume and stop; bundled native libraries");
            }
            finally { File.Delete(file); }
            return true;
        }
        if (!args.Contains("--web-smoke-test")) return false;
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (Environment.GetEnvironmentVariable("POOKIE_PROXY") is { Length: > 0 } proxy) handler.Proxy = new WebProxy(proxy);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var api = new SoundCloudWebClient(http);
        var page = await api.SearchAsync("field recording birds");
        Console.WriteLine($"WEB_SEARCH_OK: {page.Tracks.Length} tracks; no registered-app credentials");
        SoundCloudStream? stream = null;
        foreach (var track in page.Tracks.Take(8))
        {
            try { stream = await api.GetStreamAsync(track, protocol: args.Contains("--hls") ? "hls" : null); break; }
            catch (SoundCloudException) { }
        }
        if (stream == null) throw new InvalidOperationException("Не найден доступный полный поток для проверки.");
        Console.WriteLine($"WEB_STREAM_OK: {stream.Protocol} stream resolved (URL redacted)");
        if (args.Contains("--with-audio"))
        {
            await using var player = new SoundFlowPlayer(silent: true, SoundCloudWebClient.IsMediaUri);
            await player.PlayAsync(new(stream.Uri.AbsoluteUri, stream.Protocol == "hls" ? AudioTransport.Hls : AudioTransport.Progressive, stream.Duration));
            await WaitForPlaybackAsync(player);
            await player.SeekAsync(Math.Min(10, stream.Duration / 2));
            await WaitForPlaybackAsync(player);
            Console.WriteLine("WEB_AUDIO_OK: real SoundCloud stream decoded, playing and seeking with SoundFlow");
        }
        return true;
    }

    private static async Task WaitForPlaybackAsync(IAudioPlayer player)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (true)
        {
            await Task.Delay(100, timeout.Token);
            var state = player.Poll();
            if (state.Playing && state.Position > .1) return;
        }
    }
}
