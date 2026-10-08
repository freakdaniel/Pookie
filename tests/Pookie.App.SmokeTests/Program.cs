using Pookie.App.Hosting;
using Pookie.App.Browser;
using Aprillz.MewUI;
using Pookie.App;
using Pookie.App.Diagnostics;
using Pookie.Logging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--session-vault-child")
            { SessionVaultSmokeTest.RunChild(args[1]); return; }
            if (args.Length == 4 && args[0] == "--drm-transport-child" && args[2] == "--login-profile")
            { Environment.ExitCode = DrmTransportProbe.RunChild(args[1], args[3]); return; }
            if (args.Length is 4 or 6 && args[2] == "--login-profile")
            {
                var fixture = args.Length == 6 && args[4] == "--login-fixture" ? args[5] : null;
                if (args[0] == "--web-requests")
                { Environment.ExitCode = NativeBrowserSession.RunChild(args[1], args[3], fixture); return; }
                if (args[0] == "--web-login")
                { Environment.ExitCode = NativeWebLogin.RunChild(args[1], args[3], fixture); return; }
            }
            if (await DiagnosticsRunner.TryRunAsync(args)) return;
            if (!args.Any(arg => arg is "--ui-smoke-test" or "--login-ui-smoke-test" or "--smoke-test" or "--browser-shutdown-smoke-test" or "--media-ui-smoke-test" or "--buffer-ui-smoke-test" or "--expanded-ui-smoke-test" or "--lyrics-ui-smoke-test" or "--artwork-ui-smoke-test"))
                throw new ArgumentException("Укажи --ui-smoke-test, --login-ui-smoke-test или другую проверку из README.md этого проекта.");
            var loginUi = args.Contains("--login-ui-smoke-test");
            var options = AppRunOptions.FromArgs(args) with
            {
                Preview = true, RequireSignIn = loginUi, SkipSessionRestore = loginUi,
                SilentAudio = true, DiscordPresence = false, SystemMediaSession = args.Contains("--media-ui-smoke-test"), IsolatedData = true
            };
            Application.DispatcherUnhandledException += e => Console.Error.WriteLine(e.Exception);
            MainWindow? testedWindow = null;
            await AppHost.RunAsync(options, window => { testedWindow = window; window.ConfigureVerification(args); });
            if (testedWindow?.VerificationFailure is { } verificationError) throw new InvalidOperationException("UI verification failed.", verificationError);
            if (args.Contains("--browser-shutdown-smoke-test"))
                await testedWindow!.VerifyBrowserShutdownAsync();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Environment.ExitCode = 1;
        }
        finally { AppLog.Shutdown(); }
    }
}
