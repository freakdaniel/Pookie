using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Browser;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class MobileChallengeSmokeTest
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var pageLoads = 0;
        app.MapGet("/challenge/{mode}", async context =>
        {
            Interlocked.Increment(ref pageLoads);
            if (context.Request.Headers.Authorization.Count != 0 || context.Request.Cookies.Count != 0)
                throw new InvalidOperationException("The isolated challenge received account credentials or persisted cookies.");
            var mode = context.Request.RouteValues["mode"]?.ToString();
            context.Response.ContentType = "text/html; charset=utf-8";
            if (mode is "frames" or "message-frames")
            {
                await context.Response.WriteAsync("<!doctype html><iframe src='/challenge/" + (mode == "frames" ? "success" : "message") + "'></iframe>");
                return;
            }
            await context.Response.WriteAsync(mode == "wait" ? "<!doctype html><title>Pending local challenge</title>" : $$"""
                <!doctype html><title>Local mobile challenge fixture</title><p>Local test; no real account.</p>
                <script>
                const timer = setInterval(() => {
                  if (!window.Android) return;
                  clearInterval(timer);
                  if ({{System.Text.Json.JsonSerializer.Serialize(mode == "message")}}) {
                    const passed = {eventType:'passed', responseType:'captcha', cookie:'datadome=message-cookie; Path=/; Secure'};
                    window.dispatchEvent(new MessageEvent('message', {origin:location.origin, source:window, data:JSON.stringify({...passed,cookie:'datadome=forged-cookie'})}));
                    window.parent.postMessage(JSON.stringify({...passed,eventType:'load',cookie:'datadome=load-cookie'}), '*');
                    setTimeout(() => window.parent.postMessage(JSON.stringify(passed), '*'), 100);
                  } else if ({{System.Text.Json.JsonSerializer.Serialize(mode == "blocked")}}) {
                    Android.onAdditionalChallengeRedirection(2);
                  } else {
                    Android.onAdditionalChallengeRedirection(1);
                    Android.onCaptchaSuccess('datadome=old-cookie; path=/');
                    setTimeout(() => Android.onCaptchaSuccess('datadome=verified-cookie; Path=/; Secure; SameSite=None'), 50);
                  }
                }, 20);
                </script>
                """);
        });
        await app.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var origin = app.Urls.Single();
        var identity = new SoundCloudMobileIdentity("0123456789abcdef", "Fixture", "Model", "15");
        var account = new WebSession("mobileid", "private-token", identity.UserAgent) { MobileIdentity = identity, DataDomeClientId = "old-cookie" };
        var calls = 0; var changed = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (request.RequestUri!.Host != "api-mobile.soundcloud.com" || request.Method != HttpMethod.Post)
                throw new InvalidOperationException("The operation moved outside the native mobile API.");
            if (calls == 1)
            {
                var denied = Json("""{"url":"https://geo.captcha-delivery.com/captcha/?cid=fixture"}""", 403);
                denied.Headers.Add("X-DD-B", "259"); return denied;
            }
            if (request.Headers.GetValues("Cookie").Single() != "datadome=verified-cookie")
                throw new InvalidOperationException("The original operation did not receive the solved cookie.");
            return Json("""{"collection":[{"target_urn":"soundcloud:playlists:17"}]}""");
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, mobileChallenge: new NativeMobileChallenge(origin + "/challenge/success"));
        transport.SessionChanged += () => changed++;
        await transport.SetPlaylistLikedAsync(42, 17, true, timeout.Token);
        if (calls != 2 || changed != 1 || account.DataDomeClientId != "verified-cookie" || pageLoads != 1)
            throw new InvalidOperationException("The native mobile challenge did not complete once.");

        var challenge = new SoundCloudMobileChallenge(SoundCloudChallengeKind.Captcha,
            "https://api-mobile.soundcloud.com/playlists", "https://geo.captcha-delivery.com/captcha/", null, "ru", "old-cookie");
        try
        {
            await new NativeMobileChallenge(origin + "/challenge/blocked").SolveAsync(challenge, timeout.Token);
            throw new InvalidOperationException("Hard block was accepted as a solved challenge.");
        }
        catch (SoundCloudException error) when (error.ChallengeKind == SoundCloudChallengeKind.HardBlock) { }

        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
        {
            cancel.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await new NativeMobileChallenge(origin + "/challenge/wait").SolveAsync(challenge, cancel.Token);
                throw new InvalidOperationException("Canceled challenge returned a cookie.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }

        // The HTML denial is rendered at its API base URI with no network read
        // and no website profile. The SDK callback must exist at document start.
        var inline = challenge with
        {
            Url = null,
            Html = "<!doctype html><script>Android.onCaptchaSuccess('datadome=inline-cookie; Path=/; Secure');</script>"
        };
        var cookie = await new NativeMobileChallenge().SolveAsync(inline, timeout.Token);
        if (cookie != "inline-cookie") throw new InvalidOperationException("Inline Android challenge callback was lost.");
        var frameCookie = await new NativeMobileChallenge(origin + "/challenge/frames").SolveAsync(challenge, timeout.Token);
        if (frameCookie != "verified-cookie") throw new InvalidOperationException("The SDK callback was not available in the challenge frame.");
        foreach (var mode in new[] { "message", "message-frames" })
        {
            var messageCookie = await new NativeMobileChallenge(origin + "/challenge/" + mode).SolveAsync(challenge, timeout.Token);
            if (messageCookie != "message-cookie") throw new InvalidOperationException("The provider's passed postMessage did not complete the challenge.");
        }
        var forged = "{\"kind\":\"success\",\"value\":\"datadome=forged\",\"pairing\":\"wrong\"}";
        if (MobileChallengeBridge.Read(forged, challenge.Url, challenge, "expected", null).Result != null ||
            MobileChallengeBridge.Allows("https://soundcloud.com/you", challenge, null))
            throw new InvalidOperationException("The challenge bridge accepted an unpaired message or a website navigation.");
        await app.StopAsync(timeout.Token);
        Console.WriteLine("MOBILE_CHALLENGE_OK: isolated WebView, SDK callbacks and provider passed postMessage in top/frame, synthetic/load messages rejected, original native mutation, one replay, hard block, cancellation, inline HTML and bridge isolation; no real account or desktop session used");
    }

    private static HttpResponseMessage Json(string body, int status = 200) => new((HttpStatusCode)status)
        { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }
}
