using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal static class BrowserWorkerSmokeTest
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        var readRequests = 0;
        var cancelledReads = 0;
        var legacyHistoryWrites = 0;
        var audioHistoryWrites = 0;
        var historyTrack = 0L;
        var rejectHistory = false;
        var pages = 0;
        var largeTitle = string.Concat(Enumerable.Repeat("Музыка 🎵 / ", 180));
        var fixtureTrack = new {
            kind = "track", id = 42, user_id = 47, title = largeTitle, duration = 120000, track_authorization = "fixture-track-auth",
            media = new { transcodings = new[] { new { url = "https://api-v2.soundcloud.com/media/42", snipped = false,
                format = new { protocol = "hls", mime_type = "audio/mpeg" } } } }
        };
        app.Use(async (context, next) =>
        {
            if (context.Request.Path != "/" && context.Request.Path != "/restricted")
            {
                if (context.Request.Headers.Authorization != "OAuth fixture-token" || context.Request.Query["client_id"] != "fixtureid")
                    throw new InvalidOperationException("API read escaped the browser session");
                if (HttpMethods.IsGet(context.Request.Method)) Interlocked.Increment(ref readRequests);
            }
            await next(context);
        });
        var deviceRequests = 0;
        var interactiveRequests = 0;
        var cancelledRequests = 0;
        var hardBlockedRequests = 0;
        var playlistWrites = 0; var cancelledPlaylistWrites = 0;
        app.MapGet("/", async context =>
        {
            Interlocked.Increment(ref pages);
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("""
                <!doctype html><html lang="en"><title>Pookie browser worker fixture</title>
                <h1>Local browser check fixture</h1><script>
                window.ddSbh = true; window.dataDomeOptions = {};
                document.cookie = 'datadome=fixture-browser-session; Path=/';
                const original = window.fetch.bind(window);
                window.fetch = async (...args) => {
                  args[1].headers['x-fixture-width'] = String(window.innerWidth);
                  const response = await original(...args);
                  if (response.status === 403) {
                    const type = response.headers.get('x-fixture-type');
                    window.dispatchEvent(new CustomEvent('dd_blocked', {detail:{challengeType:type}}));
                    window.dispatchEvent(new CustomEvent('dd_response_displayed', {detail:{challengeType:type}}));
                    if (type !== 'hard_block' && response.headers.get('x-fixture-wait') !== 'forever')
                      setTimeout(() => window.dispatchEvent(new Event('dd_response_passed')), 650);
                  }
                  return response;
                };
                window.dispatchEvent(new Event('dd_ready'));
                setTimeout(() => window.dispatchEvent(new Event('dd_post_done')), 100);
                </script></html>
                """);
        });
        app.MapGet("/restricted", () => Results.Content("<!doctype html><title>Доступ временно ограничен</title><h1>Доступ временно ограничен</h1>", "text/html; charset=utf-8"));
        app.MapGet("/me", () => Results.Json(new { id = 42, username = "fixture", avatar_url = (string?)null }));
        app.MapPost("/me/play-history", async context =>
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body);
            if (document.RootElement.GetProperty("track_urn").GetString() != "soundcloud:tracks:42" ||
                document.RootElement.GetProperty("context_urn").GetString() != "soundcloud:playlists:11")
                throw new InvalidOperationException("Playback context was lost.");
            Interlocked.Increment(ref legacyHistoryWrites);
            // Reproduce the real server: this acknowledgement does not persist tracks.
            context.Response.StatusCode = 204;
        });
        app.MapPost("/me", async context =>
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body);
            var events = document.RootElement.GetProperty("events");
            if (events.GetArrayLength() != 1 || document.RootElement.GetProperty("auth_token").GetString() != "fixture-token")
                throw new InvalidOperationException("Playback event lost its session or included unrelated events.");
            var message = events[0]; var payload = message.GetProperty("payload");
            if (message.GetProperty("event").GetString() != "audio" || payload.GetProperty("action").GetString() != "play" ||
                payload.GetProperty("track").GetString() != "soundcloud:tracks:42" ||
                payload.GetProperty("track_owner").GetString() != "soundcloud:users:47" ||
                payload.GetProperty("in_playlist").GetString() != "soundcloud:playlists:11" ||
                payload.GetProperty("user").GetString() != "soundcloud:users:42" ||
                payload.GetProperty("track_authorization").GetString() != "fixture-track-auth" ||
                !Guid.TryParse(payload.GetProperty("session_id").GetString(), out _))
                throw new InvalidOperationException("Incomplete authenticated playback event.");
            if (rejectHistory) { context.Response.StatusCode = 400; return; }
            Interlocked.Increment(ref audioHistoryWrites);
            historyTrack = 42;
            context.Response.StatusCode = 202;
        });
        app.MapGet("/me/play-history/tracks", () => Results.Json(new
        { collection = historyTrack > 0 ? new[] { fixtureTrack } : [], next_href = (string?)null }));
        app.MapGet("/me/track_likes/ids", () => Results.Json(Enumerable.Range(1, 750).ToArray()));
        app.MapGet("/search/tracks", async context =>
        {
            if (context.Request.Query["q"] == "cancel")
            {
                Interlocked.Increment(ref cancelledReads);
                try { await Task.Delay(TimeSpan.FromSeconds(15), context.RequestAborted); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            }
            else await context.Response.WriteAsJsonAsync(new { collection = new[] { fixtureTrack }, next_href = (string?)null });
        });
        app.MapGet("/users/42/likes", () => Results.Json(new { collection = Enumerable.Repeat(new { track = fixtureTrack }, 30), next_href = (string?)null }));
        app.MapGet("/stream", () => Results.Json(new { collection = new[] { new { origin = fixtureTrack } }, next_href = (string?)null }));
        app.MapGet("/tracks/42", () => Results.Json(fixtureTrack));
        app.MapGet("/resolve", () => Results.Json(fixtureTrack));
        app.MapGet("/media/42", (HttpContext context) => {
            if (context.Request.Query["track_authorization"] != "fixture-track-auth") throw new InvalidOperationException("Track authorization lost");
            return Results.Json(new { url = "https://cf-hls-media.sndcdn.com/fixture.m3u8" });
        });
        app.MapMethods("/users/42/track_likes/{track}", ["PUT", "DELETE"], context =>
        {
            if (context.Request.Headers.Authorization != "OAuth fixture-token" || context.Request.Query["client_id"] != "fixtureid")
                throw new InvalidOperationException("WebView не передал тестовую авторизацию.");
            if (!int.TryParse(context.Request.Headers["x-fixture-width"], out var width) || width < 800)
                throw new InvalidOperationException("Скрытый WebView потерял viewport.");
            var track = context.Request.RouteValues["track"]!.ToString();
            var count = track switch
            {
                "90" => Interlocked.Increment(ref deviceRequests),
                "92" => Interlocked.Increment(ref interactiveRequests),
                "93" => Interlocked.Increment(ref hardBlockedRequests),
                _ => Interlocked.Increment(ref cancelledRequests)
            };
            context.Response.StatusCode = count == 1 ? 403 : 204;
            context.Response.Headers["x-fixture-type"] = track == "93" ? "hard_block" : track == "92" ? "block" : "device_check";
            if (track == "91") context.Response.Headers["x-fixture-wait"] = "forever";
            return Task.CompletedTask;
        });
        app.MapMethods("/users/42/playlist_likes/{playlist}", ["PUT", "DELETE"], context =>
        {
            if (context.Request.RouteValues["playlist"]!.ToString() == "12")
            {
                Interlocked.Increment(ref cancelledPlaylistWrites);
                context.Response.StatusCode = 403;
                context.Response.Headers["x-fixture-type"] = "device_check";
                context.Response.Headers["x-fixture-wait"] = "forever";
            }
            else { Interlocked.Increment(ref playlistWrites); context.Response.StatusCode = 204; }
            return Task.CompletedTask;
        });
        app.MapMethods("/me/playlist_reposts/11", ["PUT", "DELETE"], context =>
        { Interlocked.Increment(ref playlistWrites); context.Response.StatusCode = 204; return Task.CompletedTask; });
        await app.StartAsync();
        await using var browser = new NativeBrowserSession(new("fixtureid", "fixture-token", "Chromium"), app.Urls.Single() + "/");
        var observed = new System.Collections.Concurrent.ConcurrentQueue<BrowserRequestEvent>();
        browser.Changed += observed.Enqueue;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if ((await browser.GetMeAsync(timeout.Token)).Id != 42) throw new InvalidOperationException("Профиль не передан.");
        var ids = await browser.GetLikedIdsAsync(timeout.Token);
        if (ids.Count != 750 || !ids.Contains(750)) throw new InvalidOperationException("Пакеты ID потеряны при передаче.");
        using var http = new HttpClient(new RejectHttp());
        var api = new SoundCloudWebClient(http) { Session = browser.Account, BrowserTransport = browser };
        var liked = await api.GetLikesAsync(42, timeout.Token);
        if (liked.Tracks.Length != 30 || liked.Tracks.Any(track => track.Title != largeTitle))
            throw new InvalidOperationException("Large Unicode library response lost JSON chunks");
        if ((await api.SearchAsync("музыка", timeout.Token)).Tracks.Single().Title != largeTitle ||
            (await api.GetFeedAsync(timeout.Token)).Tracks.Single().Id != 42 ||
            (await api.ResolveAsync("https://soundcloud.com/fixture/track", timeout.Token)).Id != 42)
            throw new InvalidOperationException("Browser API routing failed");
        var stream = await api.GetStreamAsync(liked.Tracks[0], timeout.Token);
        if (stream.Uri.Host != "cf-hls-media.sndcdn.com" || stream.Protocol != "hls")
            throw new InvalidOperationException("Browser transcoding resolution failed");
        using (var readCancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(350)))
        {
            try { await api.SearchAsync("cancel", readCancel.Token); throw new InvalidOperationException("Read was not cancelled"); }
            catch (OperationCanceledException) when (readCancel.IsCancellationRequested) { }
        }
        if ((await api.GetMeAsync(timeout.Token)).Id != 42 || pages != 1 || cancelledReads != 1)
            throw new InvalidOperationException("Cancelling a read discarded the live browser");
        Console.WriteLine($"BROWSER_API_OK: {readRequests} authenticated GETs through the native WebView, large Unicode JSON, search/feed/library/resolve/transcoding; cancelled fetch preserved the same page");
        await api.RecordPlaybackAsync(42, "soundcloud:playlists:11", timeout.Token);
        if (legacyHistoryWrites != 1 || audioHistoryWrites != 1 ||
            (await api.GetLibraryAsync("history", 42, timeout.Token)).Items.FirstOrDefault()?.Track?.Id != 42)
            throw new InvalidOperationException("A legacy acknowledgement was mistaken for persisted playback history.");
        rejectHistory = true;
        var historyRejected = false;
        try { await api.RecordPlaybackAsync(42, "soundcloud:playlists:11", timeout.Token); }
        catch (SoundCloudException error) when (error.StatusCode == 400) { historyRejected = true; }
        if (!historyRejected || legacyHistoryWrites != 2 || audioHistoryWrites != 1)
            throw new InvalidOperationException("Playback event failure was accepted or replayed.");
        Console.WriteLine("BROWSER_HISTORY_OK: audio/play persists history after legacy 204; event failure propagates without replay");
        await browser.SetLikedAsync(42, 90, true, timeout.Token);
        await browser.SetLikedAsync(42, 90, false, timeout.Token);
        await browser.SetLikedAsync(42, 92, true, timeout.Token);
        await browser.GetMeAsync(timeout.Token);
        if (pages != 1 || deviceRequests != 3 || interactiveRequests != 2 ||
            !observed.Any(x => x.Kind == "checking" && !x.Interactive && x.ChallengeType == "device_check") ||
            !observed.Any(x => x.Kind == "checking" && x.Interactive && x.ChallengeType == "block") ||
            !observed.Any(x => x.Kind == "passed" && x.ChallengeType == "device_check") ||
            !observed.Any(x => x.Kind == "passed" && x.ChallengeType == "block"))
            throw new InvalidOperationException("Контекст не сохранился или проверка не повторила запрос.");
        Console.WriteLine("BROWSER_WORKER_OK: real WebView fetch, persistent context, 403 checks, window reveal/hide, automatic retry, PUT and DELETE");
        await browser.SetPlaylistLikedAsync(42, 11, true, timeout.Token);
        await browser.SetPlaylistLikedAsync(42, 11, false, timeout.Token);
        await browser.SetPlaylistRepostedAsync(11, true, timeout.Token);
        await browser.SetPlaylistRepostedAsync(11, false, timeout.Token);
        if (playlistWrites != 4) throw new InvalidOperationException("Playlist actions did not use the Electron transport.");
        using (var playlistCancel = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            try { await browser.SetPlaylistLikedAsync(42, 12, true, playlistCancel.Token); throw new InvalidOperationException("Playlist write was not cancelled."); }
            catch (OperationCanceledException) when (playlistCancel.IsCancellationRequested) { }
        }
        await browser.GetMeAsync(timeout.Token);
        if (cancelledPlaylistWrites != 1) throw new InvalidOperationException("Cancelled playlist write replayed after worker restart.");
        Console.WriteLine("BROWSER_PLAYLIST_ACTIONS_OK: PUT/DELETE and cancelled pending write; no delayed replay");
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await browser.SetLikedAsync(42, 91, true, cancel.Token); throw new InvalidOperationException("Запрос не отменён."); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        await browser.DisposeAsync();
        await Task.Delay(800);
        if (cancelledRequests != 1) throw new InvalidOperationException("Отменённый лайк был повторён позже.");
        Console.WriteLine("BROWSER_WORKER_CANCEL_OK: pending check cancelled, worker exited, no delayed write");
        await using var blocked = new NativeBrowserSession(new("fixtureid", "fixture-token", "Chromium"), app.Urls.Single() + "/");
        await blocked.GetMeAsync(timeout.Token);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { await blocked.SetLikedAsync(42, 93, true, timeout.Token); throw new InvalidOperationException("Hard block did not reject action"); }
            catch (SoundCloudException error) when (error.StatusCode == 403 && error.Message.Contains("Капча не предложена")) { }
        }
        if (hardBlockedRequests != 1) throw new InvalidOperationException("Hard block was retried");
        Console.WriteLine("BROWSER_WORKER_BLOCK_OK: hard-block page revealed, action failed immediately, subsequent write suppressed");
        await using var restricted = new NativeBrowserSession(new("fixtureid", "fixture-token", "Chromium"), app.Urls.Single() + "/restricted");
        try { await restricted.GetMeAsync(timeout.Token); throw new InvalidOperationException("Initial restricted document was left hidden"); }
        catch (SoundCloudException error) when (error.StatusCode == 403) { }
        Console.WriteLine("BROWSER_DOCUMENT_BLOCK_OK: restricted initial document revealed and request rejected before readiness");
    }
    private sealed class RejectHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Authenticated API used .NET HTTP instead of WebView");
    }

}
