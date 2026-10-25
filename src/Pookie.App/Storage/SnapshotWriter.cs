// SPDX-License-Identifier: BSD-3-Clause
namespace Pookie.App.Storage;

/// <summary>Serial disk writer with one replaceable pending snapshot. Awaiting a
/// write also awaits all older writes; a newer complete snapshot may subsume it.</summary>
internal sealed class SnapshotWriter<T>(Action<T> write)
{
    private readonly object sync = new();
    private (T Value, TaskCompletionSource Completion)? pending;
    private Task draining = Task.CompletedTask;
    private bool running;

    internal Task SaveAsync(T snapshot)
    {
        lock (sync)
        {
            var completion = pending?.Completion ?? new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = (snapshot, completion);
            if (!running) { running = true; draining = Task.Run(Drain); }
            return completion.Task;
        }
    }
    internal Task FlushAsync() { lock (sync) return draining; }
    private void Drain()
    {
        while (true)
        {
            (T Value, TaskCompletionSource Completion) next;
            lock (sync)
            {
                if (pending == null) { running = false; return; }
                next = pending.Value; pending = null;
            }
            try { write(next.Value); next.Completion.TrySetResult(); }
            catch (Exception error) { next.Completion.TrySetException(error); }
        }
    }
}
