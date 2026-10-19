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
    public void UnavailableTracksSurviveReloadAndFreshImportsWithoutBecomingDislikes()
    {
        var path = Path.Combine(Path.GetTempPath(), "pookie-wave-unavailable-" + Guid.NewGuid());
        try
        {
            var store = new WaveProfileStore(path); var taste = new WaveTaste();
            var missing = Track(1); var available = Track(2);
            taste.Import(missing, true, true); taste.MarkUnavailable(missing, DateTimeOffset.UtcNow.AddDays(30));
            store.Save(42, taste); taste = store.Load(42);
            taste.Import(missing with { Title = "Stale recommendation returned" }, true, true);
            taste.ResetBlocks();
            Assert.True(taste.IsUnavailable(missing)); Assert.False(taste.IsBlocked(missing));
            Assert.True(taste.Snapshot().Tracks.Single(item => item.Track.Id == 1).Liked);
            Assert.DoesNotContain(taste.Seeds([missing, available]), track => track.Id == 1);
            Assert.DoesNotContain(taste.Rank([missing, available], [], 10, DateTimeOffset.UtcNow), track => track.Id == 1);
            Assert.All(taste.Snapshot().Tracks, item => { Assert.Equal(0, item.EarlySkips); Assert.Equal(0, item.GoodPlays); });
            taste.RestoreAvailability(missing);
            Assert.False(taste.IsUnavailable(missing));
            Assert.Contains(taste.Rank([missing, available], [], 10, DateTimeOffset.UtcNow), track => track.Id == 1);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void AvailabilityCooldownExpiresAndDoesNotBlockOtherUploadsOfTheSameWork()
    {
        var taste = new WaveTaste(); var now = DateTimeOffset.UtcNow;
        var missing = Track(1, title: "Song"); var replacement = Track(2, title: "Song");
        taste.MarkUnavailable(missing, now.AddHours(1));
        Assert.Equal(2, Assert.Single(taste.Rank([missing, replacement], [], 10, now)).Id);
        Assert.Equal(1, Assert.Single(taste.Rank([missing], [], 10, now.AddHours(2))).Id);
    }
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
    public void LearnedAudioPreferenceSelectsLiveQueueAndSeedsInsteadOfLegacyRelatedWeights()
    {
        var taste = new WaveTaste(); var seed = Track(1, "A"); var other = Track(3, "B");
        taste.Import(seed, true, true);
        taste.Relate(seed, [Track(2, "Candidate A")]); taste.Relate(other, [Track(4, "Candidate B")]);
        taste.SetMode(WaveMode.Discoveries);
        var model = Pookie.Recommendations.Learning.RankerState.Empty();
        var positive = new float[200]; positive[0] = 1;
        var negative = new float[200]; negative[1] = 1;
        model.Weights[4] = 5; model.Weights[5] = -5;
        model = model with { Samples = 20, AudioModel = Pookie.Recommendations.Audio.MusicnnEncoder.ModelId,
            PositiveAudio = [positive], NegativeAudio = negative };
        var vectors = new Dictionary<long, Pookie.Recommendations.Audio.AudioVector> {
            [2] = new(model.AudioModel!, negative), [4] = new(model.AudioModel!, positive) };
        taste.Learning = new(model, vectors, [], []);
        Assert.Equal(4, taste.Rank([Track(2, "Candidate A"), Track(4, "Candidate B")], [], 1, Now, new Random(1))[0].Id);
        Assert.Equal(4, taste.Seeds([Track(2, "Candidate A"), Track(4, "Candidate B")], 1)[0].Id);
        taste.Learning = taste.Learning with { Model = model with { PositiveAudio = [negative], NegativeAudio = positive } };
        Assert.Equal(2, taste.Rank([Track(2, "Candidate A"), Track(4, "Candidate B")], [], 1, Now, new Random(1))[0].Id);
        taste.Block(Track(2, "Candidate A"));
        Assert.Equal(4, taste.Rank([Track(2, "Candidate A"), Track(4, "Candidate B")], [], 1, Now, new Random(1))[0].Id);
    }
    [Fact]
    public void RemovingRemoteLikeDoesNotLeaveItInFavoriteMode()
    {
        var taste = new WaveTaste(); taste.Import(Track(1), true, true); taste.SetMode(WaveMode.Favorites);
        taste.SyncLikes(new HashSet<long>()); Assert.Empty(taste.Rank([Track(1)], [], 10, Now));
    }

    [Fact]
    public void ColdStartUsesRealLikesAndTheirRelatedTracksWithoutFabricatingLearning()
    {
        var taste = new WaveTaste();
        var favorite = Track(1, "Favorite artist"); var merelyHeard = Track(2, "Other artist");
        var fitting = Track(3, "New artist"); var unrelated = Track(4, "Another new artist");
        taste.Import(favorite, true, true); taste.Import(merelyHeard, true);
        taste.Relate(favorite, [fitting]); taste.Relate(merelyHeard, [unrelated]);
        taste.SetMode(WaveMode.Discoveries);
        Assert.Equal(favorite.Id, taste.Seeds([merelyHeard, favorite], 1).Single().Id);
        Assert.Equal(fitting.Id, taste.Rank([unrelated, fitting], [], 1, Now, new Random(1)).Single().Id);
        Assert.Null(taste.Learning);
        Assert.All(taste.Snapshot().Tracks, item => Assert.Equal(0, item.Plays));

        taste.SyncLikes(new HashSet<long>());
        Assert.Equal(merelyHeard.Id, taste.Seeds([merelyHeard, favorite], 1).Single().Id);
    }

    [Fact]
    public void MaturePersonalModelOverridesTheColdStartPrior()
    {
        var taste = new WaveTaste(); var favorite = Track(1, "Previously liked"); var learned = Track(2, "New preference");
        taste.Import(favorite, true, true);
        taste.Import(learned, true);
        var model = Pookie.Recommendations.Learning.RankerState.Empty() with { Samples = 24 };
        model.Weights[7] = -5;
        taste.Learning = new(model, [], [], []);
        Assert.Equal(learned.Id, taste.Seeds([favorite, learned], 1).Single().Id);
        Assert.Equal(learned.Id, taste.Rank([favorite, learned], [], 1, Now, new Random(1)).Single().Id);
    }

    [Fact]
    public void BaseDiscoveriesKeepUnknownAudioEligibleAndIgnoreCachedMoodContexts()
    {
        var taste = new WaveTaste(); taste.SetMode(WaveMode.Discoveries);
        var tracks = Enumerable.Range(1, 20).Select(id => Track(id, "Artist " + id)).ToArray();
        var values = new float[200]; values[0] = 1;
        var contextual = Pookie.Recommendations.Learning.RankerState.Empty() with { Samples = 100 };
        contextual.Weights[0] = 20;
        var model = Pookie.Recommendations.Learning.RankerState.Empty() with { Samples = 24, Contexts = new() {
            [Pookie.Recommendations.WaveMood.Aggressive] = contextual } };
        taste.Learning = new(model, new() { [1] = new(Pookie.Recommendations.Audio.MusicnnEncoder.ModelId, values,
            new(1, 0, 1, 0, 0, 0, Pookie.Recommendations.AudioMood.AnalysisVersion)) }, [], []);
        var before = taste.Rank(tracks, [], 20, Now, new Random(1)).Select(track => track.Id).ToArray();
        Assert.Equal(20, before.Length);
        taste.Learning = taste.Learning with { Vectors = new() { [1] = new(Pookie.Recommendations.Audio.MusicnnEncoder.ModelId, values,
            new(0, 1, 0, 1, 1, 1, Pookie.Recommendations.AudioMood.AnalysisVersion)) } };
        Assert.Equal(before, taste.Rank(tracks, [], 20, Now, new Random(1)).Select(track => track.Id));
        taste.Block(tracks[1]);
        Assert.DoesNotContain(taste.Rank(tracks, [], 20, Now), track => track.Id == tracks[1].Id);
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
        var manual = queue.Enqueue(Track(50)); queue.ReplaceRecommendations(queue.SourceVersion, [Track(8)], rebuild: true);
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
        Assert.True(queue.ReplaceRecommendations(queue.SourceVersion, [Track(4), Track(5)], rebuild: true));
        Assert.Equal(snapshot.Current, queue.Current); Assert.Equal(snapshot.Forward, queue.Snapshot.Forward);
        Assert.Equal(manual, Assert.Single(queue.Snapshot.ManualUpcoming));
        Assert.Equal(new long[] { 4, 5 }, queue.Snapshot.ContextUpcoming.Select(entry => entry.Track.Id));
        var version = queue.SourceVersion; queue.StartContext(new("other", "Other", ""), [Track(9)], 0);
        Assert.False(queue.ReplaceRecommendations(version, [Track(6)]));
        Assert.False(queue.ReplaceRecommendations(queue.SourceVersion, [Track(6)]));
        Assert.Equal(9, queue.Current!.Track.Id);
    }
}
