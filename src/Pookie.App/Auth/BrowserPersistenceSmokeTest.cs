using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.SoundCloud;

namespace Pookie.App.Auth;

internal static class BrowserPersistenceSmokeTest
{
    public static async Task RunAsync()
    {
        var root = Directory.CreateTempSubdirectory("pookie-persistence-test-").FullName;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            await using var app = builder.Build();
            var challenges = 0;
            var requests = 0;
            var rotated = false;
            app.MapGet("/login", async context =>
            {
                context.Response.Cookies.Append("oauth_token", "fixture-token", new CookieOptions { MaxAge = TimeSpan.FromDays(7) });
                context.Response.Cookies.Append("datadome", "fixture-initial", new CookieOptions { MaxAge = TimeSpan.FromDays(7) });
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("""
                    <!doctype html><html lang="en"><title>Local Pookie login fixture</title><script>
                    localStorage.setItem('ddSession_datadome','fixture-initial');
                    window.ddoptions={sessionByHeader:true};
                    window.__sc_hydration=[{hydratable:'apiClient',data:{id:'fixtureid'}}];
                    </script></html>
                    """);
            });
            app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("""
                    <!doctype html><html><title>Local Pookie persistence fixture</title><script>
                    window.ddSbh=true;window.dataDomeOptions={ddCookieSessionName:'ddSession_datadome'};
                    const original=window.fetch.bind(window);
                    window.fetch=async (...args)=>{
                      args[1].headers['x-fixture-session']=localStorage.getItem('ddSession_datadome') || 'missing';
                      args[1].headers['x-fixture-cookie']=document.cookie.includes('datadome=fixture-rotated') ? 'rotated' :
                        document.cookie.includes('datadome=fixture-initial') ? 'initial' : 'missing';
                      const response=await original(...args);
                      if(response.status===403){
                        window.dispatchEvent(new CustomEvent('dd_blocked',{detail:{challengeType:'device_check'}}));
                        setTimeout(()=>{
                          document.cookie='datadome=fixture-rotated; Path=/; Max-Age=604800';
                          localStorage.setItem('ddSession_datadome','fixture-rotated');
                          window.dispatchEvent(new Event('dd_response_passed'));
                        },350);
                      }
                      return response;
                    };
                    window.dispatchEvent(new Event('dd_ready'));
                    setTimeout(()=>window.dispatchEvent(new Event('dd_post_done')),100);
                    </script></html>
                    """);
            });
            app.MapGet("/me", (HttpContext context) =>
            {
                if (context.Request.Headers["x-fixture-session"] != (rotated ? "fixture-rotated" : "fixture-initial") ||
                    context.Request.Headers["x-fixture-cookie"] != (rotated ? "rotated" : "initial"))
                    return Results.StatusCode(412);
                return Results.Json(new { id = 42, username = "fixture" });
            });
            app.MapPut("/users/42/track_likes/90", (HttpContext context) =>
            {
                Interlocked.Increment(ref requests);
                if (context.Request.Headers["x-fixture-session"] == "fixture-initial")
                { Interlocked.Increment(ref challenges); return Results.StatusCode(403); }
                if (context.Request.Headers["x-fixture-session"] != "fixture-rotated") return Results.StatusCode(412);
                rotated = true;
                return Results.NoContent();
            });
            await app.StartAsync();
            var uri = app.Urls.Single();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var profile = Path.Combine(root, "Profile");
            var login = await NativeWebLogin.ConnectAsync(timeout.Token, uri + "/login", profile);
            if (login.DataDomeClientId != "fixture-initial") throw new InvalidOperationException("Login protection state missing");
            var staleLogin = login with { };
            await using (var first = new NativeBrowserSession(login, uri + "/", profile))
            {
                await first.GetMeAsync(timeout.Token);
                await first.SetLikedAsync(42, 90, true, timeout.Token);
                if (first.Account.DataDomeClientId != "fixture-rotated") throw new InvalidOperationException("Rotated protection session not exported");
            }
            // Deliberately reuse the old login token: durable website state must take precedence.
            await using (var restarted = new NativeBrowserSession(staleLogin, uri + "/", profile))
            {
                await restarted.GetMeAsync(timeout.Token);
                await restarted.SetLikedAsync(42, 90, true, timeout.Token);
                if (restarted.Account.DataDomeClientId != "fixture-rotated") throw new InvalidOperationException("Latest session not restored after restart");
            }
            if (challenges != 1 || requests != 3) throw new InvalidOperationException("Restart requested another browser challenge");
            Console.WriteLine("BROWSER_PERSISTENCE_OK: login-to-worker cookies/localStorage, challenge rotation, graceful process exit, restart with stale login token and protected action without another check");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
