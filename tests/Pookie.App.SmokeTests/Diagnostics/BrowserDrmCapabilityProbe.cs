using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Browser;

namespace Pookie.App.Diagnostics;

// No real account, profile, media, licenses or SoundCloud requests are needed to
// distinguish AAC/MSE availability from Widevine key-system availability.
internal static class BrowserDrmCapabilityProbe
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var server = builder.Build();
        var completion = new TaskCompletionSource<(bool Api, bool Aac, bool Supported, string Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MapGet("/me", () => Results.Json(new { id = 42, username = "Fixture" }));
        server.MapPost("/drm-result", async context =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var value = body.RootElement;
            var error = value.GetProperty("error").GetString() ?? "";
            if (error.Length > 80 || error.Any(c => !char.IsAsciiLetter(c))) throw new InvalidDataException("Invalid capability error name.");
            completion.TrySetResult((value.GetProperty("api").GetBoolean(), value.GetProperty("aac").GetBoolean(),
                value.GetProperty("supported").GetBoolean(), error));
            context.Response.StatusCode = 204;
        });
        server.MapGet("/", () => Results.Content("""
            <!doctype html><title>Pookie DRM capability fixture</title><script>
            window.ddSbh=true;window.dataDomeOptions={};document.cookie='datadome=fixture-drm; Path=/';
            window.dispatchEvent(new Event('dd_ready'));
            setInterval(()=>window.dispatchEvent(new Event('dd_post_done')),200);
            (async()=>{
              const mime='audio/mp4; codecs="mp4a.40.2"';
              const result={api:typeof navigator.requestMediaKeySystemAccess==='function',aac:!!window.MediaSource && MediaSource.isTypeSupported(mime),supported:false,error:''};
              if(result.api){
                try{
                  const access=await navigator.requestMediaKeySystemAccess('com.widevine.alpha',[{initDataTypes:['cenc'],audioCapabilities:[{contentType:mime}],sessionTypes:['temporary']}]);
                  await access.createMediaKeys();result.supported=true;
                }catch(error){result.error=error.name;}
              }
              await fetch('/drm-result',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(result)});
            })();
            </script>
            """, "text/html"));
        await server.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var browser = new NativeBrowserSession(new("fixtureid", "fixture-token", "Chromium"), server.Urls.Single() + "/");
        await browser.GetMeAsync(timeout.Token);
        var result = await completion.Task.WaitAsync(timeout.Token);
        Console.WriteLine($"NATIVE_BROWSER_DRM_CAPABILITY: eme_api={result.Api}; aac_mse={result.Aac}; widevine={result.Supported}; error={result.Error}; isolated profile; no license request");
    }
}
