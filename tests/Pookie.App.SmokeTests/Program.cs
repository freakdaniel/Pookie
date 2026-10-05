using Aprillz.MewUI;
using Pookie.App;
using Pookie.App.Auth;
using Pookie.App.Diagnostics;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task RunAsync(string[] args)
    {
        try
        {
            if (args.Length is 4 or 6 && args[2] == "--login-profile")
            {
                var fixture = args.Length == 6 && args[4] == "--login-fixture" ? args[5] : null;
                if (args[0] == "--web-requests")
                { Environment.ExitCode = NativeBrowserSession.RunChild(args[1], args[3], fixture); return; }
                if (args[0] == "--web-login")
                { Environment.ExitCode = NativeWebLogin.RunChild(args[1], args[3], fixture); return; }
            }
            if (await DiagnosticsRunner.TryRunAsync(args)) return;
            if (!args.Any(arg => arg is "--ui-smoke-test" or "--login-ui-smoke-test" or "--smoke-test"))
                throw new ArgumentException("Укажи --ui-smoke-test, --login-ui-smoke-test или другую проверку из README.md этого проекта.");
            var loginUi = args.Contains("--login-ui-smoke-test");
            var options = AppRunOptions.FromArgs(args) with
            {
                Preview = true, RequireSignIn = loginUi, SkipSessionRestore = loginUi,
                SilentAudio = true, DiscordPresence = false, IsolatedData = true
            };
            Application.DispatcherUnhandledException += e => Console.Error.WriteLine(e.Exception);
            await AppHost.RunAsync(options, window => window.ConfigureVerification(args));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Environment.ExitCode = 1;
        }
    }
}
