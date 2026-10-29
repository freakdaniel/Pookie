using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Browser;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class NativeSessionRecoverySmokeTest
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var preparations = 0; var nativeWrites = 0; var browserWrites = 0; var rejections = 0; var denyNative = false;
        app.MapGet("/browser", () => Results.Content("""
            <!doctype html><title>Local native recovery fixture</title><script>
            window.__pookieTagReady=true;window.__pookieProtectionReady=true;window.ddSbh=true;window.dataDomeOptions={};
            document.cookie='datadome=fixture-loaded; Path=/';
            </script>
            """, "text/html"));
        app.MapGet("/me", (HttpContext context) =>
        {
            if (context.Request.Headers.Authorization != "OAuth fixture-token") return Results.Unauthorized();
            Interlocked.Increment(ref preparations);
            context.Response.Cookies.Append("datadome", "fixture-prepared");
            return Results.Json(new { id = 42, username = "fixture" });
        });
        app.MapPut("/users/42/track_likes/{id:long}", (HttpContext context) =>
        {
            if (context.Request.Headers.Authorization != "OAuth fixture-token") return Results.Unauthorized();
            if (!context.Request.Headers.ContainsKey("x-datadome-clientid"))
            { Interlocked.Increment(ref browserWrites); return Results.Json(new { }); }
            if (!Volatile.Read(ref denyNative) && context.Request.Headers["x-datadome-clientid"] == "fixture-prepared")
            { Interlocked.Increment(ref nativeWrites); return Results.NoContent(); }
            Interlocked.Increment(ref rejections); context.Response.Headers["X-DD-B"] = "259";
            return Results.Json(new { }, statusCode: 403);
        });
        await app.StartAsync();
        var root = Directory.CreateTempSubdirectory("pookie-persistence-test-recovery-").FullName;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var initial = new WebSession("fixtureid", "fixture-token", "Fixture/1") { DataDomeClientId = "initial" };
            await using var browser = new NativeBrowserSession(initial, app.Urls.Single() + "/browser", root);
            using var http = new HttpClient(new FixtureRoutingHandler(new(app.Urls.Single()))) { Timeout = TimeSpan.FromSeconds(10) };
            using var native = new SoundCloudHttpTransport(http, () => browser.Account, browser);
            var stages = new List<SoundCloudRecoveryStage>(); native.RecoveryObserved += message => stages.Add(message.Stage);
            await native.SetLikedAsync(42, 7, true, timeout.Token);
            await native.SetLikedAsync(42, 8, true, timeout.Token);
            if (preparations != 1 || nativeWrites != 2 || browserWrites != 0 || rejections != 1 || browser.Account.DataDomeClientId != "fixture-prepared")
                throw new InvalidOperationException("Browser preparation did not transfer protection before both native mutations.");
            if (!stages.Contains(SoundCloudRecoveryStage.NativeAccepted)) throw new InvalidOperationException("Native recovery acceptance was not observed.");

            // A server can still reject HTTP with a browser-owned session. Keep
            // the existing one-operation fallback, then return to native HTTP.
            Volatile.Write(ref denyNative, true);
            await native.SetLikedAsync(42, 9, true, timeout.Token);
            if (preparations != 1 || browserWrites != 1 || nativeWrites != 2 || rejections != 2)
                throw new InvalidOperationException("Completed browser fallback was replayed or preparation was repeated.");
            Volatile.Write(ref denyNative, false);
            await native.SetLikedAsync(42, 10, true, timeout.Token);
            if (nativeWrites != 3 || browserWrites != 1 || !stages.Contains(SoundCloudRecoveryStage.BrowserCompleted))
                throw new InvalidOperationException("Requests did not return to native HTTP after browser fallback.");
            Console.WriteLine("NATIVE_SESSION_RECOVERY_OK: real WebView read-only preparation, cookie handoff, native writes, bounded browser fallback and return to native; loopback fixture only");
        }
        finally { await BrowserPersistenceSmokeTest.DeleteFixtureProfileAsync(root); }
    }

    private sealed class FixtureRoutingHandler(Uri origin) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "api-v2.soundcloud.com") throw new InvalidOperationException("Recovery fixture tried a foreign host.");
            request.RequestUri = new Uri(origin, request.RequestUri.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
