using System.Diagnostics;
using System.Globalization;
using Pookie.App.Storage;
using Pookie.Logging;
using Pookie.SoundCloud;
using Serilog.Events;

namespace Pookie.App.Diagnostics;

// Startup timings are Debug events in the common logger. Workers forward only
// whitelisted milestones; arbitrary browser stdout never becomes a log message.
internal static class StartupLog
{
    private sealed record Entry(string Stage, string State, long Elapsed, LogEventLevel Level = LogEventLevel.Debug,
        long? Duration = null, int? ChildPid = null, long? ChildElapsed = null, string? ErrorType = null, int? HttpStatus = null);
    private static readonly long started = Stopwatch.GetTimestamp();
    private static readonly object gate = new();
    private static readonly List<Entry> earlyEvents = [];
    private static bool childOutput;
    private static bool consoleUnavailable;

    public static void Initialize(AppDataPaths paths)
    {
        AppLog.Initialize(paths.Root);
        lock (gate)
        {
            foreach (var entry in earlyEvents) Emit(entry);
            earlyEvents.Clear();
        }
    }

    public static void EnsureInitialized()
    {
        if (!AppLog.IsInitialized) Initialize(new AppDataPaths());
    }

    public static void UseChildOutput()
    {
        childOutput = true;
        AppLog.Initialize(AppDataPaths.DefaultRoot(), worker: true);
    }

    public static void Event(string stage)
    {
        if (childOutput)
        {
            WriteMarker($"POOKIE_STARTUP|{ElapsedMilliseconds(started)}|{stage}");
            return;
        }
        var level = stage is "session.restore-unavailable" or "browser.site.blocked" or "browser.site.challenge-error"
            ? LogEventLevel.Warning : LogEventLevel.Debug;
        Write(new(stage, "event", ElapsedMilliseconds(started), level));
        if (stage == "startup.screen-ready")
            AppLog.For("Pookie").Information("Приложение готово; запуск занял {StartupDurationMs} мс", ElapsedMilliseconds(started));
    }

    public static void ForwardChild(string line, int pid)
    {
        var parts = line.Split('|');
        if (parts.Length != 3 || parts[0] != "POOKIE_STARTUP" ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var elapsed) ||
            parts[2] is not ("browser.child-start" or "browser.child-input-ready" or "browser.window-created" or
                "browser.engine-ready" or "browser.initial-navigation-start" or "browser.initial-navigation-ready" or "browser.bridge-installed" or
                "browser.content-filter-ready")) return;
        Write(new(parts[2], "event", ElapsedMilliseconds(started), ChildPid: pid, ChildElapsed: elapsed));
    }

    public static T Run<T>(string stage, Func<T> operation)
    {
        var at = Begin(stage);
        try { var result = operation(); End(stage, at); return result; }
        catch (Exception error) { Fail(stage, at, error); throw; }
    }

    public static async Task<T> RunAsync<T>(string stage, Func<Task<T>> operation)
    {
        var at = Begin(stage);
        try { var result = await operation().ConfigureAwait(false); End(stage, at); return result; }
        catch (Exception error) { Fail(stage, at, error); throw; }
    }

    public static async Task RunAsync(string stage, Func<Task> operation)
    {
        var at = Begin(stage);
        try { await operation().ConfigureAwait(false); End(stage, at); }
        catch (Exception error) { Fail(stage, at, error); throw; }
    }

    public static void HttpStatus(string stage, int status) => Write(new(stage, "response", ElapsedMilliseconds(started),
        status is >= 400 and not (401 or 404) ? LogEventLevel.Warning : LogEventLevel.Debug, HttpStatus: status));

    private static long Begin(string stage) { Write(new(stage, "start", ElapsedMilliseconds(started))); return Stopwatch.GetTimestamp(); }
    private static void End(string stage, long at) => Write(new(stage, "end", ElapsedMilliseconds(started), Duration: ElapsedMilliseconds(at)));
    private static void Fail(string stage, long at, Exception error) => Write(new(stage,
        error is OperationCanceledException ? "cancelled" : "error", ElapsedMilliseconds(started),
        error is OperationCanceledException or SoundCloudException { StatusCode: 401 or 404 } ? LogEventLevel.Debug : LogEventLevel.Warning,
        Duration: ElapsedMilliseconds(at), ErrorType: error.GetType().Name, HttpStatus: (error as SoundCloudException)?.StatusCode));
    private static long ElapsedMilliseconds(long at) => (long)Stopwatch.GetElapsedTime(at).TotalMilliseconds;

    private static void Write(Entry entry)
    {
        lock (gate)
        {
            if (!AppLog.IsInitialized) { if (earlyEvents.Count < 256) earlyEvents.Add(entry); return; }
            Emit(entry);
        }
    }

    private static void Emit(Entry entry) => AppLog.For("Pookie.Startup").Write(entry.Level,
        "stage={Stage} state={State} elapsed_ms={ElapsedMs} duration_ms={DurationMs} child_pid={ChildPid} child_elapsed_ms={ChildElapsedMs} error_type={ErrorType} http_status={HttpStatus}",
        entry.Stage, entry.State, entry.Elapsed, entry.Duration, entry.ChildPid, entry.ChildElapsed, entry.ErrorType, entry.HttpStatus);

    private static void WriteMarker(string line)
    {
        if (consoleUnavailable) return;
        try { Console.WriteLine(line); }
        catch (IOException) { consoleUnavailable = true; }
    }
}
