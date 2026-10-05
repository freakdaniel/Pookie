using InfiniFrame;
using InfiniFrame.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pookie.SoundCloud;
using System.Text.Json;

namespace Pookie.App.Auth;

internal static class InfiniFrameLoginWindow
{
    public static void RunRequests(WebSession account, Action<BrowserRequestEvent> received, string profilePath, string? fixtureUri)
    {
        var origin = fixtureUri == null ? "https://soundcloud.com" : new Uri(fixtureUri).GetLeftPart(UriPartial.Authority);
        var script = BrowserRequestScript.Read(account, origin);
        using var lifetime = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        var builder = InfiniFrameWindowBuilder.Create(services)
            .SetTitle("Pookie — проверка SoundCloud").SetSize(900, 760)
            .SetStartPageUrl(fixtureUri ?? "https://soundcloud.com/").SetMinimized(true)
            .SetTemporaryFilesPath(profilePath).EnableIgnoreCertificateErrors(false).EnableWebSecurity(true)
            .EnableFileSystemAccess(false).EnableJavascriptClipboardAccess(false).EnableBrowserPermissions(false)
            .EnableMediaStream(false).EnableMediaAutoplay(false).EnableDevTools(false).AddTrustedOrigin(origin);
        builder.RegisterWebMessageReceivedHandler((window, raw, source) =>
        {
            var message = BrowserRequestProtocol.Parse(raw, source, origin);
            if (message == null) return;
            if (message.Kind is "checking" or "blocked" && message.Interactive) BrowserWindowVisibility.Set(window, true);
            if (message.Kind is "ready" or "passed" or "challenge-error") BrowserWindowVisibility.Set(window, false);
            received(message);
        });
        builder.RegisterWindowCreatedHandler(window =>
        {
            BrowserWindowVisibility.Set(window, false);
            _ = ObservePagesAsync(window, script, lifetime.Token, hideWhenReady: true);
            _ = ReadCommandsAsync(window, origin, lifetime.Token);
        });
        try { builder.Build().WaitForClose(); }
        finally { lifetime.Cancel(); }
    }

    private static async Task ReadCommandsAsync(IInfiniFrameWindow window, string origin, CancellationToken token)
    {
        try
        {
            await window.WaitForReadyAsync(token);
            while (await Console.In.ReadLineAsync(token) is { } line)
            {
                if (line.Length > BrowserRequestCommand.MaxLength) continue;
                var command = JsonSerializer.Deserialize(line, SoundCloudJson.Default.BrowserRequestCommand);
                if (command?.IsValid() != true) continue;
                var script = "if(location.origin === " + JsonSerializer.Serialize(origin) + ") window.__pookieRequest?.(" +
                    JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand) + ");";
                await window.DispatchAsync(() => window.Features.JavaScript.SendEvalToBrowser(script), cancellationToken: token);
            }
            await window.DispatchAsync(window.Close, cancellationToken: token);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or JsonException or InvalidOperationException) { }
    }

    public static bool Run(Action<WebSession> connected, string profilePath, string? fixtureUri = null)
    {
        var origin = fixtureUri == null ? "https://soundcloud.com" : new Uri(fixtureUri).GetLeftPart(UriPartial.Authority);
        var script = LoginCaptureScript.Read(origin);
        using var lifetime = new CancellationTokenSource();
        var done = false;
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        var builder = InfiniFrameWindowBuilder.Create(services)
            .SetTitle("Pookie — вход на soundcloud.com")
            .SetSize(900, 760)
            .SetStartPageUrl(fixtureUri ?? "https://soundcloud.com/you/likes")
            .SetTemporaryFilesPath(profilePath)
            .EnableIgnoreCertificateErrors(false)
            .EnableWebSecurity(true)
            .EnableFileSystemAccess(false)
            .EnableJavascriptClipboardAccess(false)
            .EnableBrowserPermissions(false)
            .EnableMediaStream(false)
            .EnableMediaAutoplay(false)
            .EnableDevTools(false)
            .AddTrustedOrigin(origin);
        builder.RegisterWebMessageReceivedHandler((window, raw, source) =>
        {
            if (done) return;
            var session = LoginSessionMessage.Parse(raw, source, origin);
            if (session == null) return;
            connected(session);
            done = true;
            window.Close();
        });
        builder.RegisterWindowCreatedHandler(window =>
        {
            // Reinstall across full-page navigations without sharing MewUI's dispatcher.
            _ = ObservePagesAsync(window, script, lifetime.Token);
        });
        try
        {
            var window = builder.Build();
            window.WaitForClose();
            return done;
        }
        finally
        {
            lifetime.Cancel();
        }
    }

    private static async Task ObservePagesAsync(IInfiniFrameWindow window, string script, CancellationToken token, bool hideWhenReady = false)
    {
        try
        {
            await window.WaitForReadyAsync(token);
            if (hideWhenReady) await window.DispatchAsync(() => BrowserWindowVisibility.Set(window, false), cancellationToken: token);
            while (!window.IsClosedOrClosing())
            {
                await window.DispatchAsync(() => window.Features.JavaScript.SendEvalToBrowser(script), cancellationToken: token);
                await Task.Delay(300, token);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException) { }
    }
}
