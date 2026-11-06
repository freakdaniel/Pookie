using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Hosting;

namespace Pookie.App.Diagnostics;

internal static class EmbeddedLoginSmokeTest
{
    public static async Task RunAsync(bool preview = false)
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var providerBuilder = WebApplication.CreateBuilder(); providerBuilder.Logging.ClearProviders(); providerBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var provider = providerBuilder.Build();
        var providerSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previewReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorizationRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorizationContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingDocument = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var formRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var formContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerNavigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupSeen = false;
        app.MapGet("/authorize", async (HttpContext context) =>
        {
            authorizationRequested.TrySetResult();
            await authorizationContinue.Task.WaitAsync(context.RequestAborted);
            context.Response.Cookies.Append("datadome", "embedded-cookie");
            var state = context.Request.Query["state"].ToString();
            return Results.Content($$"""
                <!doctype html><meta charset="utf-8"><title>Embedded OAuth fixture</title>
                <style>body{background:#3355bb;color:#eee;font:20px sans-serif;padding:60px}h1{font-size:32px}</style>
                <h1>Pookie — встроенный вход</h1><p>Loading the sign-in form, no real account.</p>
                <div id="sign-in" style="opacity:0;transition:opacity 180ms"><button>Sign in with provider</button></div>
                <script>
                window.addEventListener('load',async()=>{
                  await fetch('/form-wait');
                  document.getElementById('sign-in').style.opacity='1';
                  await fetch('/provider-start');
                  location.href={{JsonSerializer.Serialize(provider.Urls.Single() + "/provider?return_url=")}}+
                    encodeURIComponent({{JsonSerializer.Serialize(app.Urls.Single() + "/callback?state=" + Uri.EscapeDataString(state))}});
                });
                </script>
                """, "text/html");
        });
        app.MapGet("/form-wait", async context =>
        {
            formRequested.TrySetResult();
            await formContinue.Task.WaitAsync(context.RequestAborted);
        });
        app.MapGet("/provider-start", async context => await providerNavigation.Task.WaitAsync(context.RequestAborted));
        provider.MapGet("/provider", (HttpContext context) =>
        {
            context.Response.Headers.ContentSecurityPolicy = "script-src 'nonce-fixture'; object-src 'none'";
            return Results.Content($$"""
                <!doctype html><meta charset="utf-8"><title>Embedded external provider fixture</title>
                <style>body{background:#3355bb;color:#eee;font:20px sans-serif;padding:60px}h1{font-size:32px}</style>
                <h1>Вход внутри Pookie</h1><p>Локальный тест провайдера: без личного аккаунта и пароля.</p>
                <script nonce="fixture">
                fetch('/waiting?bridge='+Boolean(window.__pookiePost)).then(()=>{
                  location.href={{JsonSerializer.Serialize(context.Request.Query["return_url"].ToString())}};
                });
                </script>
                """, "text/html");
        });
        provider.MapGet("/waiting", async context =>
        {
            if (context.Request.Query["bridge"] != "false") throw new InvalidOperationException("Pookie bridge was exposed to the provider.");
            providerSeen.TrySetResult();
            await providerContinue.Task.WaitAsync(context.RequestAborted);
        });
        app.MapGet("/callback", (HttpContext context) =>
        {
            context.Response.Cookies.Append("oauth_token", "embedded-token");
            return Results.Content("""
            <!doctype html><title>Embedded web callback fixture</title><script>
            window.__sc_hydration=[{hydratable:'apiClient',data:{id:'embeddedid'}}];
            </script>
            """, "text/html");
        });
        app.MapGet("/web", (HttpContext context) =>
        {
            context.Response.Cookies.Append("datadome", "embedded-cookie");
            return Results.Content($$"""
            <!doctype html><title>Embedded website popup fixture</title><script>
            window.ddoptions={sessionByHeader:true};window.__sc_version='123';
            const ready=setInterval(()=>{
              if(!window.__pookiePost) return;
              clearInterval(ready);
              window.open({{JsonSerializer.Serialize(app.Urls.Single() + "/popup")}},'pookie-popup');
            },20);
            </script>
            """, "text/html");
        });
        app.MapGet("/popup", (HttpContext context) =>
        {
            popupSeen = true;
            context.Response.Cookies.Append("oauth_token", "embedded-token");
            return Results.Content("""
                <!doctype html><title>Embedded popup return</title><script>
                localStorage.setItem('fixture-popup-return','yes');
                window.opener.__sc_hydration=[{hydratable:'apiClient',data:{id:'embeddedid'}}];
                window.close();
                </script>
                """, "text/html");
        });
        app.MapGet("/wait", async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("<!doctype html><title>Pending embedded login</title>", context.RequestAborted);
            await pendingDocument.Task.WaitAsync(context.RequestAborted);
        });
        app.MapGet("/loaded", () => Results.Content("""
            <!doctype html><title>Ready login cancellation fixture</title>
            <style>body{background:#3355bb;color:white;font:20px sans-serif}</style><h1>Ready to sign in</h1><button>Sign in</button>
            """, "text/html"));
        if (preview) app.MapGet("/release-preview", () => { previewReleased.TrySetResult(); return Results.NoContent(); });
        await app.StartAsync(); await provider.StartAsync();
        if (preview) Console.WriteLine("EMBEDDED_PREVIEW_RELEASE: " + app.Urls.Single() + "/release-preview");
        var root = Directory.CreateTempSubdirectory("pookie-persistence-test-embedded-").FullName;
        MainWindow? tested = null;
        try
        {
            await AppHost.RunAsync(new() { Preview = true, RequireSignIn = true, SkipSessionRestore = true,
                IsolatedData = true, SilentAudio = true, AudioEnabled = false, DiscordPresence = false, SystemMediaSession = false }, window =>
            {
                tested = window;
                window.Window.Loaded += () => window.StartEmbeddedLoginVerification(app.Urls.Single(), root, providerSeen.Task,
                    () => providerContinue.TrySetResult(), authorizationRequested.Task, () => authorizationContinue.TrySetResult(),
                    formRequested.Task, () => formContinue.TrySetResult(), () => providerNavigation.TrySetResult(),
                    preview ? previewReleased.Task : null);
            });
            if (tested?.VerificationFailure is { } error) throw new InvalidOperationException("Embedded login verification failed.", error);
            if (!popupSeen) throw new InvalidOperationException("Embedded provider popup was not exercised.");
        }
        finally
        {
            providerContinue.TrySetResult();
            authorizationContinue.TrySetResult();
            pendingDocument.TrySetResult();
            formContinue.TrySetResult(); providerNavigation.TrySetResult();
            await Browser.BrowserPersistenceSmokeTest.DeleteFixtureProfileAsync(root);
        }
    }
}
