using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class QueueContinuationTests
{
    [Theory]
    [InlineData(Pookie.Recommendations.ListeningSource.Autoplay, Pookie.Recommendations.ListeningEndReason.Skip, true)]
    [InlineData(Pookie.Recommendations.ListeningSource.Autoplay, Pookie.Recommendations.ListeningEndReason.Stopped, false)]
    [InlineData(Pookie.Recommendations.ListeningSource.Autoplay, Pookie.Recommendations.ListeningEndReason.PlaybackError, false)]
    [InlineData(Pookie.Recommendations.ListeningSource.Manual, Pookie.Recommendations.ListeningEndReason.Skip, false)]
    public void OnlyIntentionalAutoplaySkipsTeachAnImplicitNegative(Pookie.Recommendations.ListeningSource source,
        Pookie.Recommendations.ListeningEndReason reason, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var track = new Pookie.Recommendations.WaveTrack(42, "Song", "Artist", 7, "", 120000, null, null, null);
        var observation = new Pookie.Recommendations.ListeningObservation(Guid.NewGuid(), Guid.NewGuid(), track,
            now, now.AddSeconds(2), 2, source, reason, Pookie.Recommendations.WaveMode.Balance);
        Assert.Equal(expected, observation.EarlySkip);
    }
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
    public void RepeatSupersedesRecommendationsAndLateResultsCannotRestoreThem()
    {
        var queue = Queue(1); queue.AppendRecommendations(queue.SourceVersion, [Track(2)]);
        queue.SetRepeat(RepeatMode.Context);
        Assert.Empty(queue.Snapshot.Recommendations);
        Assert.False(queue.AppendRecommendations(queue.SourceVersion, [Track(3)]));
        Assert.Equal(1, queue.Next()!.Track.Id);
        queue.SetRepeat(RepeatMode.Track);
        Assert.Equal(1, queue.Next(naturalEnd: true)!.Track.Id);
        queue.SetRepeat(RepeatMode.Off); Assert.True(queue.Snapshot.CanLoadRecommendations);
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
    public void SourceSwitchRejectsOldResponsesAndWaveUsesItsOwnRefill()
    {
        var queue = Queue(1); var old = queue.SourceVersion;
        queue.StartContext(new("other", "Other", ""), [Track(10)], 0);
        Assert.False(queue.AppendRecommendations(old, [Track(2)]));
        queue.StartContext(new("wave", "Wave", "", Kind: PlaybackContextKind.Wave), [Track(20)], 0);
        Assert.False(queue.Snapshot.CanLoadRecommendations);
        Assert.False(queue.AppendRecommendations(queue.SourceVersion, [Track(21)]));
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
            (state, _) => { calls.Add("recommend"); Assert.Null(state.Context!.NextHref); return Task.FromResult(new[] { Track(3) }); });
        await loader.EnsureAheadAsync();
        Assert.Equal(new[] { "source", "recommend" }, calls);
        Assert.Equal(2, Assert.Single(queue.Snapshot.ContextUpcoming).Track.Id);
        Assert.Equal(3, Assert.Single(queue.Snapshot.Recommendations).Track.Id);
    }
    [Fact]
    public async Task ContinuationIsSingleFlightAndNavigationCancelsItsResult()
    {
        var queue = Queue(1); var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource<SoundCloudTrack[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) => throw new InvalidOperationException(), () => { }, CancellationToken.None,
            (_, _) => { requested.TrySetResult(); return ready.Task; });
        var first = loader.EnsureAheadAsync(); Assert.Same(first, loader.EnsureAheadAsync()); await requested.Task;
        queue.StartContext(new("other", "Other", ""), [Track(10)], 0); loader.ContextChanged();
        ready.SetResult([Track(2)]); await first;
        Assert.Empty(queue.Snapshot.Recommendations); Assert.Equal(10, queue.Current!.Track.Id);
    }
    [Fact]
    public async Task EmptyResponseStopsRepeatedPrefetchWhileTransientFailureCanRetry()
    {
        var queue = Queue(1); var calls = 0;
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) => throw new InvalidOperationException(), () => { }, CancellationToken.None,
            (_, _) => ++calls == 1 ? Task.FromException<SoundCloudTrack[]>(new IOException("Offline")) : Task.FromResult(Array.Empty<SoundCloudTrack>()));
        await Assert.ThrowsAsync<IOException>(() => loader.EnsureAheadAsync());
        await loader.EnsureAheadAsync(); await loader.EnsureAheadAsync();
        Assert.Equal(2, calls); Assert.True(queue.Snapshot.RecommendationsExhausted); Assert.Null(queue.Next());
    }
    [Fact]
    public async Task ContinuationUsesRelatedContextAndIgnoresWaveModeWhileRespectingExclusions()
    {
        var queue = Queue(1, 2); var taste = new WaveTaste(); taste.SetMode(WaveMode.Favorites);
        taste.Block(Track(4)); taste.MarkUnavailable(Track(5), DateTimeOffset.UtcNow.AddDays(1));
        var manual = queue.Enqueue(Track(6)); var continuation = new QueueContinuation(); var seeds = new List<long>();
        var result = await continuation.FetchAsync(queue.Snapshot, taste, (id, _, _) =>
        { seeds.Add(id); return Task.FromResult(new TrackPage([Track(1), Track(2), Track(4), Track(5), Track(6), Track(7)], null)); }, CancellationToken.None);
        Assert.Equal(new long[] { 1 }, seeds); Assert.Equal(7, Assert.Single(result).Id);
        Assert.Equal(manual, Assert.Single(queue.Snapshot.ManualUpcoming));
    }
    [Fact]
    public async Task DuplicateOnlyRelatedPagesDoNotPrematurelyEndContinuation()
    {
        var queue = Queue(1); var calls = 0;
        var result = await new QueueContinuation().FetchAsync(queue.Snapshot, new WaveTaste(), (_, cursor, _) =>
        { calls++; return Task.FromResult(cursor == null ? new TrackPage([Track(1)], "page2") : new TrackPage([Track(2)], null)); }, CancellationToken.None);
        Assert.Equal(2, calls); Assert.Equal(2, Assert.Single(result).Id);
    }
    [Fact]
    public async Task PrefetchedPoolRefillsWithoutReplayingHistoryAndExpandsFromTheNewRecommendation()
    {
        var queue = Queue(1); var continuation = new QueueContinuation(); var taste = new WaveTaste(); var seeds = new List<long>();
        Task<TrackPage> Fetch(long id, string? _, CancellationToken token)
        { seeds.Add(id); return Task.FromResult(new TrackPage(Enumerable.Range(2, 12).Select(id => Track(id)).ToArray(), null)); }
        queue.AppendRecommendations(queue.SourceVersion, await continuation.FetchAsync(queue.Snapshot, taste, Fetch, CancellationToken.None));
        var played = queue.Next()!;
        var result = await continuation.FetchAsync(queue.Snapshot, taste, Fetch, CancellationToken.None);
        Assert.Contains(played.Track.Id, seeds); Assert.DoesNotContain(result, track => track.Id == played.Track.Id);
        Assert.DoesNotContain(result, track => queue.Snapshot.Upcoming.Any(entry => entry.Track.Id == track.Id));
    }
}
