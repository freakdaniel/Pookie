using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Hosting;

namespace Pookie.App.Diagnostics;

internal static class EmbeddedWebChallengeSmokeTest
{
    internal static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var calls = new ConcurrentDictionary<int, int>();
        var releases = new ConcurrentDictionary<int, TaskCompletionSource>();
        var documents = 0;
        var refreshes = 0;
        var chatRequests = 0;
        var expiredDuringUse = false;
        server.MapGet("/", (HttpContext context) =>
        {
            Interlocked.Increment(ref documents);
            if (!context.Request.Cookies.ContainsKey("datadome")) context.Response.Cookies.Append("datadome", "initial");
            if (!context.Request.Cookies.ContainsKey("oauth_token"))
                context.Response.Cookies.Append("oauth_token", "fixture-expired", new CookieOptions { MaxAge = TimeSpan.FromDays(7) });
            if (!context.Request.Cookies.ContainsKey("oauth_refresh_token"))
                context.Response.Cookies.Append("oauth_refresh_token", "fixture-refresh", new CookieOptions { HttpOnly = true, MaxAge = TimeSpan.FromDays(7) });
            return Results.Content("""
                <!doctype html><html><head><title>Web session fixture</title></head><body style="background:#243055;color:#eee;font:22px sans-serif">
                <h1>Existing SoundCloud website session</h1><button>Fixture website</button>
                <script>
                window.__pookieTagReady=true;window.__pookieProtectionReady=true;
                window.dataDomeOptions={sessionByHeader:true};
                if(!localStorage.getItem('ddSession_datadome'))localStorage.setItem('ddSession_datadome','initial');
                window.dispatchEvent(new Event('dd_ready'));window.dispatchEvent(new Event('dd_post_done'));
                const fetchOriginal=window.fetch;
                window.fetch=async function(input,init){
                  let response=await fetchOriginal(input,init);
                  if(response.status!==403)return response;
                  const id=Number(new URL(typeof input==='string'?input:input.url).pathname.split('/').pop());
                  if(id===20){window.dispatchEvent(new CustomEvent('dd_response_displayed',{detail:{challengeType:'hard_block'}}));return response;}
                  if(id===19){
                    window.dispatchEvent(new CustomEvent('dd_response_displayed',{detail:{challengeType:'device_check_invisible_mode'}}));
                    window.dispatchEvent(new CustomEvent('dd_response_passed',{detail:{challengeType:'device_check_invisible_mode'}}));
                    return fetchOriginal(input,init);
                  }
                  const frame=document.createElement('iframe');frame.src='/slider?id='+id;
                  frame.style='position:fixed;inset:0;width:100%;height:100%;border:0';document.body.append(frame);
                  window.dispatchEvent(new CustomEvent('dd_response_displayed',{detail:{challengeType:'block'}}));
                  await new Promise(resolve=>{
                    function done(event){if(event.origin!==location.origin||event.source!==frame.contentWindow||event.data!=='passed')return;
                      window.removeEventListener('message',done);document.cookie='datadome=verified; Path=/';
                      localStorage.setItem('ddSession_datadome','verified');frame.remove();
                      window.dispatchEvent(new CustomEvent('dd_response_passed',{detail:{challengeType:'block'}}));resolve();}
                    window.addEventListener('message',done);
                  });
                  return fetchOriginal(input,init);
                };
                </script></body></html>
                """, "text/html");
        });
        server.MapGet("/slider", (HttpContext context) => Results.Content("""
            <!doctype html><html><body style="background:#151518;color:white;font:24px sans-serif;padding:80px">
            <h1>Slider fixture</h1><input type="range"><p>Waiting for local verification</p>
            <script>fetch('/solve'+location.search).then(()=>parent.postMessage('passed',location.origin));</script></body></html>
            """, "text/html"));
        server.MapGet("/solve", async context =>
        {
            var id = int.Parse(context.Request.Query["id"].ToString());
            await releases.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(context.RequestAborted);
        });
        server.MapGet("/me", (HttpContext context) => context.Request.Headers.Authorization ==
            (Volatile.Read(ref expiredDuringUse) ? "OAuth fixture-token-renewed" : "OAuth fixture-token")
            ? Results.Json(new { id = 42, username = "Fixture" }) : Results.Unauthorized());
        server.MapGet("/users/42/conversations", (HttpContext context) =>
        {
            Interlocked.Increment(ref chatRequests);
            return Volatile.Read(ref expiredDuringUse) && context.Request.Headers.Authorization == "OAuth fixture-token-renewed"
                ? Results.Json(new { collection = Array.Empty<object>() }) : Results.Unauthorized();
        });
        server.MapPost("/api/refresh-token", (HttpContext context) =>
        {
            var number = Interlocked.Increment(ref refreshes);
            if (number is not (1 or 2) || (number == 2) != Volatile.Read(ref expiredDuringUse) ||
                context.Request.Cookies["oauth_refresh_token"] != (number == 1 ? "fixture-refresh" : "fixture-refresh-rotated") ||
                context.Request.Headers.ContainsKey("Authorization") || context.Request.QueryString.HasValue)
                return Results.BadRequest();
            context.Response.Cookies.Append("oauth_token", number == 1 ? "fixture-token" : "fixture-token-renewed",
                new CookieOptions { MaxAge = TimeSpan.FromDays(7) });
            context.Response.Cookies.Append("oauth_refresh_token", number == 1 ? "fixture-refresh-rotated" : "fixture-refresh-renewed",
                new CookieOptions { HttpOnly = true, MaxAge = TimeSpan.FromDays(7) });
            return Results.NoContent();
        });
        server.MapGet("/me/play-history/tracks", (HttpContext context) => context.Request.Headers.Authorization == "OAuth fixture-token"
            ? Results.Json(new { collection = new[] { new { track = new { id = 32, title = "Server history fixture" } } } }) : Results.Unauthorized());
        server.MapMethods("/users/42/track_likes/{id:int}", ["PUT", "DELETE"], (int id, HttpContext context) =>
        {
            if (context.Request.Headers.Authorization != "OAuth fixture-token" || context.Request.Query["client_id"] != "fixture")
                throw new InvalidOperationException("Wrong web credentials or route.");
            var count = calls.AddOrUpdate(id, 1, (_, value) => value + 1);
            if (id is 17 or 18 or 19 or 20 or 22 && count == 1) return Results.Json(new { url = "https://geo.captcha-delivery.com/captcha/fixture" }, statusCode: 403);
            if (id == 22) return Results.StatusCode(429);
            return Results.NoContent();
        });
        await server.StartAsync();
        var profile = Directory.CreateTempSubdirectory("pookie-persistence-test-web-challenge-").FullName;
        MainWindow? tested = null;
        try
        {
            await AppHost.RunAsync(new() { RequireWidevineSetup = false, SkipSessionRestore = true, RequireSignIn = true, IsolatedData = true, AudioEnabled = false, DiscordPresence = false,
                SystemMediaSession = false }, window =>
            {
                tested = window;
                window.Window.Loaded += () => window.StartWebChallengeVerification(server.Urls.Single(), profile,
                    id => releases.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(),
                    id => calls.GetValueOrDefault(id), () => Volatile.Read(ref documents),
                    () => Volatile.Write(ref expiredDuringUse, true));
            });
            if (tested?.VerificationFailure is { } error) throw new InvalidOperationException("Embedded web challenge failed.", error);
            if (refreshes != 2 || chatRequests != 3)
                throw new InvalidOperationException("Expected one startup refresh, one runtime refresh, and one retry of the expired chat poll.");
        }
        finally
        {
            foreach (var release in releases.Values) release.TrySetResult();
            await server.StopAsync();
            await Browser.BrowserPersistenceSmokeTest.DeleteFixtureProfileAsync(profile);
        }
    }
}
