using Aprillz.MewUI;
using Pookie.App.Hosting;
using Pookie.Audio;

namespace Pookie.App.Diagnostics;

internal static class WidevineConsentUiSmokeTest
{
    internal const string LicenseText = "Fixture vendor license obtained from the network. This text is supplied at runtime, not embedded by Pookie.\n\nFor information, visit www.widevine.com";

    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pookie-consent-ui-" + Guid.NewGuid().ToString("N"));
        var consent = new WidevineConsent(root);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var preparationBusy = 0;
        var resolutionCalls = 0;
        var setup = new WidevineSetup(consent, async token =>
        {
            if (Interlocked.Increment(ref resolutionCalls) == 1) throw new HttpRequestException("Fixture license fetch failure");
            await Task.Delay(150, token);
            return new WidevineCandidate(new("4.10.3112.0", "linux-x64", new("https://dl.google.com/fixture.crx3"),
                100, new string('a', 64), "_platform_specific/linux_x64/libwidevinecdm.so"), LicenseText, null);
        }, async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new HttpRequestException("Fixture download failure");
            // Simulate a synchronous hash/decompression/native initialization
            // before an async installer reaches its first incomplete await.
            Interlocked.Exchange(ref preparationBusy, 1);
            try { Thread.Sleep(450); }
            finally { Interlocked.Exchange(ref preparationBusy, 0); }
            return await ready.Task.WaitAsync(token).ConfigureAwait(false);
        });
        MainWindow? tested = null;
        try
        {
            await AppHost.RunAsync(new AppRunOptions
            {
                SkipSessionRestore = true, AudioEnabled = false, DiscordPresence = false,
                SystemMediaSession = false, IsolatedData = true, RequireWidevineSetup = true, WidevineSetupOverride = setup
            }, window =>
            {
                tested = window;
                window.Window.WindowSize = WindowSize.Resizable(1000, 680, minWidth: 1000, minHeight: 680);
                window.Window.OnLoaded(() => window.VerifyWidevineConsentUi(consent, () => Volatile.Read(ref calls), ready,
                    () => Volatile.Read(ref preparationBusy) != 0));
            });
            if (tested?.VerificationFailure is { } error) throw new InvalidOperationException("Widevine consent UI failed.", error);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
