using Pookie.App.Diagnostics;
using InfiniFrame;
using InfiniFrame.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pookie.SoundCloud;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pookie.App.Browser;

internal static class InfiniFrameLoginWindow
{
    private const string BridgeMessageId = "pookie:browser-message";
    public static void RunRequests(WebSession account, Action<BrowserRequestEvent> received, string profilePath, string? fixtureUri)
    {
        var origin = fixtureUri == null ? "https://soundcloud.com" : new Uri(fixtureUri).GetLeftPart(UriPartial.Authority);
        var pairing = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var script = CreateBridgeScript(origin, pairing, BrowserRequestScript.Read(account, origin));
        using var lifetime = new CancellationTokenSource();
        Exception? bridgeError = null;
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        var builder = InfiniFrameWindowBuilder.Create(services)
            .SetUserAgent(null) // Preserve the engine's native UA instead of InfiniFrame's default label.
            .SetTitle("Pookie — проверка SoundCloud").SetSize(900, 760)
            // A minimized Win32 window gives WebView2 a zero-sized viewport. Hide the full-sized window instead.
            .SetStartPageUrl(fixtureUri ?? "https://soundcloud.com/").SetMinimized(!OperatingSystem.IsWindows())
            .SetTemporaryFilesPath(profilePath).EnableIgnoreCertificateErrors(false).EnableWebSecurity(true)
            .EnableFileSystemAccess(false).EnableJavascriptClipboardAccess(false).EnableBrowserPermissions(false)
            .EnableMediaStream(false).EnableMediaAutoplay(OperatingSystem.IsWindows()).EnableDevTools(false).AddTrustedOrigin(origin);
        // Explicit, local-only diagnostic runs may observe startup traffic through CDP.
        if (OperatingSystem.IsWindows() && int.TryParse(Environment.GetEnvironmentVariable("POOKIE_STARTUP_DEBUG_PORT"), out var debugPort) &&
            debugPort is >= 1024 and <= 65535) builder.SetRemoteDebuggingPort(debugPort);
        var navigationObserved = false;
        builder.RegisterNavigationStartingHandler((_, navigation) =>
        {
            if (navigation.IsMainFrame && !navigationObserved)
            {
                navigationObserved = true;
                StartupLog.Event("browser.initial-navigation-start");
            }
            return NavigationStartingResult.Allow;
        });
        builder.RegisterWebMessagePostHandler(BridgeMessageId, (window, raw) =>
        {
            if (raw == null) return;
            var message = BrowserRequestProtocol.Parse(raw, window.GetCurrentUrl(), origin, pairing);
            if (message == null) return;
            if (message.Kind is "checking" or "blocked" && message.Interactive) BrowserWindowVisibility.Set(window, true);
            if (message.Kind is "ready" or "passed" or "challenge-error") BrowserWindowVisibility.Set(window, false);
            received(message);
        });
        builder.RegisterWindowCreatedHandler(window =>
        {
            BrowserContentBlocker.Attach(window);
            StartupLog.Event("browser.window-created");
            if (OperatingSystem.IsWindows()) BrowserWindowVisibility.UseForPlayback(window);
            else BrowserWindowVisibility.Set(window, false);
            if (OperatingSystem.IsWindows())
            {
                var scale = window.Features.Monitors.GetMainMonitorScreenDpi() / 96d;
                window.Features.Size.SetSize((int)Math.Ceiling(900 * scale), (int)Math.Ceiling(760 * scale));
            }
            _ = RunBridgeTaskAsync(window, () => ObservePagesAsync(window, script, lifetime.Token, hideWhenReady: true),
                error => Interlocked.CompareExchange(ref bridgeError, error, null), lifetime.Token);
            _ = RunBridgeTaskAsync(window, () => ReadCommandsAsync(window, origin, lifetime.Token),
                error => Interlocked.CompareExchange(ref bridgeError, error, null), lifetime.Token);
        });
        try { builder.Build().WaitForClose(); }
        finally { lifetime.Cancel(); }
        if (bridgeError != null) throw new BridgeException(bridgeError);
    }

