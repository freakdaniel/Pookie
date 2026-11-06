using InfiniFrame;
using InfiniFrame.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pookie.SoundCloud;
using System.Security.Cryptography;

namespace Pookie.App.Browser;

internal static partial class InfiniFrameLoginWindow
{
    public static SoundCloudMobileChallengeResult? RunMobileChallenge(SoundCloudMobileChallenge challenge, string profilePath,
        string? fixture, Action<SoundCloudMobileChallengeResult> completed)
    {
        var pairing = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var script = MobileChallengeBridge.Script(challenge, pairing, webkit: false, fixture);
        var start = fixture ?? challenge.Url ?? challenge.RequestUrl;
        var origin = new Uri(start).GetLeftPart(UriPartial.Authority);
        SoundCloudMobileChallengeResult? result = null;
        Exception? bridgeError = null;
        using var lifetime = new CancellationTokenSource();
        var services = new ServiceCollection(); services.AddLogging(logging => logging.ClearProviders());
        var builder = InfiniFrameWindowBuilder.Create(services).SetUserAgent(null)
            .SetTitle("Pookie — мобильная проверка SoundCloud").SetSize(600, 760)
            .SetTemporaryFilesPath(profilePath).EnableIgnoreCertificateErrors(false).EnableWebSecurity(true)
            .EnableFileSystemAccess(false).EnableJavascriptClipboardAccess(false).EnableBrowserPermissions(false)
            .EnableMediaStream(false).EnableMediaAutoplay(false).EnableDevTools(false).AddTrustedOrigin(origin);
        if (challenge.Html != null && fixture == null)
        {
            builder.AddTrustedOrigin("about:blank");
            builder.SetStartPageContent("<base href=\"" + System.Net.WebUtility.HtmlEncode(challenge.RequestUrl) + "\"><script>" + script + "</script>" + challenge.Html);
        }
        else builder.SetStartPageUrl(start);
        DisableSystemMedia(builder);
        builder.RegisterNavigationStartingHandler((_, navigation) =>
            MobileChallengeBridge.Allows(navigation.Url, challenge, fixture) ? NavigationStartingResult.Allow : NavigationStartingResult.Cancel);
        builder.RegisterWebMessagePostHandler(BridgeMessageId, (window, raw) =>
        {
            if (raw == null || result != null) return;
            var message = MobileChallengeBridge.Read(raw, window.GetCurrentUrl(), challenge, pairing, fixture);
            if (message.Visible is { } visible) BrowserWindowVisibility.Set(window, visible);
            if (message.Result is not { } value) return;
            result = value; completed(value); window.Close();
        });
        builder.RegisterWindowCreatedHandler(window =>
        {
            if (!challenge.Interactive) BrowserWindowVisibility.Set(window, false);
            _ = RunBridgeTaskAsync(window, async () =>
            {
                await window.WaitForReadyAsync(lifetime.Token);
                while (!window.IsClosedOrClosing())
                {
                    await window.Features.JavaScript.ExecuteJavaScriptAsync(AsExpression(script), lifetime.Token);
                    await Task.Delay(100, lifetime.Token);
                }
            }, error => Interlocked.CompareExchange(ref bridgeError, error, null), lifetime.Token);
        });
        try
        {
            builder.Build().WaitForClose();
            if (result == null && bridgeError != null) throw new BridgeException(bridgeError);
            return result;
        }
        finally { lifetime.Cancel(); }
    }
}
