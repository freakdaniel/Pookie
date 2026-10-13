using Pookie.App.Playback;
using Pookie.App.Browser;
using System.Net;
using Pookie.App.Preview;
using Pookie.App.Auth;
using Pookie.App.Storage;
using Pookie.Audio;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class DiagnosticsRunner
{
    public static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == 2 && args[0] == "--native-drm-probe") { await NativeDrmProbe.RunAsync(args[1]); return true; }
        if (args.Length is 2 or 3 && args[0] == "--playback-history-network-probe") { await PlaybackHistoryNetworkProbe.RunAsync(args[1], args.Length == 3 ? args[2] : null); return true; }
        if (args.Length == 2 && args[0] == "--playlist-page-network-probe") { await PlaylistPageNetworkProbe.RunAsync(args[1]); return true; }
        if (args.Length == 2 && args[0] == "--track-page-network-probe") { await TrackPageNetworkProbe.RunAsync(args[1]); return true; }
        if (args.Contains("--pagination-smoke-test")) { PaginationSmokeTest.Run(); return true; }
        if (args.Contains("--waveform-gain-smoke-test")) { await WaveformGainSmokeTest.RunAsync(); return true; }
        if (args.Contains("--browser-media-isolation-smoke-test")) { await BrowserMediaIsolationSmokeTest.RunAsync(); return true; }
        if (args.Contains("--system-media-smoke-test")) { await SystemMediaSmokeTest.RunAsync(); return true; }
        if (args.Length == 2 && args[0] == "--clipboard-fixture") { ClipboardFixture.Run(args[1]); return true; }
        if (args.Length == 2 && args[0] == "--clipboard-read-fixture") { ClipboardFixture.Read(args[1]); return true; }
        if (args.Contains("--content-blocker-smoke-test")) { await ContentBlockerSmokeTest.RunAsync(); return true; }
        if (args.Contains("--startup-network-probe"))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var sessionVault = SessionVault.Open(new AppDataPaths());
            var saved = sessionVault.Load() ?? throw new InvalidOperationException("No saved SoundCloud session for the startup probe.");
            await using var session = new NativeBrowserSession(saved);
            await session.GetMeAsync(timeout.Token);
            Console.WriteLine("STARTUP_NETWORK_PROFILE_OK: saved session validated; account details omitted");
            return true;
        }
        if (args.Contains("--startup-log-smoke-test")) { await StartupLogSmokeTest.RunAsync(); return true; }
        if (args.Contains("--storage-smoke-test")) { await StorageSmokeTest.RunAsync(); return true; }
        if (args.Contains("--session-vault-smoke-test")) { await SessionVaultSmokeTest.RunAsync(); return true; }
        if (args.Contains("--browser-persistence-smoke-test")) { await BrowserPersistenceSmokeTest.RunAsync(); return true; }
        if (args.Contains("--login-smoke-test")) { await LoginSmokeTest.RunAsync(); return true; }
        if (args.Contains("--login-handoff-smoke-test")) { await LoginHandoffSmokeTest.RunAsync(); return true; }
        if (args.Contains("--browser-worker-smoke-test")) { await BrowserWorkerSmokeTest.RunAsync(); return true; }
        if (args.Contains("--browser-audio-state-smoke-test")) { await BrowserAudioStateSmokeTest.RunAsync(); return true; }
        if (args.Contains("--browser-audio-capture-smoke-test")) { await BrowserAudioCaptureSmokeTest.RunAsync(); return true; }
        if (args.Contains("--browser-drm-capability-probe")) { await BrowserDrmCapabilityProbe.RunAsync(); return true; }
        if (args.Contains("--protected-audio-smoke-test"))
        {
            var flag = Array.IndexOf(args, "--protected-audio-smoke-test");
            if (flag + 1 >= args.Length) throw new ArgumentException("После --protected-audio-smoke-test нужна ссылка на трек SoundCloud.");
            // A preserved-profile login, browser API resolution and DRM exchange are separate network stages.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var vault = SessionVault.Open(new AppDataPaths());
            using var protectedHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            var protectedAccount = vault?.Load() ?? await NativeWebLogin.ConnectAsync(timeout.Token, resetSession: false);
            var diagnosticProfile = args.Contains("--isolated-browser-profile")
                ? Path.Combine(Path.GetFullPath("artifacts"), "drm-probe-" + Guid.NewGuid().ToString("N")) : null;
            var profileFlag = Array.IndexOf(args, "--browser-profile");
            if (profileFlag >= 0 && profileFlag + 1 < args.Length) diagnosticProfile = Path.GetFullPath(args[profileFlag + 1]);
            await using var protectedBrowser = new NativeBrowserSession(protectedAccount, profilePath: diagnosticProfile);
            string? audioStage = null;
            if (Environment.GetEnvironmentVariable("POOKIE_DRM_DIAGNOSTICS") == "1")
                protectedBrowser.Changed += message =>
                {
                    if (message.Kind == "audio-state" && message.Audio?.Error is { } error)
                        Console.WriteLine($"DRM_BROWSER_AUDIO_ERROR: {error}; stage={message.Audio.Stage}; media={message.Audio.MediaError}; ready={message.Audio.ReadyState}; network={message.Audio.NetworkState}; license_http={message.Audio.LicenseHttpStatus}; message_type={message.Audio.LicenseMessageType}; cdm_error={message.Audio.CdmError}; license_failure={message.Audio.LicenseFailure}; credentials redacted");
                    else if (message.Kind == "audio-state" && message.Audio is { Stage: { } stage } && stage != audioStage)
                    {
                        audioStage = stage;
                        Console.WriteLine($"DRM_BROWSER_AUDIO_STAGE: {stage}; credentials redacted");
                    }
                    else if (message.Kind is not ("json-chunk" or "audio-state" or "audio-sample"))
                        Console.WriteLine($"DRM_BROWSER_EVENT: {message.Kind}; http={message.Status}; interactive={message.Interactive}");
                };
            var protectedApi = new SoundCloudWebClient(protectedHttp)
                { Session = protectedAccount, BrowserTransport = protectedBrowser, RequireBrowserTransport = true };
            SoundCloudTrack? track = null;
            if (args[flag + 1] == "history")
            {
                var user = await protectedBrowser.GetMeAsync(timeout.Token);
                var history = await protectedApi.GetLibraryAsync("history", user.Id, timeout.Token);
                foreach (var candidate in history.Items.Select(item => item.Track).OfType<SoundCloudTrack>().Take(20))
                {
                    var detail = await protectedApi.GetTrackAsync(candidate.Id, timeout.Token);
                    if (detail.Access is "blocked" or "preview" || detail.Policy is "BLOCK" or "SNIP") continue;
                    if (detail.Media?.Transcodings?.Any(t => !t.Snipped && t.Format?.Protocol == "ctr-encrypted-hls") == true)
                    { track = detail; break; }
                }
                if (track == null) throw new InvalidOperationException("The recent history has no accessible protected track for the probe.");
            }
            else track = await protectedApi.ResolveAsync(args[flag + 1], timeout.Token);
            Console.WriteLine($"PROTECTED_TRACK_OK: track {track.Id}; credentials redacted");
            var resolved = await new SoundCloudStreamResolver(protectedApi).ResolveAsync(track.Id, timeout.Token,
                args.Contains("--default-stream-selection") ? null : ["ctr-encrypted-hls"]);
            Console.WriteLine($"PROTECTED_STREAM_OK: track {track.Id}; {resolved.Stream.Protocol}; credentials redacted");
            if (!resolved.Stream.Protected)
            {
                Console.WriteLine("STREAM_SELECTION_PROBE_OK: production resolver selected an ordinary stream; DRM playback not attempted");
                return true;
            }
            if (args.Contains("--drm-service-certificate"))
            {
                await protectedBrowser.DisposeAsync();
                using var manifestResponse = await protectedHttp.GetAsync(resolved.Stream.Uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                manifestResponse.EnsureSuccessStatusCode();
                var manifest = System.Text.Encoding.UTF8.GetString(await HttpRangeStream.ReadBoundedAsync(manifestResponse.Content, 1024 * 1024, timeout.Token));
                var playlist = ProtectedHlsPlaylist.Parse(manifest, resolved.Stream.Uri, SoundCloudWebClient.IsMediaUri);
                if (args.Contains("--drm-service-certificate"))
                {
                    using var certificateRequest = new HttpRequestMessage(HttpMethod.Post,
                        "https://license.media-streaming.soundcloud.cloud/playback/widevine?license_token=" + Uri.EscapeDataString(resolved.Stream.LicenseAuthToken!));
                    certificateRequest.Headers.Referrer = new("https://soundcloud.com/");
                    certificateRequest.Headers.TryAddWithoutValidation("Origin", "https://soundcloud.com");
                    certificateRequest.Headers.TryAddWithoutValidation("X-SC-Application-Id", "46941");
                    // Widevine's service-certificate request, not a content license or media key.
                    certificateRequest.Content = new ByteArrayContent([8, 4]);
                    certificateRequest.Content.Headers.ContentType = new("application/octet-stream");
                    using var certificateResponse = await protectedHttp.SendAsync(certificateRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    Console.WriteLine($"DRM_SERVICE_CERTIFICATE_HTTP: http={(int)certificateResponse.StatusCode}; credentials redacted");
                    certificateResponse.EnsureSuccessStatusCode();
                    var envelope = await HttpRangeStream.ReadBoundedAsync(certificateResponse.Content, 65536, timeout.Token);
                    byte[]? certificate = null;
                    try
                    {
                        certificate = UnwrapServiceCertificate(envelope);
                        using var session = await WidevineSession.OpenAsync(protectedHttp, resolved.Stream.LicenseAuthToken!, playlist.InitData, timeout.Token, certificate);
                        Console.WriteLine("DRM_NATIVE_CERTIFICATE_LICENSE_OK: native CDM reported usable keys; playback not tested");
                    }
                    finally
                    {
                        if (certificate != null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(certificate);
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(envelope);
                    }
                    return true;
                }
            }
            IAudioPlayer protectedPlayer = new SoundFlowPlayer(silent: true, SoundCloudWebClient.IsMediaUri);
            if (OperatingSystem.IsWindows() && !args.Contains("--drm-native-cdm"))
                protectedPlayer = new WindowsAudioPlayer(protectedPlayer, () => protectedBrowser);
            await using var player = protectedPlayer;
            player.Volume(args.Contains("--system-audio") ? 15 : 0);
            var protectedSource = new AudioSource(resolved.Stream.Uri.AbsoluteUri, AudioTransport.WidevineHls, resolved.Stream.Duration)
                { LicenseAuthToken = resolved.Stream.LicenseAuthToken, CaptureAnalysis = args.Contains("--analysis-probe"), BrowserPage = track.PermalinkUrl,
                  NormalizationGainDb = args.Contains("--normalization-probe")
                    ? await new WaveformGainResolver(protectedHttp, new AppDataPaths()).ResolveAsync(track.WaveformUrl, timeout.Token) : 0 };
            await player.PlayAsync(protectedSource, timeout.Token);
            await WaitForPlaybackAsync(player);
            if (args.Contains("--analysis-probe"))
            {
                using var analysisTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                analysisTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var sampleTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
                while (player.Poll().Position < 10) await sampleTimer.WaitForNextTickAsync(analysisTimeout.Token);
                var sample = player.TakeAnalysisSample() ?? throw new InvalidOperationException("The protected browser player supplied no PCM.");
                using var encoder = new Pookie.Recommendations.Audio.MusicnnEncoder();
                var vector = encoder.Encode(sample) ?? throw new InvalidOperationException("The protected browser player supplied silence instead of audio.");
                Console.WriteLine($"BROWSER_ANALYSIS_PROBE_OK: {sample.Mono.Length} mono PCM samples; {vector.Values.Length} embedding dimensions; volume remains zero");
            }
            if (args.Contains("--normalization-probe"))
            {
                using var meterTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
                while (player.Poll().Position < 5) await meterTimer.WaitForNextTickAsync(timeout.Token);
                var normalized = player.Poll();
                if (normalized.NormalizationGainDb is not { } gain || gain != protectedSource.NormalizationGainDb || gain >= 0)
                    throw new InvalidOperationException("Protected track did not keep its waveform correction.");
                Console.WriteLine($"NORMALIZATION_PROBE_OK: constant waveform correction={gain:F2} dB; user volume remains zero");
            }
            Console.WriteLine("PROTECTED_PLAYBACK_OK: decoded audio clock advances");
            if (Math.Abs(player.Poll().Duration - resolved.Stream.Duration) > 2) throw new InvalidOperationException("DRM duration mismatch.");
            if (player is WindowsAudioPlayer)
            {
                using var cancelledSeek = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                var superseded = player.SeekAsync(Math.Min(5, resolved.Stream.Duration / 3), cancelledSeek.Token);
                cancelledSeek.Cancel();
                try { await superseded; }
                catch (OperationCanceledException) when (cancelledSeek.IsCancellationRequested) { Console.WriteLine("PROTECTED_SEEK_CANCELLED_OK"); }
            }
            await player.SeekAsync(Math.Min(30, resolved.Stream.Duration / 2), timeout.Token);
            await WaitForPlaybackAsync(player);
            if (player.Poll().Position < Math.Min(30, resolved.Stream.Duration / 2) - .2) throw new InvalidOperationException("DRM seek failed.");
            Console.WriteLine("PROTECTED_SEEK_OK: playback advances after seeking");
            player.Pause(true); await Task.Delay(150, timeout.Token);
            if (player.Poll().Playing) throw new InvalidOperationException("DRM pause failed.");
            player.Pause(false); await WaitForPlaybackAsync(player);
            Console.WriteLine("PROTECTED_PAUSE_RESUME_OK");
            await player.SeekAsync(Math.Max(0, resolved.Stream.Duration - 1.5), timeout.Token);
            while (!player.Poll().Ended) await Task.Delay(50, timeout.Token);
            if (player.Poll().Ended) throw new InvalidOperationException("DRM end signaled twice.");
            await player.PlayAsync(protectedSource, timeout.Token);
            await WaitForPlaybackAsync(player);
            player.Stop();
            if (player.Poll().Playing || player.Poll().Position != 0) throw new InvalidOperationException("DRM stop failed.");
            Console.WriteLine("PROTECTED_AUDIO_SMOKE_OK: real SoundCloud license, audio playback, full duration, seek, pause, resume, end, restart and stop; platform production backend");
            return true;
        }
        if (args.Contains("--session-smoke-test"))
        {
            using var vault = SessionVault.Open(new AppDataPaths());
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

    private static byte[] UnwrapServiceCertificate(byte[] envelope)
    {
        // The certificate response is a SignedMessage(type=SERVICE_CERTIFICATE,
        // msg=SignedDrmCertificate). SetServerCertificate accepts the signed certificate itself.
        if (envelope.Length < 4 || envelope[0] != 8 || envelope[1] != 5 || envelope[2] != 18)
            throw new InvalidOperationException("Unexpected service-certificate envelope.");
        var offset = 3;
        uint length = 0;
        for (var shift = 0; shift <= 28 && offset < envelope.Length; shift += 7)
        {
            var value = envelope[offset++];
            if (shift == 28 && value > 15) break;
            length |= (uint)(value & 127) << shift;
            if ((value & 128) != 0) continue;
            if (length is < 1 or > 65536 || length > envelope.Length - offset) break;
            return envelope.AsSpan(offset, (int)length).ToArray();
        }
        throw new InvalidOperationException("Invalid service-certificate envelope length.");
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
