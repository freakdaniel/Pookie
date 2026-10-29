using System.Net;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class MobileOAuthSmokeTest
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        string? challenge = null; var exchanges = 0; var refreshes = 0; var browserToken = "not-used"; var observerBlocked = false;
        var options = new SoundCloudMobileOAuthOptions("fixturemobileid", "fixture-secret", new("0123456789abcdef", "Fixture", "Device", "15"));
        app.MapGet("/authorize/{variant?}", async context =>
        {
            challenge = context.Request.Query["code_challenge"];
            if (context.Request.Query["code_challenge_method"] != "S256" || context.Request.Query["redirect_uri"] != "sc://auth" || challenge?.Length != 43)
                throw new InvalidOperationException("OAuth fixture received an invalid PKCE request.");
            context.Response.Cookies.Append("datadome", "fixture-mobile-cookie");
            context.Response.ContentType = "text/html; charset=utf-8";
            var state = context.Request.Query["state"].ToString();
            await context.Response.WriteAsync($$"""
                <!doctype html><title>Pookie mobile OAuth fixture</title><p>Local test account; no real credentials.</p>
                <button id="continue">Continue</button>
                <script>
                async function complete() {
                  const code = await (await fetch('/fixture-code')).text();
                  location.href='sc://auth?code='+encodeURIComponent(code)+'&state='+encodeURIComponent({{JsonSerializer.Serialize(state)}});
                }
                document.querySelector('#continue').addEventListener('click', complete);
                const ready = setInterval(async () => {
                  if (!window.__pookieMobileCookie) return;
                  clearInterval(ready);
                  if ({{JsonSerializer.Serialize(context.Request.RouteValues["variant"]?.ToString() == "stalled")}}) {
                    const messaging = window.infiniframe.messaging;
                    const send = messaging.sendMessageToHost.bind(messaging);
                    messaging.sendMessageToHost = (id, data) => {
                      if (id === '__infiniframe:javascript:eval:result') {
                        fetch('/fixture-observer-blocked', {method:'POST'}).then(() => document.querySelector('#continue').click());
                        return;
                      }
                      send(id, data);
                    };
                  } else document.querySelector('#continue').click();
                }, 20);
                </script>
                """);
        });
        app.MapGet("/fixture-code", () => Results.Text("fixture-authorization-code"));
        app.MapPost("/fixture-observer-blocked", () => { Volatile.Write(ref observerBlocked, true); return Results.NoContent(); });
        app.MapGet("/wait", () => Results.Content("<!doctype html><title>Pending local mobile login</title>", "text/html"));
        app.MapPost("/oauth/token", async context =>
        {
            var form = await context.Request.ReadFormAsync();
            if (form["client_id"] != options.ClientId || context.Request.Query["client_id"] != options.ClientId || form.ContainsKey("client_secret"))
                throw new InvalidOperationException("Mobile OAuth exposed or changed client credentials.");
            if (form["grant_type"] == "authorization_code")
            {
                var actualChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
                var auth = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(options.ClientId + ":" + options.ClientSecret)));
                if (actualChallenge != challenge || form["code"] != "fixture-authorization-code" || context.Request.Headers.Authorization != auth)
                    throw new InvalidOperationException("PKCE code exchange did not match the originating window.");
                exchanges++;
                await context.Response.WriteAsJsonAsync(new { access_token = "fixture-access-1", refresh_token = "fixture-refresh-1", expires_in = 3600 });
            }
            else
            {
                if (form["grant_type"] != "refresh_token" || form["refresh_token"] != "fixture-refresh-1" || context.Request.Headers.ContainsKey("Authorization") || ++refreshes != 1)
                    throw new InvalidOperationException("Single-use refresh token was reused or incorrectly authenticated.");
                await context.Response.WriteAsJsonAsync(new { access_token = "fixture-access-2", refresh_token = "fixture-refresh-2", expires_in = 3600 });
            }
        });
        app.MapGet("/me", (HttpContext context) => context.Request.Headers.Authorization == "OAuth fixture-access-2" || context.Request.Headers.Authorization == "OAuth " + browserToken
            ? Results.Json(new { id = 42, username = "fixture" }) : Results.StatusCode(401));
        app.MapGet("/browser", () => Results.Content("""
            <!doctype html><title>Local mobile session bridge</title><script>
            window.__pookieTagReady=true;window.__pookieProtectionReady=true;window.ddSbh=true;window.dataDomeOptions={};
            document.cookie='datadome=fixture-mobile-cookie; Path=/';
            </script>
            """, "text/html"));
        await app.StartAsync();
        var root = Directory.CreateTempSubdirectory("pookie-persistence-test-mobile-").FullName;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await VerifyFramedResultAsync(timeout.Token);
            using var http = new HttpClient(new FixtureRoutingHandler(new(app.Urls.Single()))) { Timeout = TimeSpan.FromSeconds(10) };
            var oauth = new SoundCloudMobileOAuth(http, options); var attempt = oauth.Begin("ru");
            var result = await NativeMobileLogin.AuthorizeAsync(attempt.Request, Path.Combine(root, "browser"), timeout.Token, app.Urls.Single() + "/authorize");
            if (result.Code != "fixture-authorization-code" || result.DataDomeClientId != "fixture-mobile-cookie")
                throw new InvalidOperationException("WebView did not transfer the bounded authorization code and protection cookie.");
            var current = await oauth.ExchangeAsync(attempt, result, timeout.Token);
            var paths = new AppDataPaths(Path.Combine(root, "vault"));
            using var vault = SessionVault.Open(paths); vault.Save(current);
            current = vault.Load() ?? throw new InvalidOperationException("OAuth session did not survive secure storage.");
            if (current.RefreshToken != "fixture-refresh-1" || current.ExpiresAtUtc == null || current.MobileIdentity == null)
                throw new InvalidOperationException("Secure storage lost refresh token, expiry or mobile identity.");
            using var manager = new SoundCloudMobileSessionManager(oauth, () => current, updated => current = updated,
                (updated, _) => { vault.Save(updated); return Task.CompletedTask; });
            using var native = new SoundCloudHttpTransport(http, () => current, authorization: manager);
            using var profile = await native.GetJsonAsync(new("https://api-v2.soundcloud.com/me"), timeout.Token);
            if (exchanges != 1 || refreshes != 1 || profile.RootElement.GetProperty("id").GetInt64() != 42 || vault.Load()?.RefreshToken != "fixture-refresh-2")
                throw new InvalidOperationException("OAuth exchange, reactive refresh or durable rotation failed.");

            var initial = current with { AccessToken = "old-browser-token" };
            await using (var browser = new NativeBrowserSession(initial, app.Urls.Single() + "/browser", Path.Combine(root, "browser")))
            {
                browserToken = "old-browser-token";
                await browser.GetMeAsync(timeout.Token);
                browserToken = "not-used";
                browser.UpdateAccount(current);
                await browser.GetMeAsync(timeout.Token);
            }
            if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("POOKIE_LOGIN_ENGINE") == "infiniframe")
            {
                using var stalledTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var stalledAttempt = oauth.Begin();
                try
                {
                    var stalled = await NativeMobileLogin.AuthorizeAsync(stalledAttempt.Request, Path.Combine(root, "browser"),
                        stalledTimeout.Token, app.Urls.Single() + "/authorize/stalled");
                    if (!Volatile.Read(ref observerBlocked) || stalled.Code != "fixture-authorization-code")
                        throw new InvalidOperationException("Fixture did not block the actual observer evaluation before Continue.");
                }
                catch (OperationCanceledException error) when (stalledTimeout.IsCancellationRequested)
                { throw new InvalidOperationException("OAuth callback waited for manual window closure while its JS observer was pending.", error); }
                Console.WriteLine("MOBILE_OAUTH_STALLED_OBSERVER_OK: Continue callback completed while the real JS evaluation reply was withheld; no manual window close");
            }
            using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await NativeMobileLogin.AuthorizeAsync(oauth.Begin().Request, Path.Combine(root, "cancel"), cancelled.Token, app.Urls.Single() + "/wait");
                throw new InvalidOperationException("Pending mobile login ignored cancellation.");
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
            { Console.WriteLine("MOBILE_OAUTH_CANCEL_OK: pending browser process stopped"); }
            vault.Delete();
            Console.WriteLine("MOBILE_OAUTH_OK: real WebView callback, PKCE, private pipe, secure storage, single-use refresh, API retry and live browser token update; fixture account only");
        }
        finally { await BrowserPersistenceSmokeTest.DeleteFixtureProfileAsync(root); }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task VerifyFramedResultAsync(CancellationToken token)
    {
        var name = "pookie-oauth-fixture-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(token), client.ConnectAsync(token));
        using var reader = new StreamReader(server, Encoding.UTF8);
        using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
        var message = JsonSerializer.Serialize(new MobileAuthorizationResult("fixture-framed-code"), SoundCloudJson.Default.MobileAuthorizationResult);
        var receiving = NativeMobileLogin.ReadAuthorizationResultAsync(reader, token).WaitAsync(TimeSpan.FromSeconds(2), token);
        await writer.WriteLineAsync(message.AsMemory(), token);
        // The writer intentionally remains open: waiting for EOF would hang.
        var result = await receiving;
        if (result?.Code != "fixture-framed-code") throw new InvalidOperationException("OAuth result required pipe EOF or lost its code.");
        using var oversized = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 32769))));
        try
        {
            await NativeMobileLogin.ReadAuthorizationResultAsync(oversized, token);
            throw new InvalidOperationException("Oversized OAuth pipe message was accepted.");
        }
        catch (InvalidOperationException error) when (error.Message == "Слишком большой ответ мобильного входа.")
        { Console.WriteLine("MOBILE_OAUTH_FRAMING_OK: bounded result completes before pipe EOF"); }
    }

    private sealed class FixtureRoutingHandler(Uri origin) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host is not ("secure.soundcloud.com" or "api-v2.soundcloud.com")) throw new InvalidOperationException("Fixture tried a foreign host.");
            request.RequestUri = new Uri(origin, request.RequestUri.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
