using Pookie.App.Storage;
using Pookie.App.Browser;
using Pookie.SoundCloud;
using Pookie.Logging;
using Serilog.Events;
using System.Text.RegularExpressions;

namespace Pookie.App.Diagnostics;

internal static class StartupLogSmokeTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pookie-startup-log-" + Guid.NewGuid().ToString("N"));
        var stdout = Console.Out;
        var stderr = Console.Error;
        using var output = new StringWriter();
        var settings = new[] { "POOKIE_LOG_DIRECTORY", "POOKIE_LOG_CONSOLE_LEVEL", "POOKIE_LOG_FILE_LEVEL" };
        var original = settings.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            AppLog.Shutdown();
            foreach (var name in settings) Environment.SetEnvironmentVariable(name, null);
            Console.SetOut(output); Console.SetError(output);
            StartupLog.Event("probe.before-file");
            StartupLog.Initialize(new AppDataPaths(root));
            if (StartupLog.Run("probe.sync", () => 42) != 42 ||
                await StartupLog.RunAsync("probe.async", () => Task.FromResult(43)) != 43)
                throw new InvalidOperationException("Logging changed operation results.");
            const string privateValue = "synthetic-secret-must-not-be-logged";
            StartupLog.BrowserChallenge(new("checking", ChallengeType: "device_check"));
            StartupLog.BrowserChallenge(new("checking", Interactive: true, ChallengeType: "block"));
            StartupLog.BrowserChallenge(new("passed", ChallengeType: "block"));
            StartupLog.BrowserChallenge(new("challenge-error", ChallengeType: privateValue));
            StartupLog.BrowserChallenge(new(privateValue, ChallengeType: privateValue));
            var browserLog = new BrowserRequestLog();
            var command = new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "like", 42, 90, true);
            try
            {
                await browserLog.RunAsync(command, "embedded-browser", () =>
                {
                    browserLog.Observe(new("checking", Interactive: true, ChallengeType: "block"), "test-browser");
                    browserLog.Observe(new("protection-session", DataDomeClientId: privateValue), "test-browser");
                    browserLog.Observe(new("passed", ChallengeType: "block"), "test-browser");
                    browserLog.Observe(new("request-trace", command.Id, 403)
                    { Trace = new("response", "PUT", 1, 2, 1, false, true), Json = privateValue }, "test-browser");
                    browserLog.Observe(new("request-trace", command.Id)
                    { Trace = new(privateValue, "PUT", 1, 2, 1, false, true) }, "test-browser");
                    browserLog.Observe(new("blocked", Interactive: true, ChallengeType: "hard_block"), "test-browser");
                    browserLog.Observe(new("pass-ignored", ChallengeType: "hard_block"), "test-browser");
                    return Task.FromException<BrowserRequestEvent>(new SoundCloudException(privateValue, 403));
                });
            }
            catch (SoundCloudException error) when (error.StatusCode == 403) { }
            var propagated = false;
            try { await StartupLog.RunAsync("probe.failure", () => Task.FromException(new InvalidOperationException(privateValue))); }
            catch (InvalidOperationException error) when (error.Message == privateValue) { propagated = true; }
            if (!propagated) throw new InvalidOperationException("Logging swallowed an operation failure.");
            StartupLog.ForwardChild("POOKIE_STARTUP|12|browser.engine-ready", 123);
            StartupLog.ForwardChild("POOKIE_STARTUP|12|" + privateValue, 123);
            StartupLog.ForwardChild("arbitrary website output: " + privateValue, 123);
            AppLog.For("Pookie.Probe").Verbose("probe-trace");
            AppLog.For("Pookie.Probe").Debug("probe-debug");
            AppLog.For("Pookie.Probe").Information("probe-information");
            AppLog.For("Pookie.Probe").Warning("probe-warning");
            AppLog.Failure("Pookie.Probe", "probe-error", new InvalidOperationException(privateValue));
            AppLog.Failure("Pookie.Probe", "probe-wrapped-error", new InvalidOperationException(privateValue,
                new HttpRequestException(privateValue, null, System.Net.HttpStatusCode.Forbidden)));
            try { ThrowNetworkFailure(); }
            catch (HttpRequestException error) { AppLog.Failure("Pookie.Probe", "probe-stack", new InvalidOperationException(privateValue, error)); }
            static void ThrowNetworkFailure() => throw new HttpRequestException(privateValue, null, System.Net.HttpStatusCode.Forbidden);
            AppLog.For("Pookie.Probe").Fatal("probe-critical");
            AppLog.Shutdown();
            var log = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "Logs"), "pookie-*.log").Single());
            var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var events = lines.Where(line => line.StartsWith('[')).ToArray();
            if (lines.Any(line => !line.StartsWith('[') && !line.StartsWith("    ")) ||
                !lines.Any(line => line.StartsWith("    ") && line.Contains("ThrowNetworkFailure")))
                throw new InvalidOperationException("Safe stack frames must remain readable under their event.");
            if (!events.All(line => Regex.IsMatch(line,
                @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}\] \[(DBG|INF|WRN|ERR|FTL)\] \[Pookie[.\w]*\] \[pid=\d+\] .+")))
                throw new InvalidOperationException("Log entries must be readable timestamp/level/source/PID/message lines.");
            foreach (var stage in new[] { "probe.before-file", "probe.sync", "probe.async", "probe.failure", "browser.engine-ready" })
                if (!events.Any(line => line.Contains("stage=" + stage)))
                    throw new InvalidOperationException("Missing startup log stage: " + stage);
            if (!events.Any(line => line.Contains("child_pid=123")) || !events.Any(line => Regex.IsMatch(line, @"duration_ms=\d+")))
                throw new InvalidOperationException("Child metadata or durations were not recorded.");
            foreach (var level in new[] { "DBG", "INF", "WRN", "ERR", "FTL" })
                if (!events.Any(line => line.Contains("[" + level + "]"))) throw new InvalidOperationException("Missing log level: " + level);
            if (log.Contains(privateValue, StringComparison.Ordinal)) throw new InvalidOperationException("Private text reached startup logs.");
            if (!events.Any(line => line.Contains("HttpRequestException") && line.Contains("HTTP 403")))
                throw new InvalidOperationException("Wrapped audio/network failure lost its safe root type or HTTP status.");
            foreach (var (type, interactive) in new[] { ("device_check", "false"), ("block", "true") })
                if (!events.Any(line => line.Contains("stage=browser.site.checking ") &&
                    line.Contains("challenge_type=" + type + " ") && line.Contains("interactive=" + interactive)))
                    throw new InvalidOperationException("Challenge type/visibility missing from diagnostics.");
            if (!events.Any(line => line.Contains("stage=browser.site.passed ") && line.Contains("challenge_type=block ")))
                throw new InvalidOperationException("Completed challenge type missing from diagnostics.");
            foreach (var expectedStage in new[] { "checking", "passed", "blocked", "pass-ignored", "response", "failed" })
                if (!events.Any(line => line.Contains("stage=" + expectedStage + " ") && line.Contains(command.Id)))
                    throw new InvalidOperationException("Missing correlated browser diagnostic: " + expectedStage);
            if (!events.Any(line => line.Contains("stage=failed ") && line.Contains("http_status=403 ") &&
                line.Contains("passes=1 ") && line.Contains("rotations=1 ") && line.Contains("protection_state=blocked ")))
                throw new InvalidOperationException("Post-pass HTTP failure lost protection state.");
            var console = output.ToString();
            if (console.Contains("probe-debug") || console.Contains("probe-trace") || console.Contains("stage=probe.sync") || console.Contains(privateValue) || log.Contains("probe-trace"))
                throw new InvalidOperationException("Debug noise or private data reached the wrong destination.");
            foreach (var marker in new[] { "probe-information", "probe-warning", "probe-error", "probe-critical" })
                if (!console.Contains(marker)) throw new InvalidOperationException("Console suppressed an important event.");

            output.GetStringBuilder().Clear();
            Environment.SetEnvironmentVariable("POOKIE_LOG_CONSOLE_LEVEL", "Debug");
            Environment.SetEnvironmentVariable("POOKIE_LOG_FILE_LEVEL", "Warning");
            AppLog.Initialize(Path.Combine(root, "filtered"));
            AppLog.For("Pookie.Probe").Debug("probe-explicit-debug");
            AppLog.For("Pookie.Probe").Warning("probe-explicit-warning");
            AppLog.Shutdown();
            var filtered = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "filtered", "Logs"), "pookie-*.log").Single());
            if (!output.ToString().Contains("probe-explicit-debug") || filtered.Contains("probe-explicit-debug") || !filtered.Contains("probe-explicit-warning"))
                throw new InvalidOperationException("Independent console and file thresholds failed.");

            output.GetStringBuilder().Clear();
            Environment.SetEnvironmentVariable("POOKIE_LOG_CONSOLE_LEVEL", "None");
            Environment.SetEnvironmentVariable("POOKIE_LOG_FILE_LEVEL", "Trace");
            AppLog.Initialize(Path.Combine(root, "trace"));
            AppLog.For("Pookie.Probe").Verbose("probe-explicit-trace");
            AppLog.For("Pookie.Probe").Error("probe-muted-console");
            AppLog.Shutdown();
            if (output.ToString().Length != 0 || !File.ReadAllText(Directory.GetFiles(Path.Combine(root, "trace", "Logs"), "pookie-*.log").Single()).Contains("probe-explicit-trace"))
                throw new InvalidOperationException("Trace or None thresholds failed.");

            output.GetStringBuilder().Clear();
            Environment.SetEnvironmentVariable("POOKIE_LOG_CONSOLE_LEVEL", "invalid-private-setting");
            Environment.SetEnvironmentVariable("POOKIE_LOG_FILE_LEVEL", null);
            AppLog.Initialize(Path.Combine(root, "invalid"));
            AppLog.For("Pookie.Probe").Information("probe-invalid-fallback");
            AppLog.Shutdown();
            if (!output.ToString().Contains("POOKIE_LOG_CONSOLE_LEVEL") || !output.ToString().Contains("probe-invalid-fallback") || output.ToString().Contains("invalid-private-setting"))
                throw new InvalidOperationException("Invalid setting fallback failed or exposed its value.");

            output.GetStringBuilder().Clear();
            Environment.SetEnvironmentVariable("POOKIE_LOG_FILE_LEVEL", null);
            AppLog.Initialize(Path.Combine(root, "worker"), worker: true);
            AppLog.For("Pookie.Probe").Error("probe-worker-error");
            AppLog.Shutdown();
            if (output.ToString().Length != 0 || !File.ReadAllText(Directory.GetFiles(Path.Combine(root, "worker", "Logs"), "pookie-*.log").Single()).Contains("probe-worker-error"))
                throw new InvalidOperationException("Worker logs leaked into the console or were lost.");

            output.GetStringBuilder().Clear();
            var unavailable = Path.Combine(root, "unavailable");
            Directory.CreateDirectory(unavailable);
            File.WriteAllText(Path.Combine(unavailable, "Logs"), "fixture");
            AppLog.Initialize(unavailable);
            AppLog.For("Pookie.Probe").Error("probe-console-fallback");
            AppLog.Shutdown();
            if (!output.ToString().Contains("probe-console-fallback")) throw new InvalidOperationException("File failure broke console logging.");
        }
        finally
        {
            AppLog.Shutdown();
            Console.SetOut(stdout); Console.SetError(stderr);
            foreach (var (name, value) in original) Environment.SetEnvironmentVariable(name, value);
            Directory.Delete(root, recursive: true);
        }
        Console.WriteLine("STARTUP_LOG_OK: default/explicit thresholds, worker file output, console fallback, timings, propagation and private-data filtering");
    }
}
