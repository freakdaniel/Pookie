using Pookie.SoundCloud;

namespace Pookie.App.Playback;

// Called on the owner's dispatcher. SourceVersion also rejects responses from a
// transport which completes after cancellation. Navigation never owns this token.
internal sealed class PlaybackQueueLoader(PlaybackQueue queue,
    Func<PlaybackContext, string, CancellationToken, Task<TrackPage>> fetch, Action changed,
    CancellationToken lifetime,
    Func<PlaybackQueueSnapshot, CancellationToken, Task<SoundCloudTrack[]>>? recommend = null) : IDisposable
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
        var loadSource = state.Context?.NextHref != null && (force ||
            (state.Context.Kind == PlaybackContextKind.Wave ? state.ContextUpcoming.Count < PlaybackQueue.WaveUpcomingLimit : state.ContextUpcoming.Count <= 5));
        if (!loadSource && !(recommend != null && state.CanLoadRecommendations))
            return Task.CompletedTask;
        return pending = LoadAheadAsync(state.SourceVersion, loadSource, cancellation!.Token);
    }
    private async Task LoadAheadAsync(long sourceVersion, bool loadSource, CancellationToken token)
    {
        await Task.Yield(); token.ThrowIfCancellationRequested();
        if (sourceVersion != queue.SourceVersion) return;
        if (loadSource && queue.Context is { NextHref: { } cursor } context) await LoadAsync(sourceVersion, context, cursor, token);
        if (recommend == null || token.IsCancellationRequested || sourceVersion != queue.SourceVersion || !queue.Snapshot.CanLoadRecommendations) return;
        var tracks = await recommend(queue.Snapshot, token);
        if (!token.IsCancellationRequested && queue.AppendRecommendations(sourceVersion, tracks)) changed();
    }
    private async Task LoadAsync(long sourceVersion, PlaybackContext context, string cursor, CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var page = await fetch(context, cursor, token);
            if (token.IsCancellationRequested || sourceVersion != queue.SourceVersion) return;
            if (queue.Append(sourceVersion, cursor, page)) { changed(); return; }
            // Feedback can replace only a wave's future queue while this request is
            // in flight. Retry against its new cursor without touching manual entries.
            if (context.Kind != PlaybackContextKind.Wave || queue.Context?.NextHref is not { } next || next == cursor) return;
            context = queue.Context; cursor = next;
        }
    }
    public void Dispose() { cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; }
}
