using Pookie.App.Browser;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class ContentBlockerSmokeTest
{
    public static async Task RunAsync()
    {
        string[] blocked = ["https://pixel.quantserve.com/pookie-fixture", "https://cm.g.doubleclick.net/pookie-fixture",
            "https://telemetry.soundcloud.com/events", "https://api-v2.soundcloud.com/audio-ads?client_id=fixture",
            "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js", "https://PIXEL.QUANTSERVE.COM.:443/pookie-fixture"];
        string[] allowed = ["https://api-v2.soundcloud.com/me", "https://api-v2.soundcloud.com/tracks/42",
            "https://license.media-streaming.soundcloud.cloud/playback/widevine", "https://cf-hls-media.sndcdn.com/track.m3u8",
            "https://a-v2.sndcdn.com/assets/app.js", "https://geo.captcha-delivery.com/captcha/",
            "https://dwt.soundcloud.com.first-party-js.datadome.co/tags.js", "https://accounts.google.com/o/oauth2/auth",
            "https://appleid.apple.com/auth/authorize", "https://www.facebook.com/dialog/oauth",
            "https://quantserve.com.example.org/", "https://example.org/?url=https://pixel.quantserve.com/",
            "https://soundcloud.com/audio-ads-song", "https://soundcloud.com/user/track", "http://localhost:5000/me"];
        using var webkit = JsonDocument.Parse(BrowserContentBlocker.CreateWebKitRules());
        var patterns = webkit.RootElement.EnumerateArray().Select(rule =>
            new Regex(rule.GetProperty("trigger").GetProperty("url-filter").GetString()!, RegexOptions.IgnoreCase)).ToArray();
        foreach (var url in blocked.Concat(allowed))
        {
            var expected = blocked.Contains(url);
            if (BrowserContentBlocker.ShouldBlock(url) != expected || patterns.Any(pattern => pattern.IsMatch(url)) != expected)
                throw new InvalidOperationException("Platform filters disagree or block an essential service: " + url);
        }

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var allowedRequests = 0;
        server.MapGet("/allowed.js", async context =>
        {
            Interlocked.Increment(ref allowedRequests);
            context.Response.ContentType = "application/javascript";
            await context.Response.WriteAsync("window.fixtureAllowed = true;");
        });
        server.MapGet("/", async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("""
                <!doctype html><title>Pookie content filter fixture</title>
                <script src="/allowed.js"></script><script>
                window.ddSbh = true; window.dataDomeOptions = {};
                document.cookie = 'datadome=fixture-content-filter; Path=/';
                const original = window.fetch.bind(window);
                const rejectsBlockedResources = __WEBKIT_BLOCKING__;
                const checks = Promise.all([
                  'https://pixel.quantserve.com/pookie-filter-fixture',
                  'https://cm.g.doubleclick.net/pookie-filter-fixture'
                ].map(url => original(url, {mode:'no-cors'}).then(response => {
                  if (response.type !== 'opaque') throw Error('Unexpected blocked response');
                }, error => {
                  if (!rejectsBlockedResources || error.name !== 'TypeError') throw error;
                })));
                window.fetch = async (...args) => {
                  await checks;
                  args[1].headers['x-fixture-blocked'] = '2';
                  args[1].headers['x-fixture-allowed'] = String(window.fixtureAllowed);
                  return original(...args);
                };
                window.dispatchEvent(new Event('dd_ready'));
                setTimeout(() => window.dispatchEvent(new Event('dd_post_done')), 100);
                </script>
                """.Replace("__WEBKIT_BLOCKING__", OperatingSystem.IsLinux() ? "true" : "false"));
        });
        server.MapGet("/me", (HttpContext context) =>
        {
            if (context.Request.Headers["x-fixture-blocked"] != "2" || context.Request.Headers["x-fixture-allowed"] != "true" ||
                context.Request.Headers.Authorization != "OAuth fixture-token")
                throw new InvalidOperationException("Native filter failed or the authorized API lost its request headers.");
            return Results.Json(new { id = 42, username = "fixture" });
        });
        await server.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var browser = new NativeBrowserSession(new("fixtureid", "fixture-token", "WebKit"), server.Urls.Single() + "/");
        var user = await browser.GetMeAsync(timeout.Token);
        if (user.Id != 42 || allowedRequests != 1) throw new InvalidOperationException("Allowed resource or profile verification failed.");
        Console.WriteLine("CONTENT_BLOCKER_OK: native requests filtered; page assets, authorized API, DRM and login allow rules verified");
    }
}
