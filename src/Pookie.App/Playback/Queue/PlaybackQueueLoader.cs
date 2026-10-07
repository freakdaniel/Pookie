using Pookie.SoundCloud;

namespace Pookie.App.Playback;

// Called on the owner's dispatcher. SourceVersion also rejects responses from a
// transport which completes after cancellation. Navigation never owns this token.
internal sealed class PlaybackQueueLoader(PlaybackQueue queue,
    Func<PlaybackContext, string, CancellationToken, Task<TrackPage>> fetch, Action changed,
    CancellationToken lifetime) : IDisposable
{
    private CancellationTokenSource? cancellation;
    private Task? pending;
    private long version = -1;
    internal void ContextChanged()
    {
        cancellation?.Cancel(); cancellation?.Dispose();
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        version = queue.SourceVersion; pending = null;
    }
    internal Task EnsureAheadAsync(bool force = false)
    {
        if (version != queue.SourceVersion) ContextChanged();
        if (pending is { IsCompleted: false }) return pending;
        var state = queue.Snapshot;
        if (state.Context?.NextHref is not { } cursor || !force && state.ContextUpcoming.Count > 5) return Task.CompletedTask;
        return pending = LoadAsync(state.SourceVersion, state.Context, cursor, cancellation!.Token);
    }
    private async Task LoadAsync(long sourceVersion, PlaybackContext context, string cursor, CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        var page = await fetch(context, cursor, token);
        if (!token.IsCancellationRequested && queue.Append(sourceVersion, cursor, page)) changed();
    }
    public void Dispose() { cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; }
}
