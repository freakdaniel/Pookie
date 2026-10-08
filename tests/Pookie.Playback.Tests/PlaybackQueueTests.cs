using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class PlaybackQueueTests
{
    private static SoundCloudTrack Track(long id) => new() { Id = id, Title = "Track " + id };
    private static PlaybackQueue Queue(params long[] ids)
    {
        var queue = new PlaybackQueue(new Random(73));
        queue.StartContext(new("likes", "Лайки", "Сейчас играет из подборки"), ids.Select(Track), 0);
        return queue;
    }
    private static long[] Ids(IEnumerable<PlaybackEntry> entries) => entries.Select(entry => entry.Track.Id).ToArray();

    [Fact]
    public void ManualEntriesHavePriorityAndDuplicatesHaveIndependentIdentities()
    {
        var queue = Queue(1, 2, 3);
        var duplicate = queue.Enqueue(Track(1));
        var next = queue.Enqueue(Track(4), next: true);
        Assert.Equal(new long[] { 4, 1, 2, 3 }, Ids(queue.Snapshot.Upcoming));
        Assert.Equal(next.EntryId, queue.Next()!.EntryId);
        Assert.Equal(duplicate.EntryId, queue.Next()!.EntryId);
        Assert.Equal(2, queue.Next()!.Track.Id);
        Assert.Equal(new long[] { 1, 4, 1 }, Ids(queue.Snapshot.History));
        Assert.Equal(3, queue.Snapshot.History.Select(entry => entry.EntryId).Distinct().Count());
    }

    [Fact]
    public void RemoveMoveAndClearOnlyAffectManualOccurrences()
    {
        var queue = Queue(1, 2);
        var first = queue.Enqueue(Track(2));
        var second = queue.Enqueue(Track(2));
        var third = queue.Enqueue(Track(3));
        Assert.True(queue.MoveManual(third.EntryId, 0));
        Assert.True(queue.RemoveManual(first.EntryId));
        Assert.Equal(new[] { third.EntryId, second.EntryId }, queue.Snapshot.ManualUpcoming.Select(entry => entry.EntryId));
        Assert.False(queue.RemoveManual(queue.Snapshot.ContextUpcoming[0].EntryId));
        queue.ClearManual();
        Assert.Equal(new long[] { 2 }, Ids(queue.Snapshot.Upcoming));
    }

    [Fact]
    public void PreviousAndForwardUseActualPlaybackHistory()
    {
        var queue = Queue(1, 2, 3, 4);
        var manual = queue.Enqueue(Track(8));
        queue.Next(); queue.Next();
        Assert.Equal(new long[] { 1, 8 }, Ids(queue.Snapshot.History));
        Assert.Equal(manual.EntryId, queue.Previous()!.EntryId);
        Assert.Equal(1, queue.Previous()!.Track.Id);
        Assert.Equal(new long[] { 8, 2, 3, 4 }, Ids(queue.Snapshot.Upcoming));
        Assert.Equal(manual.EntryId, queue.Next()!.EntryId);
        Assert.Equal(2, queue.Next()!.Track.Id);
        Assert.Equal(3, queue.Next()!.Track.Id);
    }

    [Fact]
    public void SelectingASourcePositionDoesNotInventHistoryForSkippedTracks()
    {
        var queue = Queue(1, 2, 3, 4);
        queue.Select(queue.Snapshot.ContextUpcoming[1].EntryId);
        Assert.Equal(3, queue.Current!.Track.Id);
        Assert.Equal(new long[] { 1 }, Ids(queue.Snapshot.History));
        Assert.Equal(new long[] { 4 }, Ids(queue.Snapshot.Upcoming));
        Assert.Equal(1, queue.Previous()!.Track.Id);
        Assert.Equal(3, queue.Next()!.Track.Id);
    }

    [Fact]
    public void ShuffleOrderIsStableAndSelectionPreservesItsRemainder()
    {
        var queue = Queue(1, 2, 3, 4, 5, 6, 7, 8);
        var manual = queue.Enqueue(Track(9));
        queue.SetShuffle(true);
        var order = queue.Snapshot.ContextUpcoming.ToArray();
        Assert.Equal(order, queue.Snapshot.ContextUpcoming);
        Assert.Equal(manual, queue.Snapshot.Upcoming.First());
        queue.Select(order[3].EntryId);
        Assert.Equal(order.Where(entry => entry != order[3]), queue.Snapshot.ContextUpcoming);
        Assert.Equal(manual, queue.Next());
        Assert.Equal(order[0], queue.Next());
        queue.SetShuffle(false);
        Assert.Equal(queue.Snapshot.ContextUpcoming.OrderBy(entry => entry.SourceIndex), queue.Snapshot.ContextUpcoming);
    }

    [Fact]
    public void EndOfSourceStopsWithoutWrapping()
    {
        var queue = Queue(1, 2);
        queue.Next();
        Assert.Null(queue.Next(naturalEnd: true));
        Assert.Equal(2, queue.Current!.Track.Id);
        Assert.Equal(PlaybackPreparation.Stopped, queue.Preparation);
        Assert.False(queue.Snapshot.CanNext);
    }

    [Fact]
    public void RepeatTrackAppliesOnlyToNaturalEndAndDoesNotConsumeUpcoming()
    {
        var queue = Queue(1, 2);
        queue.SetRepeat(RepeatMode.Track);
        var manual = queue.Enqueue(Track(8));
        var initial = queue.Current;
        Assert.Equal(initial, queue.Next(naturalEnd: true));
        Assert.Empty(queue.Snapshot.History);
        Assert.Equal(manual, queue.Next());
        Assert.Equal(2, queue.Next()!.Track.Id);
        Assert.Equal(2, queue.Next(naturalEnd: true)!.Track.Id);
        Assert.Null(queue.Next());
    }

    [Fact]
    public void RepeatContextCreatesNewOccurrencesAndWaitsForPagination()
    {
        var queue = Queue(1, 2, 1);
        queue.SetRepeat(RepeatMode.Context);
        queue.Next(); queue.Next();
        var last = queue.Current;
        Assert.Equal(1, queue.Next()!.Track.Id);
        Assert.NotEqual(last!.EntryId, queue.Current!.EntryId);
        Assert.Equal(4, queue.Snapshot.History.Concat([queue.Current]).Select(entry => entry.EntryId).Distinct().Count());
        queue.StartContext(new("paged", "Плейлист", "Источник", "page2"), [Track(7)], 0);
        Assert.Null(queue.Next());
        Assert.True(queue.Append(queue.SourceVersion, "page2", new([Track(8)], null)));
        Assert.Equal(8, queue.Next()!.Track.Id);
    }

    [Fact]
    public void OldSnapshotsAndOldPreparationCallbacksCannotChangeNewSelection()
    {
        var queue = Queue(1, 2, 3);
        var snapshot = queue.Snapshot;
        queue.Next();
        queue.SetPreparation(snapshot.Current!.EntryId, PlaybackPreparation.Ready, Track(10));
        Assert.Equal(2, queue.Current!.Track.Id);
        Assert.Equal(PlaybackPreparation.Preparing, queue.Preparation);
        Assert.Equal(new long[] { 2, 3 }, Ids(snapshot.Upcoming));
        var version = queue.SourceVersion;
        queue.StartContext(new("single", "Search", "Track"), [Track(20)], 0);
        Assert.False(queue.Append(version, "old-page", new([Track(21)], null)));
        Assert.Empty(queue.Snapshot.Upcoming);
        Assert.Empty(queue.Snapshot.History);
    }

    [Fact]
    public async Task PaginationIsSingleFlightAndKeepsCursorUntilSuccessfulAppend()
    {
        var queue = Queue(1);
        queue.StartContext(new("paged", "Paged", "Source", "page2"), [Track(1)], 0);
        var completion = new TaskCompletionSource<TrackPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0; var changes = 0;
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) => { calls++; return completion.Task; }, () => changes++, CancellationToken.None);
        var first = loader.EnsureAheadAsync(); var second = loader.EnsureAheadAsync();
        Assert.Same(first, second);
        await Task.Yield();
        Assert.Equal("page2", queue.Context!.NextHref);
        completion.SetResult(new([Track(2), Track(2), Track(3)], "page3"));
        await first;
        Assert.Equal(1, calls); Assert.Equal(1, changes);
        Assert.Equal("page3", queue.Context.NextHref);
        Assert.Equal(new long[] { 2, 2, 3 }, Ids(queue.Snapshot.Upcoming));
        Assert.Equal(3, queue.Snapshot.Upcoming.Select(entry => entry.EntryId).Distinct().Count());
    }

    [Fact]
    public async Task ReplacementCancelsAndRejectsAResponseThatIgnoresCancellation()
    {
        var queue = Queue(1);
        queue.StartContext(new("old", "Old", "Source", "page2"), [Track(1)], 0);
        var completion = new TaskCompletionSource<TrackPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loader = new PlaybackQueueLoader(queue, (_, _, token) => { started.SetResult(token); return completion.Task; }, () => throw new Exception("Stale notification"), CancellationToken.None);
        var loading = loader.EnsureAheadAsync();
        var token = await started.Task;
        queue.StartContext(new("new", "New", "Source"), [Track(9)], 0); loader.ContextChanged();
        Assert.True(token.IsCancellationRequested);
        completion.SetResult(new([Track(2)], null)); await loading;
        Assert.Equal(9, queue.Current!.Track.Id); Assert.Empty(queue.Snapshot.Upcoming);
    }

    [Fact]
    public async Task FailedPaginationCanRetryWithoutPoisoningTheQueue()
    {
        var queue = Queue(1);
        queue.StartContext(new("paged", "Paged", "Source", "page2"), [Track(1)], 0);
        var calls = 0;
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) => ++calls == 1 ?
            Task.FromException<TrackPage>(new IOException("Offline")) : Task.FromResult(new TrackPage([Track(2)], null)), () => { }, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => loader.EnsureAheadAsync());
        Assert.Equal("page2", queue.Context!.NextHref);
        await loader.EnsureAheadAsync();
        Assert.Equal(2, calls); Assert.Equal(2, queue.Next()!.Track.Id);
    }
    [Fact]
    public async Task PrefetchWaitsUntilOnlyFiveContextEntriesRemain()
    {
        var queue = Queue(1, 2, 3, 4, 5, 6, 7);
        queue.StartContext(new("paged", "Paged", "Source", "page2"), Enumerable.Range(1, 7).Select(id => Track(id)), 0);
        var calls = 0;
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) =>
            { calls++; return Task.FromResult(new TrackPage([Track(8)], null)); }, () => { }, CancellationToken.None);
        await loader.EnsureAheadAsync(); Assert.Equal(0, calls);
        queue.Next(); await loader.EnsureAheadAsync();
        Assert.Equal(1, calls); Assert.Equal(new long[] { 3, 4, 5, 6, 7, 8 }, Ids(queue.Snapshot.Upcoming));
    }

    [Fact]
    public async Task DisposedLoaderDoesNotStartAnUnsentRequest()
    {
        var queue = Queue(1);
        queue.StartContext(new("paged", "Paged", "Source", "page2"), [Track(1)], 0);
        var calls = 0;
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) =>
            { calls++; return Task.FromResult(new TrackPage([], null)); }, () => { }, CancellationToken.None);
        // The UI dispatcher holds Task.Yield until the current event finishes.
        // A thread-pool continuation can otherwise beat Dispose in this test.
        var previous = SynchronizationContext.Current;
        var dispatcher = new HeldDispatcher();
        Task pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(dispatcher);
            pending = loader.EnsureAheadAsync(); loader.Dispose();
            dispatcher.RunPosted();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, calls);
    }

    private sealed class HeldDispatcher : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> posted = new();
        public override void Post(SendOrPostCallback callback, object? state) => posted.Enqueue((callback, state));
        public void RunPosted() { while (posted.TryDequeue(out var item)) item.Callback(item.State); }
    }

    [Fact]
    public void InitialShuffleUsesAllOtherOccurrencesAndClearKeepsPreferences()
    {
        var queue = new PlaybackQueue(new Random(73)); queue.SetShuffle(true); queue.SetRepeat(RepeatMode.Context);
        queue.StartContext(new("album", "Album", "Source"), [Track(1), Track(2), Track(2), Track(3)], 2);
        Assert.Equal(2, queue.Current!.SourceIndex);
        Assert.Equal(new[] { 0, 1, 3 }, queue.Snapshot.ContextUpcoming.Select(entry => entry.SourceIndex).Order());
        queue.Clear(); Assert.Null(queue.Current); Assert.True(queue.Shuffle); Assert.Equal(RepeatMode.Context, queue.Repeat);
    }

    [Fact]
    public void ManualQueueWorksBeforeAnyContextIsStarted()
    {
        var queue = new PlaybackQueue(); var entry = queue.Enqueue(Track(1));
        Assert.True(queue.Snapshot.CanNext); Assert.Equal(entry, queue.Next());
        Assert.Null(queue.Next()); Assert.Equal(entry, queue.Current);
    }

    [Fact]
    public void SnapshotsCannotBeMutatedByTheirConsumers()
    {
        var queue = Queue(1, 2);
        var entries = (IList<PlaybackEntry>)queue.Snapshot.ContextUpcoming;
        Assert.True(entries.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => entries.Clear());
        Assert.Equal(2, queue.Next()!.Track.Id);
    }

    [Fact]
    public void PlayNextKeepsPriorityAfterGoingBackThroughHistory()
    {
        var queue = Queue(1, 2, 3); queue.Next(); queue.Previous();
        var manual = queue.Enqueue(Track(9), next: true);
        Assert.Equal(new long[] { 9, 2, 3 }, Ids(queue.Snapshot.Upcoming));
        Assert.Equal(manual, queue.Next()); Assert.Equal(2, queue.Next()!.Track.Id);
    }

    [Fact]
    public void SelectingForwardDoesNotAccidentallyConsumeManualEntries()
    {
        var queue = Queue(1, 2, 3, 4); queue.Next(); queue.Next(); queue.Previous(); queue.Previous();
        var forward = queue.Snapshot.Forward[1];
        var manual = queue.Enqueue(Track(9));
        Assert.Equal(forward, queue.Select(forward.EntryId));
        Assert.Equal(manual, queue.Next()); Assert.Equal(4, queue.Next()!.Track.Id);
    }

}
