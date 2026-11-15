using System.Runtime.ExceptionServices;

namespace Pookie.App.Storage;

// Own retired resources until their async teardown finishes. A tracked operation
// may retire another resource before completing; Drain waits for that work too.
internal sealed class AsyncCleanup
{
    private readonly object sync = new();
    private readonly HashSet<Task> pending = [];
    private const int MaximumRetainedFailures = 16;
    private readonly List<Exception> failures = [];
    private int additionalFailureReports;

    internal void Add(Task operation, bool propagateFailure = true)
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync) pending.Add(observed.Task);
        _ = ObserveAsync(operation, observed, propagateFailure);
    }

    private async Task ObserveAsync(Task operation, TaskCompletionSource observed, bool propagateFailure)
    {
        try { await operation.ConfigureAwait(false); }
        catch (Exception error)
        {
            if (propagateFailure) lock (sync)
            {
                if (!failures.Any(previous => ReferenceEquals(previous, error)))
                {
                    if (failures.Count < MaximumRetainedFailures) failures.Add(error);
                    else if (additionalFailureReports < int.MaxValue) additionalFailureReports++;
                }
            }
        }
        finally
        {
            lock (sync) pending.Remove(observed.Task);
            observed.TrySetResult();
        }
    }

    internal async Task DrainAsync()
    {
        while (true)
        {
            Task[] work; Exception[] errors;
            lock (sync)
            {
                work = pending.ToArray();
                errors = [];
                if (work.Length == 0)
                {
                    errors = failures.ToArray();
                    if (additionalFailureReports > 0)
                        errors = [.. errors, new InvalidOperationException($"{additionalFailureReports} additional cleanup failure reports were omitted.")];
                    failures.Clear(); additionalFailureReports = 0;
                }
            }
            if (work.Length > 0) { await Task.WhenAll(work).ConfigureAwait(false); continue; }
            if (errors.Length == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Length > 1) throw new AggregateException(errors);
            return;
        }
    }
}
