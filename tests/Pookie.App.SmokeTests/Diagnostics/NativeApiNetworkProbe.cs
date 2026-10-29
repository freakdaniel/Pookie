using System.Net;
using Pookie.App.Auth;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class NativeApiNetworkProbe
{
    public static async Task RunAsync(string? trackUrl = null, bool mobileContracts = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var imported = MobileSessionImport.Load();
        var mobile = imported != null || MobileOAuthConfiguration.Load() != null;
        using var vault = SessionVault.Open(new AppDataPaths(mobile ? Path.Combine(AppDataPaths.DefaultRoot(), "Profiles", "Android") : null));
        var account = vault.Load() ?? imported ?? throw new InvalidOperationException("Для проверки нужна сохранённая сессия SoundCloud.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli };
        if (Environment.GetEnvironmentVariable("POOKIE_PROXY") is { Length: > 0 } proxy) handler.Proxy = new WebProxy(proxy);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        // Deliberately no browser delegate: this probe establishes whether the
        // exported session actually works with the native network stack.
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var requests = 0;
        transport.Observed += observation =>
        {
            requests++;
            Console.WriteLine($"NATIVE_HTTP host={observation.Host} method={observation.Method} status={observation.Status} browser_recovery={observation.BrowserRecovery}");
        };
        var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = transport, RequireBrowserTransport = true };
        try
        {
            var me = await api.GetMeAsync(timeout.Token);
            if (trackUrl != null)
            {
                var target = await api.ResolveAsync(trackUrl, timeout.Token);
                var fresh = await api.GetTrackAsync(target.Id, timeout.Token);
                if (mobileContracts)
                {
                    var configuration = await transport.GetMobileConfigurationAsync(timeout.Token);
                    Console.WriteLine($"MOBILE_CONFIGURATION_OK features={configuration.Count}");
                    using var owned = await transport.GetJsonAsync(new($"https://api-v2.soundcloud.com/users/{me.Id}/playlists?limit=1&linked_partitioning=1&show_tracks=false"), timeout.Token);
                    var playlist = LibraryData.Parse(owned.RootElement).Items.FirstOrDefault()?.Playlist;
                    if (playlist != null)
                    {
                        using var info = await transport.GetJsonAsync(SoundCloudMobileRoutes.Uri($"/playlists/{SoundCloudMobileRoutes.Urn("playlists", playlist.Id)}/info"), timeout.Token);
                        var root = info.RootElement;
                        var owner = root.GetProperty("playlist").GetProperty("_embedded").GetProperty("user").GetProperty("urn").GetString();
                        if (owner != SoundCloudMobileRoutes.Urn("users", me.Id) || root.GetProperty("tracks").GetProperty("collection").ValueKind != System.Text.Json.JsonValueKind.Array)
                            throw new InvalidOperationException("Мобильная схема плейлиста не совпала с контрактом APK.");
                        Console.WriteLine("MOBILE_PLAYLIST_SCHEMA_OK; owner and track identifiers omitted; no edits sent");
                    }
                    else Console.WriteLine("MOBILE_PLAYLIST_SCHEMA_SKIPPED: no owned playlist returned");
                    var fallbacks = SoundCloudMobileStreams.WithFallbacks(fresh with { Media = null });
                    var mobileResolved = false;
                    foreach (var transcoding in fallbacks.Media!.Transcodings)
                    {
                        try
                        {
                            var stream = await api.ResolveTranscodingAsync(fallbacks, transcoding, timeout.Token);
                            if (stream == null) continue;
                            Console.WriteLine($"MOBILE_STREAM_OK protocol={stream.Protocol} direct_api={SoundCloudMobileRoutes.IsStream(stream.Uri)}; media body not buffered; URL omitted");
                            mobileResolved = true; break;
                        }
                        catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 403 or 404)
                        { Console.WriteLine($"MOBILE_STREAM_UNAVAILABLE protocol={transcoding.Format?.Protocol} status={error.StatusCode}"); }
                    }
                    if (!mobileResolved) throw new InvalidOperationException("Мобильный endpoint не предоставил доступный поток.");
                }
                Console.WriteLine($"NATIVE_TRACK_OK id={fresh.Id} profile={(account.MobileIdentity == null ? "web" : "android")} requests={requests}; read-only, no like sent");
                return;
            }
            var search = await api.SearchAsync("ambient", timeout.Token);
            var likes = await api.GetLikesAsync(me.Id, timeout.Token);
            if (likes.Tracks.FirstOrDefault() is { } track)
            {
                await api.GetTrackAsync(track.Id, timeout.Token);
                using var comments = await transport.ReadTrackAsync(new("comments", track.Id), timeout.Token);
            }
            var resolved = false;
            foreach (var candidate in search.Tracks.Concat(likes.Tracks).Where(track => track.Access is not ("blocked" or "preview") &&
                track.Policy is not ("BLOCK" or "SNIP")).DistinctBy(track => track.Id).Take(3))
            {
                try
                {
                    var stream = await new SoundCloudStreamResolver(api).ResolveAsync(candidate.Id, timeout.Token);
                    Console.WriteLine($"NATIVE_STREAM_OK protocol={stream.Stream.Protocol}; URL and credentials omitted; no audio downloaded");
                    resolved = true; break;
                }
                catch (SoundCloudException error) when (error.TrackUnavailable)
                { Console.WriteLine("NATIVE_STREAM_UNAVAILABLE: fresh metadata limits access to this candidate"); }
            }
            if (!resolved) throw new InvalidOperationException("Для проверки потока не найден доступный полный трек.");
            Console.WriteLine($"NATIVE_API_OK requests={requests} profile={(account.MobileIdentity == null ? "web" : "android")} browser_started=false; account details omitted");
        }
        catch (SoundCloudException error)
        {
            Console.WriteLine($"NATIVE_API_REJECTED status={error.StatusCode} challenge={error.ChallengeKind?.ToString() ?? "none"} track_unavailable={error.TrackUnavailable} requests={requests} browser_started=false");
            Environment.ExitCode = 2;
        }
    }
}
