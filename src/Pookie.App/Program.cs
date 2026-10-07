using Pookie.App.Hosting;
using Pookie.App.Browser;
using Pookie.App;
using Pookie.App.Storage;
using Pookie.Logging;
using Serilog.Events;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { RunAsync(args).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            AppLog.Initialize(AppDataPaths.DefaultRoot());
            AppLog.Failure("Pookie", "Приложение аварийно завершилось", error, LogEventLevel.Fatal);
            Environment.ExitCode = 1;
        }
        finally { AppLog.Shutdown(); }
    }

    private static async Task RunAsync(string[] args)
    {
        if (args.Length == 4 && args[0] == "--web-requests" && args[2] == "--login-profile")
        {
            Environment.ExitCode = NativeBrowserSession.RunChild(args[1], args[3], null);
            return;
        }
        if (args.Length == 4 && args[0] == "--web-login" && args[2] == "--login-profile")
        {
            Environment.ExitCode = NativeWebLogin.RunChild(args[1], args[3], null);
            return;
        }
        if (args.Any(arg => arg.EndsWith("-smoke-test", StringComparison.Ordinal) || arg is "--smoke-test" or "--clipboard-fixture" or "--login-fixture"))
        {
            Console.Error.WriteLine("Интеграционные проверки запускаются отдельно: dotnet run --project tests/Pookie.App.SmokeTests -- <флаг проверки>");
            Environment.ExitCode = 2;
            return;
        }
        await AppHost.RunAsync(AppRunOptions.FromArgs(args));
    }
}
