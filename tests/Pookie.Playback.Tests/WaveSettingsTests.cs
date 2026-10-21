using Pookie.App.Home;
using Pookie.Recommendations;
using Pookie.Recommendations.Audio;
using Pookie.Recommendations.Storage;
using Pookie.SoundCloud;
using Xunit;
using WaveTaste = Pookie.App.Home.WaveTaste;
using WaveTrack = Pookie.Recommendations.WaveTrack;

namespace Pookie.Playback.Tests;

public sealed class WaveSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pookie-wave-settings-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "events.sqlite3");
    private static WaveTrack Track(long id) => new(id, "Song " + id, "Artist", 8, "Rock", 180000, null, null, null);
    private static WaveEvent Preference(long account, long track, WaveEventKind kind, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), account, at ?? DateTimeOffset.UtcNow, kind, Track(track));
    private static WaveEvent Play(long account, long track = 1, DateTimeOffset? at = null)
    {
        var start = at ?? DateTimeOffset.UtcNow;
        return WaveEvent.FromListening(account, new(Guid.NewGuid(), Guid.NewGuid(), Track(track), start, start.AddSeconds(120),
            120, ListeningSource.Wave, ListeningEndReason.NaturalEnd, Pookie.Recommendations.WaveMode.Balance));
    }
    public WaveSettingsTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void OptOutKeepsHistoryClockButDiscardsTheWholeCurrentPlayback()
    {
        var track = new SoundCloudTrack { Id = 1, Title = "Song", Duration = 180000 };
        var listening = new WaveListening(); var taste = new WaveTaste();
        listening.Begin(track, true, TimeSpan.Zero);
        listening.Poll(0, true, TimeSpan.Zero); listening.Poll(2, true, TimeSpan.FromSeconds(2));
        listening.SuppressLearning();
        Assert.Equal(2, listening.Seconds);
        Assert.Same(track, listening.Track);
        Assert.Null(listening.Finish(taste, ListeningEndReason.Skip, DateTimeOffset.UtcNow, learn: true));
        Assert.Empty(taste.Snapshot().Tracks);
        listening.Begin(track, true, TimeSpan.FromSeconds(3));
        Assert.NotNull(listening.Finish(taste, ListeningEndReason.NaturalEnd, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ClearingHistoryLeavesModelVectorsManualStateAndOtherAccounts()
    {
        using var store = new ListeningEventStore(Database);
        store.AppendBatch([Preference(42, 2, WaveEventKind.Like), Preference(42, 3, WaveEventKind.Block), Play(42), Play(43)]);
        var vector = new AudioVector(MusicnnEncoder.ModelId, Enumerable.Repeat(.1f, 200).ToArray());
        store.PutVector(42, Track(1), vector, DateTimeOffset.UtcNow);
        var before = store.Snapshot(42, [Track(1)]);
        store.ClearListeningHistory(42);
        Assert.DoesNotContain(store.Read(42), item => item.Event.Kind == WaveEventKind.Listening);
        Assert.Equal(2, store.Read(42).Length);
        Assert.Single(store.Read(43));
        Assert.Equal(before.Model.Samples, store.Snapshot(42, [Track(1)]).Model.Samples);
        Assert.NotNull(store.GetVector(42, 1));
        Assert.True(store.Preference(42, 2).Liked); Assert.True(store.Preference(42, 3).Blocked);
    }

    [Fact]
    public async Task ResetBarrierClearsAcceptedTrainingAndPreservesExplicitStateWithoutReplay()
    {
        using (var store = new ListeningEventStore(Database))
        {
            var vector = new AudioVector(MusicnnEncoder.ModelId, Enumerable.Repeat(.1f, 200).ToArray());
            store.PutVector(42, Track(1), vector, DateTimeOffset.UtcNow);
            store.PutVector(43, Track(1), vector, DateTimeOffset.UtcNow);
        }
        await using (var journal = new ListeningJournal(Database, capacity: 1))
        {
            var writes = new[] { journal.AppendAsync(Preference(42, 2, WaveEventKind.Like)),
                journal.AppendAsync(Preference(42, 3, WaveEventKind.Block)), journal.AppendAsync(Play(42)), journal.AppendAsync(Play(43)) };
            var reset = journal.ResetLearningAsync(42);
            await Task.WhenAll(writes);
            await reset;
            var snapshot = await journal.SnapshotAsync(42, [Track(1)]);
            Assert.Equal(0, snapshot.Model.Samples); Assert.Equal(0, snapshot.Model.Sequence);
        }
        using var reopened = new ListeningEventStore(Database);
        Assert.Empty(reopened.Read(42)); Assert.Single(reopened.Read(43));
        Assert.True(reopened.Preference(42, 2).Liked); Assert.True(reopened.Preference(42, 3).Blocked);
        Assert.Null(reopened.GetVector(42, 1)); Assert.NotNull(reopened.GetVector(43, 1));
        Assert.Equal(0, reopened.Snapshot(42, [Track(1)]).Model.Samples);
        Assert.True(reopened.Snapshot(43, [Track(1)]).Model.Samples > 0);
    }

    [Fact]
    public async Task RetentionChangePrunesExistingRawEventsWithoutErasingTrainingOrPreferences()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-60);
        await using var journal = new ListeningJournal(Database, policy: new(RawHistoryDays: 90));
        await journal.AppendAsync(Play(42, at: old));
        await journal.AppendAsync(Preference(42, 2, WaveEventKind.Like, old));
        var before = await journal.SnapshotAsync(42, [Track(1)]);
        await journal.SetRawHistoryDaysAsync(30);
        using var store = new ListeningEventStore(Database);
        Assert.Empty(store.Read(42)); Assert.True(store.Preference(42, 2).Liked);
        Assert.Equal(before.Model.Samples, store.Snapshot(42, [Track(1)]).Model.Samples);
    }

    [Fact]
    public void JsonResetKeepsOnlyManualStateAndModeForTheSelectedAccount()
    {
        var store = new WaveProfileStore(directory);
        var taste = new WaveTaste(new(Pookie.Recommendations.WaveMode.Discoveries, [
            new(Track(1), true, true, GoodPlays: 4, EarlySkips: 1, ListenedSeconds: 300, Plays: 5),
            new(Track(2), true, Blocked: true, GoodPlays: 2, Plays: 2),
            new(Track(3), true, GoodPlays: 8, Plays: 8)]));
        store.Save(42, taste); store.Save(43, taste);
        store.ResetLearning(42);
        var reset = store.Load(42).Snapshot();
        Assert.Equal(Pookie.Recommendations.WaveMode.Discoveries, reset.Mode);
        Assert.Equal(2, reset.Tracks.Length);
        Assert.All(reset.Tracks, track => { Assert.Equal(0, track.Plays); Assert.Equal(0, track.GoodPlays); Assert.Equal(0, track.ListenedSeconds); });
        Assert.True(reset.Tracks.Single(track => track.Track.Id == 1).Liked);
        Assert.True(reset.Tracks.Single(track => track.Track.Id == 2).Blocked);
        Assert.Equal(3, store.Load(43).Snapshot().Tracks.Length);
    }

    [Fact]
    public async Task CancelledObservationCannotRepopulateHistoryAfterReset()
    {
        await using var journal = new ListeningJournal(Database);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await journal.ResetLearningAsync(42);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.AppendAsync(Play(42), cancellation.Token));
        await journal.AppendAsync(Preference(42, 2, WaveEventKind.Like));
        using var store = new ListeningEventStore(Database);
        Assert.Single(store.Read(42)); Assert.True(store.Preference(42, 2).Liked);
    }
}
