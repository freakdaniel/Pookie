using System.Net;
using Aprillz.MewUI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Auth;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private WebApplication? shutdownFixture;
    private Exception? shutdownCheckFailure;
    private readonly List<Action<BrowserRequestEvent>> lateBrowserCallbacks = [];
    private int closedBrowserWorkers;

    private async Task StartBrowserShutdownCheckAsync()
    {
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            shutdownFixture = builder.Build();
            shutdownFixture.MapGet("/", () => Results.Content("""
                <!doctype html><title>Pookie shutdown fixture</title><script>
                window.ddSbh = true; window.dataDomeOptions = {};
                document.cookie = 'datadome=fixture-browser-session; Path=/';
                window.dispatchEvent(new Event('dd_ready'));
                const ready = setInterval(() => {
                  if (typeof window.__pookieRequest !== 'function') return;
                  clearInterval(ready);
                  window.dispatchEvent(new Event('dd_post_done'));
                }, 50);
                </script>
                """, "text/html"));
            shutdownFixture.MapGet("/me", () => Results.Json(new { id = 42, username = "fixture" }));
            await shutdownFixture.StartAsync();

            AttachBrowser(new NativeBrowserSession(new("fixtureid", "fixture-token", "WebKit")));
            lateBrowserCallbacks.Add(browserNotifications!.Value.Handler);
            var connected = new NativeBrowserSession(new("fixtureid", "fixture-token", "WebKit"), shutdownFixture.Urls.Single() + "/");
            AttachBrowser(connected);
            lateBrowserCallbacks.Add(browserNotifications!.Value.Handler);
            connected.Changed += message =>
            {
                if (message.Kind == "audio-closed") Interlocked.Increment(ref closedBrowserWorkers);
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            if ((await connected.GetMeAsync(timeout.Token)).Id != 42)
                throw new InvalidOperationException("Shutdown fixture did not open the real browser worker.");

            // Emulate a worker that captured its subscriber just before unsubscription.
            // Queue its notification while the UI thread is held, then close immediately.
            Task.Run(() => lateBrowserCallbacks[1](new("checking"))).GetAwaiter().GetResult();
        }
        catch (Exception error) { shutdownCheckFailure = error; }
        finally { Window.Close(); }
    }

    internal async Task VerifyBrowserShutdownAsync()
    {
        try
        {
            if (shutdownCheckFailure != null) throw new InvalidOperationException("Browser shutdown setup failed.", shutdownCheckFailure);
            if (Application.IsRunning || !disposed || browserNotifications != null)
                throw new InvalidOperationException("UI shutdown did not release its browser subscription.");
            if (Volatile.Read(ref closedBrowserWorkers) != 1)
                throw new InvalidOperationException("Browser worker did not finish during application shutdown.");
            var finalStatus = status.Value;
            await Task.Run(() =>
            {
                foreach (var callback in lateBrowserCallbacks)
                    foreach (var kind in new[] { "checking", "blocked", "passed", "challenge-error", "protection-session", "audio-state", "audio-closed" })
                        callback(new(kind));
            });
            if (status.Value != finalStatus)
                throw new InvalidOperationException("Late browser callback changed the closed UI.");
            Console.WriteLine("BROWSER_SHUTDOWN_OK: real worker exited; queued and late callbacks after UI teardown were harmless, including replaced sessions");
        }
        finally
        {
            if (shutdownFixture != null) await shutdownFixture.DisposeAsync();
        }
    }
}
