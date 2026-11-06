using System.Net;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class NativeApiNetworkProbe
{
    public static async Task RunAsync(string? trackUrl = null, bool mobileContracts = false, bool mobileChallenges = false)
    {
        using var timeout = new CancellationTokenSource(mobileChallenges ? TimeSpan.FromMinutes(4) : TimeSpan.FromSeconds(45));
        var imported = MobileSessionImport.Load();
        var mobile = imported != null || MobileOAuthConfiguration.Load() != null;
        using var vault = SessionVault.Open(new AppDataPaths(mobile ? Path.Combine(AppDataPaths.DefaultRoot(), "Profiles", "Android") : null));
        var account = vault.Load() ?? imported ?? throw new InvalidOperationException("Для проверки нужна сохранённая сессия SoundCloud.");
        if (mobileChallenges && account.MobileIdentity == null) throw new InvalidOperationException("Для проверки Android challenge нужна сохранённая мобильная сессия.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = account.MobileIdentity == null,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli };
        if (Environment.GetEnvironmentVariable("POOKIE_PROXY") is { Length: > 0 } proxy) handler.Proxy = new WebProxy(proxy);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        // Deliberately no browser delegate: this probe establishes whether the
        // exported session actually works with the native network stack.
        var challenge = mobileChallenges ? new NativeMobileChallenge() : null;
        var options = mobileChallenges ? MobileOAuthConfiguration.Load() : null;
        using var authorization = options == null ? null : new SoundCloudMobileSessionManager(
            new SoundCloudMobileOAuth(http, options, mobileChallenge: challenge), () => account, updated => account = updated,
            (updated, token) => Task.Run(() => vault.Save(updated), token));
        using var transport = new SoundCloudHttpTransport(http, () => account, authorization: authorization, mobileChallenge: challenge);
        if (mobileChallenges) transport.SessionChanged += () => vault.Save(account);
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
            if (mobileChallenges)
            {
                var configuration = await transport.GetMobileConfigurationAsync(timeout.Token);
                Console.WriteLine($"MOBILE_CONFIGURATION_OK features={configuration.Count}");
                var owned = await transport.GetMobileOwnedPlaylistsAsync(me.Id, timeout.Token);
                Console.WriteLine($"MOBILE_OWNED_PLAYLISTS_OK count={owned.Length}; identifiers omitted; no edits sent");
                var library = await api.GetLibraryAsync("collections", me.Id, timeout.Token);
                var mobileLikes = await api.GetLikedIdsAsync(timeout.Token);
                var mobileFollowing = await api.GetFollowingIdsAsync(me.Id, timeout.Token);
                var playlistLikes = await api.GetPlaylistActionIdsAsync(false, timeout.Token);
                var reposted = await api.GetRepostedIdsAsync(timeout.Token);
                Console.WriteLine($"MOBILE_LIBRARY_OK collections={library.Items.Length} likes={mobileLikes.Count} following={mobileFollowing.Count} playlist_likes={playlistLikes.Count} reposts={reposted.Count}; no edits sent");
                long? membershipTrack = null;
                foreach (var entry in owned)
                {
                    var first = entry.Playlist!;
                    Console.WriteLine($"MOBILE_PLAYLIST_CHECK ordinal={Array.IndexOf(owned, entry) + 1} expected_tracks={first.TrackCount}; identifiers omitted");
                    var full = await transport.GetMobilePlaylistAsync(first.Id, timeout.Token);
                    if (full.User?.Id != me.Id || !full.TrackPagesComplete)
                        throw new InvalidOperationException("Мобильный список треков не подтвердил владельца или полный состав.");
                    var detail = await api.GetCollectionDetailAsync(entry, timeout.Token);
                    if (detail.Item.Key != entry.Key || detail.Item.Playlist?.Id != first.Id || detail.Tracks.NextHref != null)
                        throw new InvalidOperationException("Мобильное открытие плейлиста не подтвердило карточку.");
                    Console.WriteLine($"MOBILE_PLAYLIST_TRACKS_OK returned={full.Tracks.Length} declared={full.TrackCount}; identifiers omitted; no edits sent");
                    membershipTrack ??= full.Tracks.FirstOrDefault()?.Id;
                }
                if (membershipTrack is { } selectedTrack)
                {
                    var choices = await api.GetPlaylistChoicesForTrackAsync(me.Id, selectedTrack, timeout.Token);
                    Console.WriteLine($"MOBILE_PLAYLIST_PICKER_OK count={choices.Length}; available track membership checked; no edits sent");
                    // Artwork is public CDN content: never attach the account's
                    // OAuth or DataDome headers to these requests.
                    using var images = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    foreach (var choice in choices)
                    {
                        if (!Uri.TryCreate(choice.ArtworkUrl, UriKind.Absolute, out var image) || image.Scheme != "https" ||
                            !image.Host.EndsWith(".sndcdn.com", StringComparison.OrdinalIgnoreCase) || choice.ArtworkUrl!.Contains("{size}", StringComparison.Ordinal))
                            throw new InvalidOperationException("Мобильный плейлист не предоставил готовый URL обложки.");
                        using var response = await images.GetAsync(image, timeout.Token);
                        response.EnsureSuccessStatusCode();
                        var payload = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true || payload.Length == 0)
                            throw new InvalidOperationException("CDN не вернул изображение плейлиста.");
                        Console.WriteLine($"MOBILE_PLAYLIST_ARTWORK_OK ordinal={Array.IndexOf(choices, choice) + 1} bytes={payload.Length}; URL omitted; no credentials sent");
                    }
                }
            }
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
            if (mobileChallenges && likes.Tracks.Length > 0)
            {
                var counters = likes.Tracks.Count(track => track.PlaybackCount != null && track.LikesCount != null && track.CommentCount != null && track.RepostsCount != null);
                if (counters == 0) throw new InvalidOperationException("В мобильных понравившихся треках потеряны счётчики статистики.");
                Console.WriteLine($"MOBILE_LIKES_STATS_OK tracks={likes.Tracks.Length} with_counters={counters}; values and identifiers omitted");
            }
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
            Console.WriteLine($"NATIVE_API_OK requests={requests} profile={(account.MobileIdentity == null ? "web" : "android")} desktop_session_started=false mobile_challenge_enabled={mobileChallenges}; account details omitted");
        }
        catch (SoundCloudException error)
        {
            Console.WriteLine($"NATIVE_API_REJECTED status={error.StatusCode} challenge={error.ChallengeKind?.ToString() ?? "none"} track_unavailable={error.TrackUnavailable} requests={requests} desktop_session_started=false mobile_challenge_enabled={mobileChallenges}");
            Environment.ExitCode = 2;
        }
    }

}
