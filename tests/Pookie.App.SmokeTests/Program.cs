using Pookie.App.Hosting;
using Pookie.App.Browser;
using Aprillz.MewUI;
using Pookie.App;
using Pookie.App.Diagnostics;
using Pookie.Logging;
using Aprillz.MewUI.Diagnostics;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_PROFILE") == "1" ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POOKIE_PERF_CAPTURE")))
        {
            AppContext.SetSwitch("Aprillz.MewUI.DevTools.Enabled", true);
            var profilerType = typeof(Window).Assembly.GetType("Aprillz.MewUI.Diagnostics.PerformanceProfiler", throwOnError: true)!;
            var profiler = profilerType.GetProperty("Instance")!.GetValue(null)!;
            profilerType.GetProperty("IsEnabled")!.SetValue(profiler, true);
        }
        if (BrowserChildHost.TryRun(args, out var childExitCode)) { Environment.ExitCode = childExitCode; return; }
        RunAsync(args).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(string[] args)
    {
        var capture = Environment.GetEnvironmentVariable("POOKIE_PERF_CAPTURE");
        var frames = new List<FramePerformanceStats>();
        long lastFrame = -1;
        try
        {
            if (args.Contains("--chat-video-smoke-test")) { await ChatVideoSmokeTest.RunAsync(); return; }
            if (args.Length == 2 && args[0] == "--session-vault-child")
            { SessionVaultSmokeTest.RunChild(args[1]); return; }
            if (await DiagnosticsRunner.TryRunAsync(args)) return;
            var chromeUi = args.Contains("--chrome-ui-smoke-test") || args.Contains("--expanded-glass-ui-smoke-test") ||
                args.Contains("--track-description-ui-smoke-test");
            if (!chromeUi && !args.Contains("--chat-video-live-ui-probe") && !args.Any(arg => arg is "--playback-memory-ui-smoke-test" or "--feed-ui-smoke-test" or "--user-profile-ui-smoke-test" or "--settings-ui-smoke-test" or "--chat-media-ui-smoke-test" or "--queue-continuation-ui-smoke-test" or "--menus-ui-smoke-test" or "--chat-music-ui-smoke-test" or "--chat-drawer-ui-smoke-test" or "--chat-navigation-ui-smoke-test" or "--chat-emoji-ui-smoke-test" or "--chats-ui-smoke-test" or "--wave-queue-ui-smoke-test" or "--memory-ui-smoke-test" or "--ui-smoke-test" or "--login-ui-smoke-test" or "--smoke-test" or "--browser-shutdown-smoke-test" or "--media-ui-smoke-test" or "--buffer-ui-smoke-test" or "--expanded-ui-smoke-test" or "--lyrics-ui-smoke-test" or "--artwork-ui-smoke-test" or "--scrolling-ui-smoke-test" or "--track-page-ui-smoke-test" or "--track-page-live-ui-probe" or "--likes-filter-ui-smoke-test" or "--likes-edits-ui-smoke-test" or "--collections-ui-smoke-test" or "--comments-ui-smoke-test" or "--home-ui-smoke-test" or "--wave-journal-ui-smoke-test"))
                throw new ArgumentException("Укажи --ui-smoke-test, --login-ui-smoke-test или другую проверку из README.md этого проекта.");
            var loginUi = args.Contains("--login-ui-smoke-test");
            var liveTrackUi = args.Contains("--track-page-live-ui-probe") || args.Contains("--chat-video-live-ui-probe");
            var likesEditsUi = args.Contains("--likes-filter-ui-smoke-test") || args.Contains("--playback-memory-ui-smoke-test") || args.Contains("--feed-ui-smoke-test") || args.Contains("--user-profile-ui-smoke-test") || args.Contains("--settings-ui-smoke-test") || args.Contains("--chat-media-ui-smoke-test") || args.Contains("--queue-continuation-ui-smoke-test") || args.Contains("--chat-music-ui-smoke-test") || args.Contains("--chat-drawer-ui-smoke-test") || args.Contains("--chat-navigation-ui-smoke-test") || args.Contains("--chat-emoji-ui-smoke-test") || args.Contains("--chats-ui-smoke-test") || args.Contains("--likes-edits-ui-smoke-test") || args.Contains("--collections-ui-smoke-test") || args.Contains("--comments-ui-smoke-test") || args.Contains("--home-ui-smoke-test") || args.Contains("--wave-journal-ui-smoke-test");
            var options = AppRunOptions.FromArgs(args) with
            {
                Preview = !liveTrackUi && !likesEditsUi, RequireSignIn = loginUi, SkipSessionRestore = loginUi || liveTrackUi || likesEditsUi,
                SilentAudio = true, DiscordPresence = false, SystemMediaSession = args.Contains("--media-ui-smoke-test"), IsolatedData = true
            };
            Application.DispatcherUnhandledException += e => Console.Error.WriteLine(e.Exception);
            MainWindow? testedWindow = null;
            await AppHost.RunAsync(options, window =>
            {
                testedWindow = window; window.ConfigureVerification(args);
                if (!string.IsNullOrWhiteSpace(capture)) window.Window.FrameRendered += () =>
                {
                    // FrameRendered can precede native presentation. The public
                    // stats describe the previous completed frame; deduplicate it.
                    var frame = window.Window.LastFramePerformanceStats;
                    if (frame.IsEmpty || frame.FrameIndex == lastFrame) return;
                    lastFrame = frame.FrameIndex; frames.Add(frame);
                };
            });
            if (testedWindow?.VerificationFailure is { } verificationError) throw new InvalidOperationException("UI verification failed.", verificationError);
            if (args.Contains("--browser-shutdown-smoke-test"))
                await testedWindow!.VerifyBrowserShutdownAsync();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Environment.ExitCode = 1;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(capture))
                    File.WriteAllText(capture, JsonSerializer.Serialize(frames));
            }
            finally { AppLog.Shutdown(); }
        }
    }
}
