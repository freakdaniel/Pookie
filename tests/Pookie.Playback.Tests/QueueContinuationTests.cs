using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class QueueContinuationTests
{
    private static SoundCloudTrack Track(long id, string? title = null) => new() { Id = id, Title = title ?? "Track " + id,
        Duration = 120000, User = new() { Id = id, Username = "Artist " + id } };
    private static PlaybackQueue Queue(params long[] ids)
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("playlist", "Playlist", "", Kind: PlaybackContextKind.Playlist), ids.Select(id => Track(id)), 0);
        return queue;
    }
    [Fact]
    public void ContinuationPreservesOriginalSourceAndManualPriority()
    {
        var queue = Queue(1, 2); var version = queue.SourceVersion;
        Assert.True(queue.AppendRecommendations(version, [Track(1), Track(3), Track(3), Track(4), Track(5), Track(6)]));
        Assert.Equal(new long[] { 3, 4, 5 }, queue.Snapshot.Recommendations.Select(entry => entry.Track.Id));
        var manual = queue.Enqueue(Track(8));
        Assert.Equal(manual, queue.Next()); Assert.Equal(2, queue.Next()!.Track.Id);
        var recommendation = queue.Next()!;
        Assert.Equal(3, recommendation.Track.Id); Assert.Equal(PlaybackEntryOrigin.Recommendation, recommendation.Origin);
        Assert.Equal(new long[] { 1, 2 }, queue.Snapshot.Source.Select(entry => entry.Track.Id));
        Assert.Equal(version, queue.SourceVersion); Assert.Equal("playlist", queue.Context!.Key);
        Assert.Equal(2, queue.Previous()!.Track.Id); Assert.Equal(recommendation, queue.Next());
    }
    [Fact]
    public void RepeatPreservesVisibleRecommendationsAndRejectsLateRefills()
    {
        var queue = Queue(1); queue.AppendRecommendations(queue.SourceVersion, [Track(2)]);
        var retained=queue.Snapshot.Recommendations.ToArray();
        queue.SetRepeat(RepeatMode.Context);
        Assert.Equal(retained,queue.Snapshot.Recommendations);
        Assert.False(queue.AppendRecommendations(queue.SourceVersion, [Track(3)]));
        Assert.Equal(1, queue.Next()!.Track.Id);
        queue.SetRepeat(RepeatMode.Track);
        Assert.Equal(retained,queue.Snapshot.Recommendations);
        Assert.False(queue.Snapshot.CanNext);
        Assert.Equal(1, queue.Next(naturalEnd: true)!.Track.Id);
        queue.SetRepeat(RepeatMode.Off); Assert.True(queue.Snapshot.CanLoadRecommendations);
        Assert.Equal(retained,queue.Snapshot.Recommendations);
        Assert.Equal(2,queue.Next(naturalEnd:true)!.Track.Id);
    }
    [Fact]
    public void AddingARecommendationManuallyRemovesItsAutomaticDuplicate()
    {
        var queue = Queue(1); queue.AppendRecommendations(queue.SourceVersion, [Track(2), Track(3)]);
        var manual = queue.Enqueue(Track(2));
        Assert.Single(queue.Snapshot.Recommendations);
        Assert.Equal(manual, queue.Next()); Assert.Equal(3, queue.Next()!.Track.Id);
    }
    [Fact]
    public void FailedRecommendationDoesNotBecomePlayedHistoryOrForwardHistory()
    {
        var queue = Queue(1); queue.AppendRecommendations(queue.SourceVersion, [Track(2), Track(3)]);
        queue.Next(); queue.SetPreparation(queue.Current!.EntryId, PlaybackPreparation.Failed);
        Assert.Equal(3, queue.Next()!.Track.Id);
        Assert.Equal(new long[] { 1 }, queue.Snapshot.History.Select(entry => entry.Track.Id));
        queue.SetPreparation(queue.Current.EntryId, PlaybackPreparation.Failed);
        Assert.Equal(1, queue.Previous()!.Track.Id); Assert.Empty(queue.Snapshot.Forward);
    }
    [Fact]
    public void SourceSwitchRejectsOldResponsesAndRadioWorksWithAutoplayDisabled()
    {
        var queue = Queue(1); var old = queue.SourceVersion;
        queue.StartContext(new("other", "Other", ""), [Track(10)], 0);
        Assert.False(queue.AppendRecommendations(old, [Track(2)]));
        queue.SetAutoContinue(false);
        queue.StartContext(new("radio:20", "Radio", "", Kind: PlaybackContextKind.Radio), [Track(20)], 0);
        Assert.True(queue.Snapshot.CanLoadRecommendations);
        Assert.True(queue.AppendRecommendations(queue.SourceVersion, [Track(21)]));
        Assert.Equal(21, queue.Next()!.Track.Id);
    }
    [Fact]
    public void SelectionOfContinuationSkipsSourceTailAndKeepsManualAdditions()
    {
        var queue = Queue(1, 2, 3); queue.AppendRecommendations(queue.SourceVersion, [Track(4), Track(5)]);
        var manual = queue.Enqueue(Track(8));
        var recommendation = queue.Snapshot.Recommendations[0];
        Assert.Equal(recommendation, queue.Select(recommendation.EntryId));
        Assert.Empty(queue.Snapshot.ContextUpcoming); Assert.Equal(manual, queue.Next()); Assert.Equal(5, queue.Next()!.Track.Id);
    }
    [Fact]
    public async Task LoaderFinishesPaginationBeforeAddingASeparateContinuation()
    {
        var queue = Queue(1); queue.StartContext(new("paged", "Playlist", "", "page2"), [Track(1)], 0);
        var calls = new List<string>();
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) =>
            { calls.Add("source"); return Task.FromResult(new TrackPage([Track(2)], null)); }, () => { }, CancellationToken.None,
            (state, _) => { calls.Add("recommend"); Assert.Null(state.Context!.NextHref); return Task.FromResult(new RecommendationBatch([Track(3)])); });
        await loader.EnsureAheadAsync();
        Assert.Equal(new[] { "source", "recommend" }, calls);
        Assert.Equal(2, Assert.Single(queue.Snapshot.ContextUpcoming).Track.Id);
        Assert.Equal(3, Assert.Single(queue.Snapshot.Recommendations).Track.Id);
    }
    [Fact]
    public async Task ContinuationIsSingleFlightAndNavigationCancelsItsResult()
    {
        var queue = Queue(1); var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource<RecommendationBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) => throw new InvalidOperationException(), () => { }, CancellationToken.None,
            (_, _) => { requested.TrySetResult(); return ready.Task; });
        var first = loader.EnsureAheadAsync(); Assert.Same(first, loader.EnsureAheadAsync()); await requested.Task;
        queue.StartContext(new("other", "Other", ""), [Track(10)], 0); loader.ContextChanged();
        ready.SetResult(new([Track(2)])); await first;
        Assert.Empty(queue.Snapshot.Recommendations); Assert.Equal(10, queue.Current!.Track.Id);
    }
    [Fact]
    public async Task EmptyResponseStopsRepeatedPrefetchWhileTransientFailureCanRetry()
    {
        var queue = Queue(1); var calls = 0;
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) => throw new InvalidOperationException(), () => { }, CancellationToken.None,
            (_, _) => ++calls == 1 ? Task.FromException<RecommendationBatch>(new IOException("Offline")) : Task.FromResult(new RecommendationBatch([], Exhausted: true)));
        await Assert.ThrowsAsync<IOException>(() => loader.EnsureAheadAsync());
        await loader.EnsureAheadAsync(); await loader.EnsureAheadAsync();
        Assert.Equal(2, calls); Assert.True(queue.Snapshot.RecommendationsExhausted); Assert.Null(queue.Next());
    }
    [Fact]
    public async Task FiltersReuploadsWithoutReorderingSoundCloudPagesOrCollapsingSpeedEdits()
    {
        var seed = Track(1, "Artist — Song");
        var queue = new PlaybackQueue();
        queue.StartContext(new("radio:1", "Radio", "", Kind: PlaybackContextKind.Radio), [seed], 0);
        var manual = queue.Enqueue(Track(9, "Manual"));
        var calls = new List<(long Id, string? Cursor)>();
        var continuation = new QueueContinuation();
        Task<TrackPage> Fetch(long id, string? cursor, CancellationToken token)
        {
            calls.Add((id, cursor));
            return Task.FromResult(cursor == null
                ? new TrackPage([Track(20, "Song (re-upload)") with { Duration = 121000 }, Track(4), Track(5) with { Access = "blocked" },
                    Track(6), Track(21, "Artist - Song (sped up)") with { Duration = 100000 }, Track(8), Track(9), Track(10)], "page2")
                : new TrackPage([Track(11)], null));
        }
        var first = await continuation.FetchAsync(queue.Snapshot, track => track.Id is not (4 or 6), Fetch, CancellationToken.None);
        Assert.Equal(new long[] { 21, 8, 10 }, first.Tracks.Select(t => t.Id));
        Assert.Equal(new[] { (1L, (string?)null) }, calls);
        queue.AppendRecommendations(queue.SourceVersion, first.Tracks, first.Exhausted);
        Assert.Equal(manual, queue.Next());
        Assert.Equal(21, queue.Next()!.Track.Id);
        var next = await continuation.FetchAsync(queue.Snapshot, _ => true, Fetch, CancellationToken.None);
        Assert.Equal(11, Assert.Single(next.Tracks).Id);
        Assert.Equal((1L, "page2"), calls[1]);
    }

    [Fact]
    public async Task DuplicateOnlyRelatedPagesDoNotPrematurelyEndContinuation()
    {
        var queue = Queue(1); var calls = 0;
        var result = await new QueueContinuation().FetchAsync(queue.Snapshot, _ => true, (_, cursor, _) =>
        { calls++; return Task.FromResult(cursor == null ? new TrackPage([Track(1)], "page2") : new TrackPage([Track(2)], null)); }, CancellationToken.None);
        Assert.Equal(2, calls); Assert.Equal(2, Assert.Single(result.Tracks).Id);
    }
}
