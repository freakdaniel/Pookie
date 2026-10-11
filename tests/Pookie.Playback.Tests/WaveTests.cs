using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class WaveTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
    private static SoundCloudTrack Track(long id, string artist = "Artist", string? title = null) => new() {
        Id = id, Title = title ?? "Track " + id, Duration = 180000, Genre = "Rock", User = new() { Id = id, Username = artist } };
    [Fact]
    public void ModesRespectFavoriteAndUnknownBoundaries()
    {
        var taste = new WaveTaste();
        taste.Import(Track(1), true, true); taste.Import(Track(2), true);
        var candidates = new[] { Track(1), Track(2), Track(3), Track(4) with { Access = "blocked" } };
        taste.SetMode(WaveMode.Favorites);
        Assert.Equal(1, Assert.Single(taste.Rank(candidates, [], 10, Now)).Id);
        taste.SetMode(WaveMode.Discoveries);
        Assert.Equal(3, Assert.Single(taste.Rank(candidates, [], 10, Now)).Id);
        taste.SetMode(WaveMode.Balance);
        Assert.Equal(3, taste.Rank(candidates, [], 10, Now).Length);
    }
    [Fact]
    public void DislikeExcludesOtherVersionsAndCanBeReset()
    {
        var taste = new WaveTaste(); var original = Track(1, title: "Song");
        taste.Block(original);
        var variants = new[] { original, Track(2, title: "Song (𝙨𝙥𝙚𝙚𝙙 𝙪𝙥)"), Track(3, title: "Song speed up + reverb"), Track(4, title: "Other") };
        Assert.Equal(4, Assert.Single(taste.Rank(variants, [], 10, Now)).Id);
        Assert.Equal(4, Assert.Single(taste.Seeds(variants)).Id);
        Assert.Empty(taste.Rank([original with { Title = "Changed metadata", User = new() { Username = "New uploader" } }], [], 10, Now));
        taste.ResetBlocks();
        Assert.Equal(2, taste.Rank(variants, [], 10, Now).Length);
    }
    [Fact]
    public void DiscoveriesDoNotTreatARecognizedVersionOfFamiliarMusicAsNew()
    {
        var taste = new WaveTaste(); taste.Import(Track(1, title: "Song"), true, true);
        taste.SetMode(WaveMode.Discoveries);
        Assert.Equal(3, Assert.Single(taste.Rank([Track(2, title: "Song speed up + reverb"),
            Track(3, title: "Other")], [], 10, Now)).Id);
    }
    [Fact]
    public void DuplicateWorksAndRecentTracksCannotFillTheQueue()
    {
        var taste = new WaveTaste();
        var tracks = new[] { Track(1, title: "Song"), Track(2, title: "Song (speed up)"), Track(3, title: "Other"), Track(4, title: "Fresh") };
        var ranked = taste.Rank(tracks, [Track(3, title: "Other")], 20, Now);
        Assert.Equal(2, ranked.Length); Assert.DoesNotContain(ranked, track => track.Id == 3);
        Assert.Equal(ranked.Length, ranked.Select(WaveTaste.WorkKey).Distinct().Count());
    }
    [Fact]
    public void SmallLibraryCooldownRelaxesWithoutImmediatelyRepeatingCurrent()
    {
        var taste = new WaveTaste(); taste.Import(Track(1), true, true); taste.Import(Track(2), true, true);
        taste.SetMode(WaveMode.Favorites);
        Assert.Equal(1, Assert.Single(taste.Rank([Track(1), Track(2)], [Track(1), Track(2)], 10, Now)).Id);
    }
    [Fact]
    public void CooldownRelaxationCannotRepeatCurrentForwardOrManualTracks()
    {
        var taste = new WaveTaste(); var candidates = Enumerable.Range(1, 4).Select(id => Track(id)).ToArray();
        foreach (var track in candidates) taste.Import(track, true, true);
        taste.SetMode(WaveMode.Favorites);
        Assert.Equal(4, Assert.Single(taste.Rank(candidates, candidates, 10, Now,
            reserved: candidates.Take(3))).Id);
        Assert.Empty(taste.Rank(candidates, candidates, 10, Now, reserved: candidates));
    }
    [Fact]
    public void ArtistMetadataSeparatesUploaderFromMusician()
    {
        var one = Track(1, "Uploader1") with { PublisherMetadata = new() { Artist = "Musician", Isrc = "ABC123" } };
        var two = Track(2, "Uploader2") with { PublisherMetadata = new() { Artist = "Musician", Isrc = "ABC123" } };
        Assert.Equal(WaveTaste.ArtistKey(one), WaveTaste.ArtistKey(two));
        Assert.Single(new WaveTaste().Rank([one, two], [], 20, Now));
        var restored = WaveTrack.From(one).ToTrack();
        Assert.Equal("Uploader1", restored.Author);
        Assert.Equal("Musician", restored.PublisherMetadata!.Artist);
    }
    [Fact]
    public void ArtistDiversityAndBalanceSeparateKnownAndNewMusic()
    {
        var taste = new WaveTaste();
        foreach (var id in Enumerable.Range(1, 4)) taste.Import(Track(id, "A"), true, true);
        var ranked = taste.Rank(Enumerable.Range(1, 4).Select(id => Track(id, "A"))
            .Concat(Enumerable.Range(5, 12).Select(id => Track(id, "Artist" + id))), [], 6, Now, new Random(42));
        Assert.True(ranked[0].Id < 5); Assert.True(ranked[1].Id >= 5); Assert.True(ranked[2].Id >= 5);
        Assert.NotEqual(WaveTaste.ArtistKey(ranked[1]), WaveTaste.ArtistKey(ranked[2]));
    }
    [Fact]
    public void ListeningIgnoresSeekPauseBufferingAndStalledPolling()
    {
        var taste = new WaveTaste(); var observation = new WaveListening();
        observation.Begin(Track(1), true, TimeSpan.Zero);
        observation.Poll(0, true, TimeSpan.Zero);
        observation.Poll(1, true, TimeSpan.FromSeconds(1));
        observation.Poll(90, true, TimeSpan.FromSeconds(2));
        observation.Poll(91, false, TimeSpan.FromSeconds(3));
        observation.Poll(92, false, TimeSpan.FromSeconds(4));
        observation.Poll(92, true, TimeSpan.FromSeconds(5));
        observation.Poll(93, true, TimeSpan.FromSeconds(6));
        observation.Poll(120, true, TimeSpan.FromSeconds(30));
        Assert.Equal(2, observation.Seconds);
        observation.Finish(taste, false, Now);
        Assert.Equal(2, Assert.Single(taste.Snapshot().Tracks).ListenedSeconds);
        Assert.Equal(0, Assert.Single(taste.Snapshot().Tracks).EarlySkips);
    }
    [Fact]
    public void EarlySkipIsWeakAndAppliesOnlyToWaveRecommendations()
    {
        var taste = new WaveTaste(); var observation = new WaveListening();
        observation.Begin(Track(1), false, TimeSpan.Zero); observation.Finish(taste, true, Now);
        observation.Begin(Track(2), true, TimeSpan.Zero); observation.Finish(taste, true, Now);
        Assert.Equal(0, taste.Snapshot().Tracks.Single(item => item.Track.Id == 1).EarlySkips);
        Assert.Equal(1, taste.Snapshot().Tracks.Single(item => item.Track.Id == 2).EarlySkips);
        Assert.Equal(2, taste.Rank([Track(1), Track(2)], [], 10, Now).Length);
    }
    [Fact]
    public void GoodListeningAndRepeatAreRemembered()
    {
        var taste = new WaveTaste();
        taste.Record(Track(1), 90, false, Now); taste.Record(Track(1), 100, false, Now);
        var state = Assert.Single(taste.Snapshot().Tracks);
        Assert.Equal(2, state.GoodPlays); Assert.Equal(190, state.ListenedSeconds);
        taste.SetMode(WaveMode.Favorites); Assert.Single(taste.Rank([Track(1), Track(2)], [], 10, Now));
    }
    [Fact]
    public void ProfilePersistsPerAccountWithoutAuthorizationAndRecoversCorruptFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "pookie-wave-unit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WaveProfileStore(path); var taste = new WaveTaste();
            taste.Import(Track(1) with { TrackAuthorization = "SECRET", PublisherMetadata = new() { Artist = "Music" } }, true, true);
            taste.SetMode(WaveMode.Favorites); store.Save(42, taste);
            var restored = store.Load(42);
            Assert.Equal(WaveMode.Favorites, restored.Mode); Assert.Single(restored.Snapshot().Tracks);
            Assert.Empty(store.Load(43).Snapshot().Tracks);
            Assert.DoesNotContain("SECRET", File.ReadAllText(Path.Combine(path, "42.json")));
            File.WriteAllText(Path.Combine(path, "42.json"), "broken"); Assert.Empty(store.Load(42).Snapshot().Tracks);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact]
    public void RelatedSourcesReactToSessionPreferenceInsteadOfRandomlyMixingAllCandidates()
    {
        var taste = new WaveTaste(); var seed = Track(1, "A"); var other = Track(3, "B");
        taste.Import(seed, true, true);
        taste.Relate(seed, [Track(2, "Candidate A")]); taste.Relate(other, [Track(4, "Candidate B")]);
        taste.SetMode(WaveMode.Discoveries);
        Assert.Equal(2, taste.Rank([Track(2, "Candidate A"), Track(4, "Candidate B")], [], 1, Now, new Random(1))[0].Id);
        for (var i = 0; i < 3; i++) taste.Record(other, 90, false, Now);
        Assert.Equal(4, taste.Rank([Track(2, "Candidate A"), Track(4, "Candidate B")], [], 1, Now, new Random(1))[0].Id);
    }
    [Fact]
    public void RemovingRemoteLikeDoesNotLeaveItInFavoriteMode()
    {
        var taste = new WaveTaste(); taste.Import(Track(1), true, true); taste.SetMode(WaveMode.Favorites);
        taste.SyncLikes(new HashSet<long>()); Assert.Empty(taste.Rank([Track(1)], [], 10, Now));
    }
    [Fact]
    public async Task RefillRetargetsFeedbackCursorWithoutAppendingAnOldBatch()
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("wave", "Wave", "", "first", Kind: PlaybackContextKind.Wave), [Track(1)], 0);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var loader = new PlaybackQueueLoader(queue, async (_, _, _) => {
            if (++requests == 1) { entered.SetResult(); await release.Task; return new([Track(7)], "old"); }
            return new([Track(9)], "next");
        }, () => { }, CancellationToken.None);
        var load = loader.EnsureAheadAsync(); await entered.Task;
        var manual = queue.Enqueue(Track(50)); queue.ReplaceRecommendations(queue.SourceVersion, [Track(8)]);
        release.SetResult(); await load;
        Assert.Equal(2, requests); Assert.Equal(1, queue.Current!.Track.Id);
        Assert.Equal(manual, Assert.Single(queue.Snapshot.ManualUpcoming));
        Assert.Equal(new long[] { 8, 9 }, queue.Snapshot.ContextUpcoming.Select(entry => entry.Track.Id));
    }
    [Fact]
    public void RerankPreservesCurrentHistoryManualAndForwardEntriesAndRejectsStaleContext()
    {
        var queue = new PlaybackQueue();
        queue.StartContext(new("wave", "Wave", "", "pookie:wave:1", Kind: PlaybackContextKind.Wave), [Track(1), Track(2), Track(3)], 0);
        queue.Next(); queue.Previous(); var manual = queue.Enqueue(Track(50));
        var snapshot = queue.Snapshot;
        Assert.True(queue.ReplaceRecommendations(queue.SourceVersion, [Track(4), Track(5)]));
        Assert.Equal(snapshot.Current, queue.Current); Assert.Equal(snapshot.Forward, queue.Snapshot.Forward);
        Assert.Equal(manual, Assert.Single(queue.Snapshot.ManualUpcoming));
        Assert.Equal(new long[] { 4, 5 }, queue.Snapshot.ContextUpcoming.Select(entry => entry.Track.Id));
        var version = queue.SourceVersion; queue.StartContext(new("other", "Other", ""), [Track(9)], 0);
        Assert.False(queue.ReplaceRecommendations(version, [Track(6)]));
        Assert.False(queue.ReplaceRecommendations(queue.SourceVersion, [Track(6)]));
        Assert.Equal(9, queue.Current!.Track.Id);
    }
}
