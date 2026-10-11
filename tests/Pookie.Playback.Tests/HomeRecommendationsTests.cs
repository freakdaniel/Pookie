using Pookie.App.Home;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class HomeRecommendationsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
    private static SoundCloudTrack Track(long id, long artist = 1) => new() { Id = id, Title = "Track " + id,
        User = new() { Id = artist, Username = "Artist " + artist }, CreatedAt = Now.AddDays(-1).ToString("O") };
    private static HomeContent Build(TrackPage[] related, LibraryItem[]? recent = null, LibraryItem[]? history = null,
        SoundCloudTrack[]? likes = null, SoundCloudTrack[]? feed = null, LibraryItem[]? mixes = null, IReadOnlySet<long>? known = null) =>
        HomeRecommendations.Build(recent ?? [], history ?? [], likes ?? [], [], related, feed ?? [], new HashSet<long> { 1 }, mixes ?? [], Now, known);

    [Fact]
    public void RecommendationsInterleaveSeedsLimitAuthorsAndExcludeKnownAndBlockedTracks()
    {
        var content = Build([new([Track(1), Track(2), Track(3), Track(4)], null),
            new([Track(10, 2), Track(11, 3), Track(12, 4) with { Access = "blocked" }, Track(13, 4) with { Policy = "SNIP" }], null)],
            likes: [Track(1)], known: new HashSet<long> { 11 });
        Assert.Equal(new long[] { 10, 2, 3 }, content.Recommendations.Select(item => item.Track!.Id));
    }

    [Fact]
    public void ReleasesAreFreshFollowedOriginalAuthorsAndNewestFirst()
    {
        var content = Build([], feed: [Track(1) with { CreatedAt = Now.AddDays(-31).ToString("O") }, Track(2),
            Track(3, 2), Track(4) with { CreatedAt = "bad" }, Track(5) with { CreatedAt = Now.AddDays(1).ToString("O") },
            Track(6) with { CreatedAt = Now.AddDays(-2).ToString("O") }, Track(2)]);
        Assert.Equal(new long[] { 2, 6 }, content.Releases.Select(item => item.Track!.Id));
    }

    [Fact]
    public void SectionsHaveNoDuplicateEntriesAndRecommendationsExcludeHistory()
    {
        var playlist = new LibraryItem("playlist:1", "Mix", "", null, Playlist: new() { Id = 1 });
        var content = Build([new([Track(1), Track(2), Track(3)], null)], recent: [LibraryItem.FromTrack(Track(1)), playlist],
            history: [LibraryItem.FromTrack(Track(4))], feed: [Track(3), Track(5)], mixes: [playlist]);
        Assert.Equal(new long[] { 2, 3 }, content.Recommendations.Select(item => item.Track!.Id));
        Assert.Equal(5, Assert.Single(content.Releases).Track!.Id);
        Assert.Empty(content.Mixes);
        var all = content.Recent.Concat(content.Recommendations).Concat(content.Releases).Concat(content.Mixes).Select(item => item.Key).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void SeedsSkipUnavailableTracksAndIncludeLikesAndHistoryWithoutDuplicates()
    {
        var seeds = HomeRecommendations.Seeds([Track(1) with { Access = "preview" }, Track(2), Track(3), Track(4)],
            [LibraryItem.FromTrack(Track(2)), LibraryItem.FromTrack(Track(5)), LibraryItem.FromTrack(Track(6))]);
        Assert.Equal(new long[] { 2, 3, 4, 5, 6 }, seeds.Select(track => track.Id));
    }

    [Fact]
    public void CachedHomePromotesLatestLocalPlaybackAndRemovesDuplicatesInOtherSections()
    {
        var content = Build([new([Track(7, 2)], null)], recent: [LibraryItem.FromTrack(Track(1))], feed: [Track(8)]);
        var refreshed = HomeRecommendations.RefreshRecent(content, [LibraryItem.FromTrack(Track(7, 2)), LibraryItem.FromTrack(Track(1))]);
        Assert.Equal(new long[] { 7, 1 }, refreshed.Recent.Select(item => item.Track!.Id));
        Assert.Empty(refreshed.Recommendations);
        Assert.Equal(8, Assert.Single(refreshed.Releases).Track!.Id);
    }

    [Fact]
    public void NewAccountsDoNotAcquireSyntheticRecommendations()
    {
        var content = Build([]);
        Assert.Empty(content.Recent); Assert.Empty(content.Recommendations); Assert.Empty(content.Releases); Assert.Empty(content.Mixes);
    }
}
