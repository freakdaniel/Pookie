using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pookie.App.Browser;

internal static class LoginSmokeTest
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        var cleanProfileSeen = false;
        var preservedProfileSeen = false;
        app.MapGet("/profile-{state}", async context =>
        {
            var state = context.Request.RouteValues["state"]?.ToString();
            var oldSession = context.Request.Cookies["oauth_token"] == "fixture-token" &&
                context.Request.Cookies["fixture-http-only"] == "old-account";
            if (state == "preserved") preservedProfileSeen = oldSession;
            if (state == "clean") cleanProfileSeen = !context.Request.Cookies.ContainsKey("oauth_token") &&
                !context.Request.Cookies.ContainsKey("fixture-http-only");
            context.Response.Cookies.Append("oauth_token", "fixture-token", new CookieOptions { MaxAge = TimeSpan.FromDays(7) });
            if (state == "seed") context.Response.Cookies.Append("fixture-http-only", "old-account",
                new CookieOptions { HttpOnly = true, MaxAge = TimeSpan.FromDays(7) });
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync($$"""
                <!doctype html><html lang="ru"><title>Local profile reset fixture</title><script>
                const state = {{JsonSerializer.Serialize(state)}};
                if (state === 'seed') localStorage.setItem('fixture-account', 'old-account');
                const validStorage = state === 'clean' ? localStorage.getItem('fixture-account') === null :
                    localStorage.getItem('fixture-account') === 'old-account';
                if (validStorage) window.__sc_hydration = [{hydratable:'apiClient',data:{id:'fixtureid'} }];
                </script></html>
                """);
        });
        app.MapGet("/bridge-error", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("""
                <!doctype html><title>Local bridge failure fixture</title><script>
                Object.defineProperty(window, 'fetch', {get() {throw new Error('fixture evaluation failure');}});
                </script>
                """);
        });
        app.MapGet("/popup-login", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            var origin = app.Urls.Single();
            var provider = new UriBuilder(origin) { Host = "localhost", Path = "/popup-provider" }.Uri.AbsoluteUri;
            await context.Response.WriteAsync($$"""
                <!doctype html><html lang="ru"><title>Pookie: проверка возврата из окна входа</title>
                <h1>Локальная проверка дочернего окна</h1><p>Вымышленный провайдер на другом origin.</p>
                <script>
                window.ddoptions = {sessionByHeader:true};
                window.__sc_version = '123';
                window.fetch = () => Promise.resolve(new Response('{}'));
                let providerWindow;
                addEventListener('message', event => {
                    if (event.origin !== {{JsonSerializer.Serialize(origin)}} || event.source !== providerWindow ||
                        event.data !== 'fixture-login-complete' || localStorage.getItem('pookie-fixture-popup') !== 'returned') return;
                    window.__sc_hydration = [{hydratable:'apiClient',data:{id:'fixtureid'} }];
                });
                setTimeout(() => providerWindow = window.open({{JsonSerializer.Serialize(provider)}}, 'pookie-login-fixture',
                    'popup,width=600,height=600'), 1000);
                </script>
                """);
        });
        app.MapGet("/popup-provider", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync($$"""
                <!doctype html><title>Локальный провайдер входа</title><script>
                location.replace({{JsonSerializer.Serialize(app.Urls.Single() + "/popup-callback")}});
                </script>
                """);
        });
        app.MapGet("/popup-callback", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Cookies.Append("oauth_token", "fixture-token");
            context.Response.Cookies.Append("datadome", "fixture-protection");
            await context.Response.WriteAsync($$"""
                <!doctype html><title>Локальный возврат из входа</title><script>
                localStorage.setItem('pookie-fixture-popup', 'returned');
                if (window.opener) {
                    window.opener.postMessage('fixture-login-complete', {{JsonSerializer.Serialize(app.Urls.Single())}});
                    window.close();
                }
                </script>
                """);
        });
        app.MapGet("/{mode}", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            var mode = context.Request.RouteValues["mode"]?.ToString();
            if (mode == "cookie") context.Response.Cookies.Append("oauth_token", "fixture-token");
            context.Response.Cookies.Append("datadome", "fixture-protection");
            var html = """
                <!doctype html><html lang="ru"><title>Pookie: проверка окна входа</title>
                <h1>Тест передачи сессии InfiniFrame</h1><p>Используются вымышленные данные, без SoundCloud.</p>
                <script>
                window.ddoptions = {sessionByHeader:true};
                window.__sc_version = '123';
                window.__sc_hydration = [{hydratable:'apiClient',data:{id:'fixtureid'}}];
                if (!window.__pookieLoginCapture) window.fetch = () => Promise.resolve(new Response('{}'));
                setInterval(() => window.fetch('https://api-v2.soundcloud.com/me?client_id=fixtureid',
                    {headers:{Authorization:'OAuth fixture-token'}}), 600);
                </script>
                """;
            if (mode == "wait") html = html.Replace("Authorization:'OAuth fixture-token'", "Authorization:'Bearer invalid'");
            await context.Response.WriteAsync(html);
        });
        await app.StartAsync();
        // A cancelled/unfinished previous login can still have durable cookies, including HttpOnly cookies.
        // Verify the same profile is actually populated first, then starts clean on a new sign-in attempt.
        var resetRoot = Directory.CreateTempSubdirectory("pookie-persistence-test-").FullName;
        try
        {
            using var resetTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            foreach (var state in new[] { "seed", "preserved", "clean" })
            {
                var session = await NativeWebLogin.ConnectAsync(resetTimeout.Token,
                    app.Urls.Single() + "/profile-" + state, resetRoot, resetSession: state != "preserved");
                if (session.ClientId != "fixtureid" || session.AccessToken != "fixture-token")
                    throw new InvalidOperationException("Fresh login did not transfer the fixture session");
            }
            if (!preservedProfileSeen || !cleanProfileSeen)
                throw new InvalidOperationException("Login profile reset did not remove the previous account cookies");
            Console.WriteLine("LOGIN_RESET_OK: populated profile reused, then cookies/HttpOnly cookies/localStorage removed before fresh login");
        }
        finally { await BrowserPersistenceSmokeTest.DeleteFixtureProfileAsync(resetRoot); }
        foreach (var mode in new[] { "fetch", "cookie", "popup-login" })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var session = await NativeWebLogin.ConnectAsync(timeout.Token, app.Urls.Single() + "/" + mode);
            if (session.ClientId != "fixtureid" || session.AccessToken != "fixture-token" || string.IsNullOrWhiteSpace(session.UserAgent) ||
                session.DataDomeClientId != "fixture-protection" || session.AppVersion != "123" || session.AppLocale != "ru")
                throw new InvalidOperationException("Ошибка передачи тестовой сессии.");
            if (OperatingSystem.IsWindows() && (!session.UserAgent.StartsWith("Mozilla/5.0", StringComparison.Ordinal) ||
                !session.UserAgent.Contains("Windows", StringComparison.Ordinal) || session.UserAgent.Contains("InfiniFrame", StringComparison.Ordinal)))
                throw new InvalidOperationException("Окно входа потеряло штатный User-Agent WebView2.");
            Console.WriteLine($"LOGIN_SMOKE_OK: WebView {mode}, origin check, anonymous pipe, window close");
        }
        if (OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("POOKIE_LOGIN_ENGINE") == "infiniframe")
        {
            using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await NativeWebLogin.ConnectAsync(failureTimeout.Token, app.Urls.Single() + "/bridge-error");
                throw new InvalidOperationException("Ошибка JavaScript не остановила передачу сессии.");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("Не удалось передать сессию", StringComparison.Ordinal))
            { Console.WriteLine("LOGIN_BRIDGE_ERROR_OK: evaluation failure reaches caller and closes the host without hanging"); }
        }
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await NativeWebLogin.ConnectAsync(cancel.Token, app.Urls.Single() + "/wait");
            throw new InvalidOperationException("Окно входа не отменило ожидание сессии.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        { Console.WriteLine("LOGIN_CANCEL_OK: pending login process and WebView children stopped"); }
    }
}
