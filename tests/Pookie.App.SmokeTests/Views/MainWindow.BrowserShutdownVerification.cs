using Pookie.App.Browser;
using System.Net;
using Aprillz.MewUI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private WebApplication? shutdownFixture;
    private Exception? shutdownCheckFailure;
    private readonly List<Action<BrowserRequestEvent>> lateBrowserCallbacks = [];
    private int closedBrowserWorkers;
    private Task<Messages.ChatVideoSession>? closingNativeStartup;

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

            AttachBrowser(new NativeBrowserSession(new("fixtureid", "fixture-token", "Chromium")));
            lateBrowserCallbacks.Add(browserNotifications!.Value.Handler);
            var connected = new NativeBrowserSession(new("fixtureid", "fixture-token", "Chromium"), shutdownFixture.Urls.Single() + "/");
            AttachBrowser(connected);
            if (api.Transport is not SoundCloudHttpTransport)
                throw new InvalidOperationException("The application did not select native HTTP as the default SoundCloud transport.");
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
            // Close the actual window while a non-cancelable native factory is
            // held. Its late result must be disposed without requiring a UI post.
            var releaseNative = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            chatSessionFactory = (bytes, _) =>
            {
                var owned = bytes.ToArray();
                return closingNativeStartup = Task.Run(async () =>
                {
                    await releaseNative.Task;
                    try { return await Messages.ChatVideoSession.StartAsync(owned, silent: true); }
                    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(owned); }
                });
            };
            videoMusicGain = 0;
            Run(() => PlayChatVoiceBytesAsync(Messages.ChatVoicePcm.Encode(new float[16000], preferOpus: false).Data));
            await WaitForLikedLayoutAsync(() => closingNativeStartup != null);
            _ = Task.Run(async () =>
            {
                while (!Volatile.Read(ref chatClosing)) await Task.Delay(5);
                await Task.Delay(50); releaseNative.TrySetResult();
            });
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
            if (closingNativeStartup is not { IsCompletedSuccessfully: true } started ||
                !(bool)typeof(Messages.ChatVideoSession).GetField("disposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(await started)!)
                throw new InvalidOperationException("Closing the actual window abandoned a held native startup or its late decoder.");
            Console.WriteLine("CHAT_NATIVE_CLOSE_OK: actual Window.Close canceled UI admission and drained a held native startup/late decoder");
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
