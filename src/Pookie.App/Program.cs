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
        if (BrowserChildHost.TryRun(args, out var childExitCode)) { Environment.ExitCode = childExitCode; return; }
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
        if (args is ["--aot-self-test"] or ["--aot-self-test", _])
        {
            try { await Pookie.App.Diagnostics.AotSelfTest.RunAsync(args.Length == 2 ? args[1] : null); }
            catch (Exception error) { Console.Error.WriteLine(error); throw; }
            return;
        }
        if (args is ["--aot-ui-check"])
        {
            if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) throw new InvalidOperationException("This check requires Native AOT.");
            var frames = 0; var ticks = 0; Task? browserCheck = null;
            using var timer = new Aprillz.MewUI.DispatcherTimer(TimeSpan.FromSeconds(1));
            await AppHost.RunAsync(AppRunOptions.FromArgs(["--demo", "--no-audio"]) with { DiscordPresence = false, SystemMediaSession = false }, window =>
            {
                window.Window.FrameRendered += () => frames++;
                timer.Tick += () =>
                {
                    if (++ticks == 4) browserCheck = Pookie.App.Diagnostics.AotBrowserCheck.RunAsync(window.Window);
                    if (ticks >= 6 && browserCheck is { IsCompleted: true }) window.Window.Close();
                    if (ticks >= 50) { window.Window.Close(); Environment.ExitCode = 1; }
                };
                timer.Start();
            });
            if (browserCheck == null) throw new InvalidOperationException("Browser check did not start.");
            await browserCheck;
            if (frames < 3 || Environment.ExitCode != 0) throw new InvalidOperationException("Native UI did not render successfully.");
            Console.WriteLine("AOT_UI_OK: production UI rendered " + frames + " frames and shut down cleanly");
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
