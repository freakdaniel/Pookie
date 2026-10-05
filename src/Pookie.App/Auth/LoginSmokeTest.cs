using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pookie.App.Auth;

internal static class LoginSmokeTest
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
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
        foreach (var mode in new[] { "fetch", "cookie" })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var session = await NativeWebLogin.ConnectAsync(timeout.Token, app.Urls.Single() + "/" + mode);
            if (session.ClientId != "fixtureid" || session.AccessToken != "fixture-token" || string.IsNullOrWhiteSpace(session.UserAgent) ||
                session.DataDomeClientId != "fixture-protection" || session.AppVersion != "123" || session.AppLocale != "ru")
                throw new InvalidOperationException("Ошибка передачи тестовой сессии.");
            Console.WriteLine($"LOGIN_SMOKE_OK: WebView {mode}, origin check, anonymous pipe, window close");
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
