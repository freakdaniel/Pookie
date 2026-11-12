using Pookie.App.Hosting;

namespace Pookie.App.Diagnostics;

internal static class EmbeddedLoginLiveProbe
{
    internal static async Task RunAsync()
    {
        var root = Directory.CreateTempSubdirectory("pookie-persistence-test-embedded-live-").FullName;
        MainWindow? tested = null;
        try
        {
            await AppHost.RunAsync(new() { RequireWidevineSetup = false, Preview = true, RequireSignIn = true, SkipSessionRestore = true,
                IsolatedData = true, SilentAudio = true, AudioEnabled = false, DiscordPresence = false, SystemMediaSession = false }, window =>
            {
                tested = window;
                window.Window.Loaded += () => window.StartEmbeddedLoginLiveProbe(root);
            });
            if (tested?.VerificationFailure is { } error) throw new InvalidOperationException("Live embedded login probe failed.", error);
        }
        finally { await Browser.BrowserPersistenceSmokeTest.DeleteFixtureProfileAsync(root); }
    }
}
