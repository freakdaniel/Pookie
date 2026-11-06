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
        server.MapGet("/", (HttpContext context) =>
        {
            Interlocked.Increment(ref documents);
            if (!context.Request.Cookies.ContainsKey("datadome")) context.Response.Cookies.Append("datadome", "initial");
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
        server.MapGet("/me", () => Results.Json(new { id = 42, username = "Fixture" }));
        server.MapMethods("/users/42/track_likes/{id:int}", ["PUT", "DELETE"], (int id, HttpContext context) =>
        {
            if (context.Request.Headers.Authorization != "OAuth fixture-token" || context.Request.Query["client_id"] != "fixture")
                throw new InvalidOperationException("Wrong web credentials or route.");
            var count = calls.AddOrUpdate(id, 1, (_, value) => value + 1);
            if (id is 17 or 18 or 19 or 20 && count == 1) return Results.Json(new { url = "https://geo.captcha-delivery.com/captcha/fixture" }, statusCode: 403);
            return Results.NoContent();
        });
        await server.StartAsync();
        var profile = Directory.CreateTempSubdirectory("pookie-persistence-test-web-challenge-").FullName;
        MainWindow? tested = null;
        try
        {
            await AppHost.RunAsync(new() { Preview = true, IsolatedData = true, AudioEnabled = false, DiscordPresence = false,
                SystemMediaSession = false }, window =>
            {
                tested = window;
                window.Window.Loaded += () => window.StartWebChallengeVerification(server.Urls.Single(), profile,
                    id => releases.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(),
                    id => calls.GetValueOrDefault(id), () => Volatile.Read(ref documents));
            });
            if (tested?.VerificationFailure is { } error) throw new InvalidOperationException("Embedded web challenge failed.", error);
        }
        finally
        {
            foreach (var release in releases.Values) release.TrySetResult();
            await server.StopAsync();
            await Browser.BrowserPersistenceSmokeTest.DeleteFixtureProfileAsync(profile);
        }
    }
}