    private static async Task ReadCommandsAsync(IInfiniFrameWindow window, string origin, CancellationToken token)
    {
        await window.WaitForReadyAsync(token);
        while (await Console.In.ReadLineAsync(token) is { } line)
        {
            if (line.Length > BrowserRequestCommand.MaxLength) continue;
            var command = JsonSerializer.Deserialize(line, SoundCloudJson.Default.BrowserRequestCommand);
            if (command?.IsValid() != true) continue;
            var script = "if(location.origin === " + JsonSerializer.Serialize(origin) + ") window.__pookieRequest?.(" +
                JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand) + ");";
            await window.Features.JavaScript.ExecuteJavaScriptAsync(AsExpression(script), token);
        }
        await window.DispatchAsync(window.Close, cancellationToken: token);
    }

    public static bool Run(Action<WebSession> connected, string profilePath, string? fixtureUri = null)
    {
        var origin = fixtureUri == null ? "https://soundcloud.com" : new Uri(fixtureUri).GetLeftPart(UriPartial.Authority);
        var pairing = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var script = CreateBridgeScript(origin, pairing, LoginCaptureScript.Read(origin));
        using var lifetime = new CancellationTokenSource();
        Exception? bridgeError = null;
        var done = false;
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        var builder = InfiniFrameWindowBuilder.Create(services)
            .SetUserAgent(null)
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
        builder.RegisterWebMessagePostHandler(BridgeMessageId, (window, raw) =>
        {
            if (done || raw == null) return;
            var session = LoginSessionMessage.Parse(raw, window.GetCurrentUrl(), origin, pairing);
            if (session == null) return;
            connected(session);
            done = true;
            window.Close();
        });
        builder.RegisterWindowCreatedHandler(window =>
        {
            BrowserContentBlocker.Attach(window);
            // Reinstall across full-page navigations without sharing MewUI's dispatcher.
            _ = RunBridgeTaskAsync(window, () => ObservePagesAsync(window, script, lifetime.Token),
                error => Interlocked.CompareExchange(ref bridgeError, error, null), lifetime.Token);
        });
        try
        {
            var window = builder.Build();
            window.WaitForClose();
            if (bridgeError != null) throw new BridgeException(bridgeError);
            return done;
        }
        finally
        {
            lifetime.Cancel();
        }
    }

    // InfiniFrame evaluates an expression with `return (...)`, not a script body.
    private static string AsExpression(string script) => "(() => {\n" + script + "\n})()";

    // Valid v2 messages go through InfiniFrame's named Post handler, not its raw-message event.
    // The capability is installed only in the permitted top frame, as in the WebKit bridge.
    private static string CreateBridgeScript(string origin, string pairing, string script) =>
        "if(location.origin === " + JsonSerializer.Serialize(origin) + " && window === window.top) {\n" +
        "const pairing = " + JsonSerializer.Serialize(pairing) + ";\n" +
        "window.__pookiePost = message => window.infiniframe.host.postData({id:" + JsonSerializer.Serialize(BridgeMessageId) +
        ",command:'Post',version:2,data:{...message,pookie_pairing:pairing}});\n" + script + "\n}";

    private static async Task ObservePagesAsync(IInfiniFrameWindow window, string script, CancellationToken token, bool hideWhenReady = false)
    {
        await window.WaitForReadyAsync(token);
        if (hideWhenReady) StartupLog.Event("browser.initial-navigation-ready");
        var firstInjection = true;
        if (hideWhenReady) await window.DispatchAsync(() => BrowserWindowVisibility.Set(window, false), cancellationToken: token);
        while (!window.IsClosedOrClosing())
        {
            await window.Features.JavaScript.ExecuteJavaScriptAsync(AsExpression(script), token);
            if (firstInjection && hideWhenReady) { StartupLog.Event("browser.bridge-installed"); firstInjection = false; }
            await Task.Delay(300, token);
        }
    }

    // An evaluation failure must terminate the host and reach the caller, rather than fault an unobserved task.
    private static async Task RunBridgeTaskAsync(IInfiniFrameWindow window, Func<Task> operation, Action<Exception> failed, CancellationToken token)
    {
        try { await operation(); }
        catch (Exception) when (token.IsCancellationRequested || window.IsClosedOrClosing())
        {
            // Closing the window cancels pending evaluations and input reads.
            return;
        }
        catch (Exception error)
        {
            failed(error);
            await window.DispatchAsync(window.Close, cancellationToken: token);
        }
    }

    internal sealed class BridgeException(Exception error) : Exception("Не удалось подключить браузерную сессию SoundCloud.", error);
}
