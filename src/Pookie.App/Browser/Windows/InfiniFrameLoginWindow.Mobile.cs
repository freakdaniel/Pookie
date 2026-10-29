using InfiniFrame;
using InfiniFrame.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pookie.App.Diagnostics;
using Pookie.SoundCloud;
using System.Security.Cryptography;

namespace Pookie.App.Browser;

internal static partial class InfiniFrameLoginWindow
{
    public static MobileAuthorizationResult? RunMobile(MobileAuthorizationRequest request, string profilePath, string? fixtureUri,
        Action<MobileAuthorizationResult>? authorized = null)
    {
        var startUrl = NativeMobileLogin.StartUrl(request, fixtureUri);
        var origin = new Uri(startUrl).GetLeftPart(UriPartial.Authority);
        var pairing = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var script = CreateBridgeScript(origin, pairing, NativeMobileLogin.CookieScript);
        var completed = new TaskCompletionSource<MobileAuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? bridgeError = null;
        string? cookie = null;
        using var lifetime = new CancellationTokenSource();
        var services = new ServiceCollection(); services.AddLogging(logging => logging.ClearProviders());
        var builder = InfiniFrameWindowBuilder.Create(services).SetUserAgent(null)
            .SetTitle("Pookie — мобильный вход SoundCloud").SetSize(900, 760).SetStartPageUrl(startUrl)
            .SetTemporaryFilesPath(profilePath).EnableIgnoreCertificateErrors(false).EnableWebSecurity(true)
            .EnableFileSystemAccess(false).EnableJavascriptClipboardAccess(false).EnableBrowserPermissions(false)
            .EnableMediaStream(false).EnableMediaAutoplay(false).EnableDevTools(false).AddTrustedOrigin(origin);
        DisableSystemMedia(builder);
        builder.RegisterNavigationStartingHandler((_, navigation) =>
        {
            if (!navigation.IsMainFrame) return NavigationStartingResult.Allow;
            var callback = request.ReadCallback(navigation.Url);
            if (callback != null)
            {
                if (completed.TrySetResult(callback with { DataDomeClientId = Volatile.Read(ref cookie) }))
                    StartupLog.Event("browser.mobile-login-callback");
                return NavigationStartingResult.Cancel;
            }
            return Uri.TryCreate(navigation.Url, UriKind.Absolute, out var target) && target.Scheme is "http" or "https" or "about"
                ? NavigationStartingResult.Allow : NavigationStartingResult.Cancel;
        });
        builder.RegisterWebMessagePostHandler(BridgeMessageId, (window, raw) =>
        {
            if (raw == null) return;
            var message = BrowserRequestProtocol.Parse(raw, window.GetCurrentUrl(), origin, pairing);
            if (message?.Kind == "protection-session") Volatile.Write(ref cookie, message.DataDomeClientId);
        });
        builder.RegisterWindowCreatedHandler(window =>
        {
            BrowserContentBlocker.Attach(window);
            // JS evaluation may never reply after a page transition/device check.
            // Completion has its own native dispatcher path and never awaits it.
            _ = RunBridgeTaskAsync(window, () => CompleteAsync(window),
                error => Interlocked.CompareExchange(ref bridgeError, error, null), lifetime.Token);
            _ = ObserveAsync(window);
        });
        try
        {
            builder.Build().WaitForClose();
            if (completed.Task.IsCompletedSuccessfully) return completed.Task.Result;
            if (bridgeError != null) throw new BridgeException(bridgeError);
            return null;
        }
        finally { lifetime.Cancel(); }

        async Task CompleteAsync(IInfiniFrameWindow window)
        {
            var result = await completed.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
            authorized?.Invoke(result);
            StartupLog.Event("browser.mobile-login-result-published");
            var dispatch = await window.DispatchAsync(window.Close, cancellationToken: lifetime.Token).ConfigureAwait(false);
            if (dispatch is not (InfiniFrameDispatchResult.Completed or InfiniFrameDispatchResult.WindowClosed))
                StartupLog.Event("browser.mobile-login-close-dispatch-failed");
        }

        async Task ObserveAsync(IInfiniFrameWindow window)
        {
            try
            {
                await window.WaitForReadyAsync(lifetime.Token);
                while (!window.IsClosedOrClosing())
                {
                    await window.Features.JavaScript.ExecuteJavaScriptAsync(AsExpression(script), lifetime.Token);
                    await Task.Delay(200, lifetime.Token);
                }
            }
            catch (Exception error) when (lifetime.IsCancellationRequested || window.IsClosedOrClosing())
            { Logging.AppLog.For("Pookie.Login").Debug("Окно мобильного входа завершено: {ErrorType}", error.GetType().Name); }
            catch (Exception error)
            {
                Logging.AppLog.Failure("Pookie.Login", "Ошибка окна мобильного входа", error);
                await window.DispatchAsync(window.Close, cancellationToken: lifetime.Token);
            }
        }
    }
}
