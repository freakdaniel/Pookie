using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class WaveQueueTests
{
    [Fact]
    public void MissingRecommendationIsRemovedWithoutReorderingRetainedOccurrencesOrManualEntries()
    {
        var queue = new PlaybackQueue();
        var tracks = Enumerable.Range(1, 4).Select(id => new SoundCloudTrack { Id = id, Title = "Track " + id }).ToArray();
        queue.StartContext(new("wave", "Wave", "", Kind: PlaybackContextKind.Wave), tracks, 0);
        var committed = queue.Snapshot.ContextUpcoming.ToArray(); var manual = queue.Enqueue(tracks[1]);
        queue.RemoveUnavailableRecommendation(2);
        Assert.Equal(committed.Skip(1), queue.Snapshot.ContextUpcoming);
        Assert.Equal(manual, Assert.Single(queue.Snapshot.ManualUpcoming));
        Assert.DoesNotContain(queue.Snapshot.Source, entry => entry.Track.Id == 2);
        Assert.Equal(1, queue.Current!.Track.Id);
    }
    [Fact]
    public void FailedCurrentRecommendationCannotReturnThroughContextRepeat()
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("wave", "Wave", "", Kind: PlaybackContextKind.Wave), new[] { Track(1), Track(2) }, 0);
        queue.SetRepeat(RepeatMode.Context);
        queue.RemoveUnavailableRecommendation(1);
        Assert.Equal(1, queue.Current!.Track.Id);
        Assert.Equal(2, queue.Next()!.Track.Id);
        Assert.Equal(2, queue.Next()!.Track.Id);
        Assert.DoesNotContain(queue.Snapshot.Source, entry => entry.Track.Id == 1);
    }
    private static SoundCloudTrack Track(long id) => new() { Id = id, Title = "Track " + id };
    private static PlaybackQueue Wave(params long[] ids)
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("wave", "Wave", "", "first", Kind: PlaybackContextKind.Wave), ids.Select(Track), 0);
        return queue;
    }
    private static long[] Ids(PlaybackQueue queue) => queue.Snapshot.ContextUpcoming.Select(entry => entry.Track.Id).ToArray();

    [Fact]
    public void InitialWaveCommitsOnlyThreeUpcomingTracks()
    {
        var queue = Wave(1, 2, 3, 4, 5, 6, 7);
        Assert.Equal(new long[] { 2, 3, 4 }, Ids(queue));
        Assert.Equal(4, queue.Snapshot.Source.Count);
    }

    [Fact]
    public void RepeatedLearningDoesNotReorderRecreateOrInvalidateCommittedQueue()
    {
        var queue = Wave(1, 2, 3, 4);
        var snapshot = queue.Snapshot;
        for (var i = 0; i < 100; i++)
            Assert.True(queue.ReplaceRecommendations(queue.SourceVersion, [Track(7), Track(4), Track(3), Track(2)]));
        Assert.Same(snapshot, queue.Snapshot);
        Assert.Equal(new long[] { 2, 3, 4 }, Ids(queue));
    }

    [Fact]
    public void AdvanceKeepsRemainingOccurrencesAndAppendsOneLatestCandidate()
    {
        var queue = Wave(1, 2, 3, 4);
        var remaining = queue.Snapshot.ContextUpcoming.Skip(1).ToArray();
        var manual = queue.Enqueue(Track(9)); queue.Next();
        Assert.Equal(manual, queue.Current);
        queue.Next(); Assert.Equal(2, queue.Current!.Track.Id);
        queue.ReplaceRecommendations(queue.SourceVersion, [Track(2), Track(3), Track(8), Track(8), Track(7)]);
        Assert.Equal(new long[] { 3, 4, 8 }, Ids(queue));
        Assert.Equal(remaining, queue.Snapshot.ContextUpcoming.Take(2));
        Assert.Equal(3, queue.Next()!.Track.Id);
    }

    [Fact]
    public void ExplicitRebuildReusesSurvivingIdentitiesAndRejectsOldRefill()
    {
        var queue = Wave(1, 2, 3, 4);
        var entries = queue.Snapshot.ContextUpcoming.ToArray();
        var manual = queue.Enqueue(Track(9));
        queue.ReplaceRecommendations(queue.SourceVersion, [Track(4), Track(2), Track(7)], rebuild: true);
        Assert.Equal(new long[] { 4, 2, 7 }, Ids(queue));
        Assert.Equal(entries[2].EntryId, queue.Snapshot.ContextUpcoming[0].EntryId);
        Assert.Equal(entries[0].EntryId, queue.Snapshot.ContextUpcoming[1].EntryId);
        Assert.Equal(manual, Assert.Single(queue.Snapshot.ManualUpcoming));
        Assert.Equal(1, queue.Current!.Track.Id);
        Assert.False(queue.Append(queue.SourceVersion, "first", new([Track(8)], "old")));
        var cursor = queue.Context!.NextHref;
        var retained = queue.Snapshot.ContextUpcoming.ToArray();
        queue.ReplaceRecommendations(queue.SourceVersion, retained.Select(entry => entry.Track), rebuild: true);
        Assert.Equal(retained, queue.Snapshot.ContextUpcoming);
        Assert.NotEqual(cursor, queue.Context.NextHref);
    }

    [Fact]
    public void RefillCapsHorizonAndExcludesCurrentManualAndDuplicateCandidates()
    {
        var queue = Wave(1, 2);
        var committed = Assert.Single(queue.Snapshot.ContextUpcoming);
        queue.Enqueue(Track(9));
        Assert.True(queue.Append(queue.SourceVersion, "first", new([Track(1), Track(2), Track(9), Track(3), Track(3), Track(4), Track(5)], "next")));
        Assert.Equal(new long[] { 2, 3, 4 }, Ids(queue));
        Assert.Equal(committed, queue.Snapshot.ContextUpcoming[0]);
    }

    [Fact]
    public async Task FullWaveDoesNotFetchUntilASlotOpensAndOrdinaryLearningKeepsInFlightRefill()
    {
        var queue = Wave(1, 2, 3, 4);
        var calls = 0;
        var response = new TaskCompletionSource<TrackPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loader = new PlaybackQueueLoader(queue, (_, _, _) => { calls++; entered.SetResult(); return response.Task; }, () => { }, CancellationToken.None);
        await loader.EnsureAheadAsync(); Assert.Equal(0, calls);
        queue.Next(); var load = loader.EnsureAheadAsync(); await entered.Task;
        queue.ReplaceRecommendations(queue.SourceVersion, [Track(7)]);
        response.SetResult(new([Track(8)], "next")); await load;
        Assert.Equal(1, calls);
        Assert.Equal(new long[] { 3, 4, 7 }, Ids(queue));
    }

    [Fact]
    public void EmptyRebuildKeepsRefillAndForwardHistoryAndManualOccurrences()
    {
        var queue = Wave(1, 2, 3, 4);
        queue.Next(); queue.Previous();
        var before = queue.Snapshot; var manual = queue.Enqueue(Track(9));
        queue.ReplaceRecommendations(queue.SourceVersion, [], rebuild: true);
        Assert.Empty(queue.Snapshot.ContextUpcoming);
        Assert.Equal(before.Forward, queue.Snapshot.Forward);
        Assert.Equal(before.History, queue.Snapshot.History);
        Assert.Equal(manual, Assert.Single(queue.Snapshot.ManualUpcoming));
        Assert.True(queue.Snapshot.CanNext); Assert.NotNull(queue.Context!.NextHref);
        Assert.Equal(manual, queue.Next()); Assert.Equal(2, queue.Next()!.Track.Id);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void SingleSlotRefillKeepsTheBalanceOfFamiliarAndNewMusic(int position, bool familiar)
    {
        var taste = new Pookie.App.Home.WaveTaste();
        var known = Track(1) with { Duration = 180000, User = new() { Username = "Known", Id = 1 } };
        var fresh = Track(2) with { Duration = 180000, User = new() { Username = "New", Id = 2 } };
        taste.Import(known, familiar: true, liked: true);
        var selected = Assert.Single(taste.Rank([known, fresh], [], 1, DateTimeOffset.UtcNow, new Random(17), startIndex: position));
        Assert.Equal(familiar, taste.IsFamiliar(selected));
    }
}
