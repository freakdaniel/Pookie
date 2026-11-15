using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class FilteredQueueTests
{
    private static SoundCloudTrack Track(long id, string title = "Другой трек", string author = "Другой артист") =>
        new() { Id = id, Title = title, User = new() { Username = author } };
    private static SoundCloudTrack[] Tracks() =>
        [Track(1), Track(2, author: "T-Fest"), Track(3), Track(4, "T-Fest — песня"), Track(2, author: "T-Fest")];

    [Fact]
    public void FilteredSourceKeepsVisibleOrderAndOriginalDuplicatePositions()
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("likes", "Лайки", "Фильтр", Filter: " t-fEST "), Tracks(), 3);
        Assert.Equal("t-fEST", queue.Context!.Filter);
        Assert.Equal(new long[] { 2, 4, 2 }, queue.Snapshot.Source.Select(e => e.Track.Id));
        Assert.Equal(new[] { 1, 3, 4 }, queue.Snapshot.Source.Select(e => e.SourceIndex));
        Assert.Equal(3, queue.Current!.SourceIndex);
        Assert.Equal(4, Assert.Single(queue.Snapshot.ContextUpcoming).SourceIndex);
        Assert.Equal(4, queue.Next()!.SourceIndex);
        Assert.Equal(3, queue.Previous()!.SourceIndex);
        Assert.Equal(4, queue.Next()!.SourceIndex);
        Assert.Null(queue.Next());
    }

    [Fact]
    public void PaginationUsesCapturedFilterAndCountsExcludedTracksInSourcePositions()
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("playlist", "Плейлист", "Фильтр", "page2", Filter: "T-Fest"), Tracks(), 4);
        Assert.True(queue.Append(queue.SourceVersion, "page2", new([Track(5), Track(6)], "page3")));
        Assert.Empty(queue.Snapshot.ContextUpcoming);
        Assert.True(queue.Append(queue.SourceVersion, "page3", new([Track(7), Track(8, author: "t-fest"), Track(9, "T-FEST"), Track(10)], null)));
        Assert.Equal(new long[] { 8, 9 }, queue.Snapshot.ContextUpcoming.Select(e => e.Track.Id));
        Assert.Equal(new[] { 8, 9 }, queue.Snapshot.ContextUpcoming.Select(e => e.SourceIndex));
        Assert.Equal("T-Fest", queue.Context!.Filter);
        Assert.Null(queue.Context.NextHref);
        Assert.False(queue.Snapshot.CanLoadRecommendations);
        Assert.False(queue.AppendRecommendations(queue.SourceVersion, [Track(99)]));
    }

    [Fact]
    public void ShuffleRepeatAndManualAdditionsRespectTheFilteredSource()
    {
        var queue = new PlaybackQueue(new Random(73));
        queue.SetShuffle(true);
        queue.StartContext(new("likes", "Лайки", "Фильтр", Filter: "T-Fest"), Tracks(), 1);
        Assert.Equal(new[] { 3, 4 }, queue.Snapshot.ContextUpcoming.Select(e => e.SourceIndex).Order());
        queue.SetShuffle(false);
        Assert.Equal(new[] { 3, 4 }, queue.Snapshot.ContextUpcoming.Select(e => e.SourceIndex));
        queue.SetRepeat(RepeatMode.Context);
        Assert.Equal(3, queue.Next()!.SourceIndex);
        Assert.Equal(4, queue.Next()!.SourceIndex);
        Assert.Equal(1, queue.Next()!.SourceIndex);
        queue.Enqueue(Track(99), next: true);
        Assert.Equal(99, queue.Next()!.Track.Id);
        Assert.Equal(new long[] { 2, 4, 2 }, queue.Snapshot.Source.Select(e => e.Track.Id));
    }

    [Fact]
    public void EmptyFilteredCursorCycleEndsAndAnotherContextDropsTheFilter()
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("likes", "Лайки", "Фильтр", "page2", Filter: "T-Fest"), Tracks(), 4);
        var version = queue.SourceVersion;
        queue.Append(version, "page2", new([Track(5)], "page3"));
        queue.Append(version, "page3", new([Track(6)], "page2"));
        Assert.Null(queue.Context!.NextHref);
        Assert.Null(queue.Next());
        queue.StartContext(new("likes", "Лайки", "Все треки"), Tracks(), 0);
        Assert.Null(queue.Context!.Filter);
        Assert.Equal(5, queue.Snapshot.Source.Count);
        Assert.True(queue.Snapshot.CanLoadRecommendations);
        Assert.False(queue.Append(version, "page2", new([Track(7, "T-Fest")], null)));
    }

    [Theory]
    [InlineData("   ", true)]
    [InlineData(" t-fEST ", true)]
    [InlineData("ПЕСНЯ", true)]
    [InlineData("другой", false)]
    public void TheSharedMatcherChecksArtistAndTitle(string filter, bool expected) =>
        Assert.Equal(expected, TrackFilter.Matches(Track(1, "Новая песня", "T-Fest"), filter));
}
