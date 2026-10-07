using Pookie.App.Storage;
using Pookie.Logging;
using Serilog.Events;
using System.Text.Json;

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
            AppLog.For("Pookie.Probe").Fatal("probe-critical");
            AppLog.Shutdown();
            var log = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "Logs"), "pookie-*.log").Single());
            var events = log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
            { using var json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();
            foreach (var stage in new[] { "probe.before-file", "probe.sync", "probe.async", "probe.failure", "browser.engine-ready" })
                if (!events.Any(entry => entry.GetProperty("Properties").TryGetProperty("Stage", out var value) && value.GetString() == stage))
                    throw new InvalidOperationException("Missing startup log stage: " + stage);
            if (!events.Any(entry => entry.GetProperty("Properties").TryGetProperty("ChildPid", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == 123) ||
                !events.Any(entry => entry.GetProperty("Properties").TryGetProperty("DurationMs", out var value) && value.ValueKind == JsonValueKind.Number))
                throw new InvalidOperationException("Child metadata or durations were not recorded.");
            foreach (var level in new[] { "Debug", "Information", "Warning", "Error", "Fatal" })
                if (!events.Any(entry => entry.GetProperty("Level").GetString() == level)) throw new InvalidOperationException("Missing log level: " + level);
            if (log.Contains(privateValue, StringComparison.Ordinal)) throw new InvalidOperationException("Private text reached startup logs.");
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
