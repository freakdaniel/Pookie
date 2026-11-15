// SPDX-License-Identifier: BSD-3-Clause
namespace Pookie.App.Storage;

/// <summary>Serial disk writer with one replaceable pending snapshot. Awaiting a
/// write also awaits all older writes; a newer complete snapshot may subsume it.</summary>
internal sealed class SnapshotWriter<T>(Action<T> write)
{
    private readonly object sync = new();
    private (T Value, TaskCompletionSource Completion)? pending;
    private Task lastSave = Task.CompletedTask;
    private bool running;

    internal Task SaveAsync(T snapshot)
    {
        lock (sync)
        {
            var completion = pending?.Completion ?? new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = (snapshot, completion);
            lastSave = completion.Task;
            if (!running) { running = true; _ = Task.Run(Drain); }
            return lastSave;
        }
    }
    // The latest admitted snapshot is the durability barrier. The worker itself
    // remains healthy after a failed write, so awaiting it would hide that error.
    internal Task FlushAsync() { lock (sync) return lastSave; }
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
