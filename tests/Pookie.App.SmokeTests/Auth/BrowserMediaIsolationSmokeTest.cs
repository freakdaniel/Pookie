using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Browser;
using Pookie.App.Preview;
using Pookie.Media;
#if WINDOWS
using Windows.Media.Control;
#endif

namespace Pookie.App.Diagnostics;

internal static class BrowserMediaIsolationSmokeTest
{
    public static async Task RunAsync()
    {
#if WINDOWS
        const string websiteTitle = "Pookie WebView competing-session fixture";
        const string playerTitle = "Pookie authoritative-player fixture";
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var pageNext = 0;
        var audioPath = DemoAudio.Create(frequency: 0, seconds: 30);
        try
        {
            server.MapGet("/audio.wav", () => Results.File(audioPath, "audio/wav", enableRangeProcessing: true));
            server.MapPost("/page-next", () => { Interlocked.Increment(ref pageNext); return Results.NoContent(); });
            server.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("""
                    <!doctype html><title>Discover fixture</title><audio src="/audio.wav" autoplay loop></audio>
                    <script>
                    window.ddSbh = true; window.dataDomeOptions = {};
                    document.cookie = 'datadome=fixture-media-isolation; Path=/';
                    const audio = document.querySelector('audio'); audio.volume = .1;
                    navigator.mediaSession.setActionHandler('nexttrack', () => fetch('/page-next', {method:'POST'}));
                    setInterval(() => {
                      navigator.mediaSession.metadata = new MediaMetadata({title:'__WEBSITE_TITLE__', artist:'WebView fixture'});
                      navigator.mediaSession.playbackState = 'playing';
                    }, 100);
                    const original = window.fetch.bind(window);
                    window.fetch = (...args) => {
                      if (args[1]?.headers) {
                        args[1].headers['x-fixture-audio-position'] = String(audio.currentTime);
                        args[1].headers['x-fixture-audio-playing'] = String(!audio.paused);
                      }
                      return original(...args);
                    };
                    window.dispatchEvent(new Event('dd_ready'));
                    setInterval(() => window.dispatchEvent(new Event('dd_post_done')), 200);
                    </script>
                    """.Replace("__WEBSITE_TITLE__", websiteTitle));
            });
            server.MapGet("/me", (HttpContext context) => Results.Json(new
            {
                id = 42,
                username = context.Request.Headers["x-fixture-audio-playing"] + ":" + context.Request.Headers["x-fixture-audio-position"]
            }));
            await server.StartAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var session = await MediaSession.CreateAsync(action => action());
            var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var snapshot = new MediaSnapshot("fixture-1", playerTitle, "Player fixture", null, null,
                0, 180, MediaPlayback.Playing, true, true, true, .1, false);
            session.Command += command =>
            {
                if (command.Action != MediaAction.Next) return;
                session.Update(snapshot with { TrackId = "fixture-2", Title = playerTitle + " next" });
                next.TrySetResult();
            };
            session.Update(snapshot);
            await using var browser = new NativeBrowserSession(new("fixtureid", "fixture-token", "WebKit"), server.Urls.Single() + "/");
            await browser.GetMeAsync(timeout.Token);
            await Task.Delay(1200, timeout.Token);
            var user = await browser.GetMeAsync(timeout.Token);
            var audio = user.Username.Split(':');
            if (audio.Length != 2 || audio[0] != "true" ||
                !double.TryParse(audio[1], System.Globalization.CultureInfo.InvariantCulture, out var position) || position < .5)
                throw new InvalidOperationException("Disabling WebView media integration interrupted audio playback: " + user.Username);

            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? published = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                foreach (var candidate in manager.GetSessions())
                {
                    var title = (await candidate.TryGetMediaPropertiesAsync()).Title;
                    if (title == websiteTitle) throw new InvalidOperationException("WebView published a competing system media session.");
                    if (title == playerTitle) published = candidate;
                }
                await Task.Delay(100, timeout.Token);
            }
            if (published == null || !await published.TrySkipNextAsync())
                throw new InvalidOperationException("Pookie's system session did not accept Next.");
            await next.Task.WaitAsync(timeout.Token);
            for (var attempt = 0; (await published.TryGetMediaPropertiesAsync()).Title != playerTitle + " next"; attempt++)
            {
                if (attempt >= 30) throw new InvalidOperationException("Pookie metadata did not follow Next.");
                await Task.Delay(100, timeout.Token);
            }
            if (pageNext != 0) throw new InvalidOperationException("Next was delivered to the website player.");
            Console.WriteLine("BROWSER_MEDIA_ISOLATION_OK: real WebView audio progresses, website metadata stays out of SMTC, Next reaches Pookie and updates its metadata");
        }
        finally { File.Delete(audioPath); }
#else
        throw new PlatformNotSupportedException("This check uses Windows SMTC and WebView2.");
#endif
    }
}
