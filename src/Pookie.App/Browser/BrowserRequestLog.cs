using System.Collections.Concurrent;
using System.Diagnostics;
using Pookie.Logging;
using Pookie.SoundCloud;
using Serilog.Events;

namespace Pookie.App.Browser;

// Only our validated command IDs and bounded protocol metadata may enter this journal.
// Never destructure commands/events: they can contain credentials, messages and response bodies.
internal sealed class BrowserRequestLog
{
    private readonly string session = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, string> pending = new();
    private string state = "initial";
    private int passes, rotations;

    internal async Task<BrowserRequestEvent> RunAsync(BrowserRequestCommand command, string transport,
        Func<Task<BrowserRequestEvent>> request)
    {
        var started = Stopwatch.GetTimestamp();
        pending[command.Id] = command.Operation;
        Write(LogEventLevel.Debug, "start", 0, null);
        try
        {
            var result = await request().ConfigureAwait(false);
            Write(LogEventLevel.Debug, "complete", result.Status, null);
            return result;
        }
        catch (Exception error)
        {
            var cancelled = error is OperationCanceledException;
            var status = (error as SoundCloudException)?.StatusCode ?? 0;
            var missingResource = command.Operation == "api-get" && status == 404;
            Write(cancelled || missingResource ? LogEventLevel.Debug : LogEventLevel.Warning,
                cancelled ? "cancelled" : "failed", status, error.GetType().Name);
            throw;
        }
        finally { pending.TryRemove(command.Id, out _); }

        void Write(LogEventLevel level, string stage, int status, string? errorType) => AppLog.For("Pookie.Browser.Request").Write(level,
            "browser_session={BrowserSession} request={RequestId} operation={Operation} transport={Transport} stage={Stage} http_status={HttpStatus} duration_ms={DurationMs} protection_state={ProtectionState} passes={PassCount} rotations={RotationCount} error_type={ErrorType}",
            session, command.Id, command.Operation, transport, stage, status, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Volatile.Read(ref state), Volatile.Read(ref passes), Volatile.Read(ref rotations), errorType);
    }

    internal void Observe(BrowserRequestEvent message, string browserInstance)
    {
        if (message.Kind == "request-trace" && message.Trace is { } trace && trace.IsValid() &&
            pending.TryGetValue(message.RequestId, out var operation))
        {
            AppLog.For("Pookie.Browser.Request").Debug(
                "browser_session={BrowserSession} browser_instance={BrowserInstance} request={RequestId} operation={Operation} stage={Stage} method={Method} fetch={FetchNumber} attempt={Attempt} http_status={HttpStatus} generation={ProtectionGeneration} challenge_active={ChallengeActive} protection_present={ProtectionPresent} reason={Reason}",
                session, browserInstance, message.RequestId, operation, trace.Stage, trace.Method, trace.FetchNumber, trace.Attempt,
                message.Status, trace.Generation, trace.ChallengeActive, trace.HasProtection, trace.Reason);
            return;
        }
        if (message.Kind is not ("ready" or "checking" or "passed" or "blocked" or "challenge-error" or "protection-session" or "pass-ignored")) return;
        var previous = Volatile.Read(ref state);
        if (message.Kind is not ("protection-session" or "pass-ignored")) Volatile.Write(ref state, message.Kind);
        if (message.Kind == "passed") Interlocked.Increment(ref passes);
        if (message.Kind == "protection-session") Interlocked.Increment(ref rotations);
        var type = message.ChallengeType is "device_check" or "device_check_invisible_mode" or "block" or "hard_block"
            ? message.ChallengeType : "unknown";
        AppLog.For("Pookie.Browser.Protection").Write(message.Kind switch
        {
            "blocked" or "challenge-error" or "pass-ignored" => LogEventLevel.Warning,
            "checking" or "passed" => LogEventLevel.Information,
            _ => LogEventLevel.Debug
        }, "browser_session={BrowserSession} browser_instance={BrowserInstance} stage={Stage} previous_state={PreviousState} challenge_type={ChallengeType} interactive={Interactive} active_requests={ActiveRequests} passes={PassCount} rotations={RotationCount}",
            session, browserInstance, message.Kind, previous, type, message.Interactive,
            string.Join(",", pending.OrderBy(item => item.Key).Select(item => item.Key + ":" + item.Value)),
            Volatile.Read(ref passes), Volatile.Read(ref rotations));
    }
}
