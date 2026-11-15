namespace Pookie.App.Storage;

// A native factory may ignore cancellation. Keep its result owned until the UI
// explicitly takes it; cancellation can dispose a late result without a UI pump.
internal sealed class AsyncResourceStartup<T> : IDisposable where T : class, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken token;
    private CancellationTokenRegistration cancellation;
    private T? resource;
    private bool abandoned, taken;

    internal Task Ready => ready.Task;
    internal Task Completion { get; }
    internal bool Taken { get { lock (sync) return taken; } }

    internal AsyncResourceStartup(Func<Task<T>> start, CancellationToken token)
    {
        this.token = token;
        cancellation = token.Register(Dispose);
        Completion = CompleteAsync(start);
    }

    internal bool TryTake(out T value)
    {
        lock (sync)
        {
            if (!abandoned && !token.IsCancellationRequested && resource != null)
            {
                value = resource; resource = null; taken = true;
                decision.TrySetResult(); return true;
            }
            value = null!; return false;
        }
    }

    private async Task CompleteAsync(Func<Task<T>> start)
    {
        try
        {
            if (token.IsCancellationRequested) return;
            T created;
            try { created = await start().ConfigureAwait(false); }
            catch (Exception error) { ready.TrySetException(error); return; }
            lock (sync)
            {
                if (!abandoned) { resource = created; ready.TrySetResult(); }
            }
            await decision.Task.ConfigureAwait(false);
            bool release; lock (sync) release = !taken;
            if (release) await created.DisposeAsync().ConfigureAwait(false);
        }
        finally { cancellation.Dispose(); }
    }

    public void Dispose()
    {
        lock (sync)
        {
            abandoned = true; resource = null;
            ready.TrySetCanceled(token.IsCancellationRequested ? token : new CancellationToken(true));
            decision.TrySetResult();
        }
    }
}
