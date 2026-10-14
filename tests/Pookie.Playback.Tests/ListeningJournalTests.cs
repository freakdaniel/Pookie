using Pookie.App.Home;
using Pookie.Recommendations;
using Pookie.Recommendations.Storage;
using Pookie.SoundCloud;
using Xunit;
using WaveMode = Pookie.App.Home.WaveMode;
using WaveTaste = Pookie.App.Home.WaveTaste;
using WaveTrack = Pookie.App.Home.WaveTrack;

namespace Pookie.Playback.Tests;

public sealed class ListeningJournalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pookie-journal-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T10:00:00Z");
    private string Database => Path.Combine(directory, "events.sqlite3");
    public ListeningJournalTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private static SoundCloudTrack Track(long id = 1) => new() { Id = id, Title = "Song", Duration = 180000,
        User = new() { Id = 8, Username = "Uploader" }, PublisherMetadata = new() { Artist = "Artist" },
        TrackAuthorization = "SECRET_STREAM_TOKEN" };
    private static WaveEvent Event(long account = 42, long track = 1) => new(Guid.NewGuid(), account, Now, WaveEventKind.Like, WaveTrack.From(Track(track)));

    [Fact]
    public void ObservationCountsOnlyListeningAndFinishesOnce()
    {
        var listening = new WaveListening(); var taste = new WaveTaste();
        listening.Begin(Track(), true, TimeSpan.Zero, started: Now);
        listening.Poll(0, true, TimeSpan.Zero);
        listening.Poll(1, true, TimeSpan.FromSeconds(1));
        listening.Poll(90, true, TimeSpan.FromSeconds(2));
        listening.Poll(91, false, TimeSpan.FromSeconds(3));
        listening.Poll(91, true, TimeSpan.FromSeconds(4));
        listening.Poll(92, true, TimeSpan.FromSeconds(5));
        var value = listening.Finish(taste, ListeningEndReason.Skip, Now.AddSeconds(5))!;
        Assert.Equal(2, value.ListenedSeconds); Assert.True(value.EarlySkip);
        Assert.Equal(2d / 180, value.ListenedFraction, 8);
        Assert.Equal(ListeningSource.Wave, value.Source); Assert.Equal("learned-base-v2", value.AlgorithmVersion);
        Assert.Equal(Pookie.Recommendations.WaveMood.Auto, value.Mood);
        Assert.Null(listening.Finish(taste, ListeningEndReason.Skip, Now.AddSeconds(6)));
        Assert.Equal(1, Assert.Single(taste.Snapshot().Tracks).Plays);
    }

    [Theory]
    [InlineData(ListeningEndReason.Shutdown)]
    [InlineData(ListeningEndReason.AccountChanged)]
    [InlineData(ListeningEndReason.PlaybackError)]
    [InlineData(ListeningEndReason.Stopped)]
    [InlineData(ListeningEndReason.NaturalEnd)]
    [InlineData(ListeningEndReason.Rejected)]
    public void InterruptionsAreNotImplicitDislikes(ListeningEndReason reason)
    {
        var listening = new WaveListening(); var taste = new WaveTaste();
        listening.Begin(Track(), true, TimeSpan.Zero, started: Now);
        var value = listening.Finish(taste, reason, Now.AddSeconds(1))!;
        Assert.False(value.EarlySkip); Assert.Equal(reason, value.EndReason);
        Assert.Equal(0, Assert.Single(taste.Snapshot().Tracks).EarlySkips);
    }

    [Fact]
    public void SeparatePlaysHaveSeparateIdentitiesAndManualOriginSurvives()
    {
        var listening = new WaveListening(); var taste = new WaveTaste();
        listening.Begin(Track(), false, TimeSpan.Zero, ListeningSource.Manual, WaveMode.Discoveries, Now, PlaybackSelection.TrackSelection);
        var first = listening.Finish(taste, ListeningEndReason.Skip, Now.AddSeconds(1))!;
        listening.Begin(Track(), false, TimeSpan.FromSeconds(2), started: Now.AddSeconds(2), selection: PlaybackSelection.QueueAdvance);
        var second = listening.Finish(taste, ListeningEndReason.NaturalEnd, Now.AddSeconds(3))!;
        Assert.NotEqual(first.PlaybackId, second.PlaybackId); Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal(ListeningSource.Manual, first.Source); Assert.False(first.EarlySkip);
        Assert.Equal(Pookie.Recommendations.WaveMode.Discoveries, first.Mode);
        Assert.Equal(PlaybackSelection.TrackSelection, first.Selection);
        Assert.Equal(PlaybackSelection.QueueAdvance, second.Selection);
    }

    [Fact]
    public async Task JournalPersistsBeforeAcknowledgingAndSeparatesAccountsAndRetries()
    {
        var listening = new WaveListening(); var taste = new WaveTaste();
        listening.Begin(Track(), true, TimeSpan.Zero, started: Now);
        var observation = listening.Finish(taste, ListeningEndReason.Shutdown, Now.AddSeconds(1))!;
        var item = WaveEvent.FromListening(42, observation);
        await using (var journal = new ListeningJournal(Database, capacity: 1))
        {
            await journal.AppendAsync(item);
            using (var reader = new ListeningEventStore(Database)) Assert.Single(reader.Read(42));
            await Task.WhenAll(journal.AppendAsync(item), journal.AppendAsync(Event(43)), journal.AppendAsync(Event(42, 2)));
        }
        using var reopened = new ListeningEventStore(Database);
        var rows = reopened.Read(42);
        Assert.Equal(2, rows.Length); Assert.Single(reopened.Read(43)); Assert.Empty(reopened.Read(44));
        Assert.Equal(observation, rows[0].Event.Listening);
        Assert.Single(reopened.Read(42, rows[0].Sequence));
        Assert.Empty(reopened.Read(42, rows[^1].Sequence));
        reopened.Dispose();
        Assert.DoesNotContain("SECRET_STREAM_TOKEN", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Database)));
    }

    [Fact]
    public async Task ShutdownDrainsEveryAcceptedWrite()
    {
        var journal = new ListeningJournal(Database);
        var pending = Enumerable.Range(1, 80).Select(id => journal.AppendAsync(Event(track: id))).ToArray();
        await journal.DisposeAsync();
        await Task.WhenAll(pending);
        using var store = new ListeningEventStore(Database);
        Assert.Equal(80, store.Read(42).Length);
    }

    [Fact]
    public async Task InvalidEventDoesNotPoisonFollowingWrites()
    {
        await using var journal = new ListeningJournal(Database);
        await Assert.ThrowsAsync<ArgumentException>(() => journal.AppendAsync(Event() with { AccountId = 0 }));
        await journal.AppendAsync(Event());
    }

    [Fact]
    public void InvalidBatchCannotPartiallyCommit()
    {
        using var store = new ListeningEventStore(Database);
        Assert.Throws<ArgumentException>(() => store.AppendBatch([Event(), Event() with { AccountId = 0 }]));
        Assert.Empty(store.Read(42));
    }

    [Fact]
    public void RetentionBoundsListeningButKeepsExplicitActionsAndOtherAccounts()
    {
        WaveEvent Play(long account)
        {
            var observation = new ListeningObservation(Guid.NewGuid(), Guid.NewGuid(), WaveTrack.From(Track()),
                Now, Now.AddSeconds(10), 10, ListeningSource.Wave, ListeningEndReason.Skip, Pookie.Recommendations.WaveMode.Balance);
            return WaveEvent.FromListening(account, observation);
        }
        using (var store = new ListeningEventStore(Database, retainedListeningEvents: 2))
        {
            store.AppendBatch([Event(), new(Guid.NewGuid(), 42, Now, WaveEventKind.ResetBlocks), Play(43)]);
            store.AppendBatch(Enumerable.Range(1, 260).Select(_ => Play(42)).ToArray());
            Assert.Equal(2, store.Read(42).Count(row => row.Event.Kind == WaveEventKind.Listening));
            // A short next session must also be pruned on close.
            store.AppendBatch([Play(42)]);
        }
        using var reopened = new ListeningEventStore(Database);
        Assert.Equal(4, reopened.Read(42).Length); Assert.Single(reopened.Read(43));
        Assert.Equal(2, reopened.Read(42).Count(row => row.Event.Kind == WaveEventKind.Listening));
    }

    [Fact]
    public async Task StorageFailureReachesProducersAndShutdown()
    {
        var journal = new ListeningJournal(Path.Combine(directory, "missing", "events.sqlite3"));
        await Assert.ThrowsAnyAsync<Exception>(() => journal.AppendAsync(Event()));
        await Assert.ThrowsAnyAsync<Exception>(async () => await journal.DisposeAsync());
    }

    [Fact]
    public void ExistingJsonProfileLoadsWithoutInventingObservations()
    {
        File.WriteAllText(Path.Combine(directory, "42.json"), """
            {"mode":2,"tracks":[{"track":{"id":9,"title":"Old song","artist":"Artist","uploader":8,"genre":"Rock","duration":180000},
            "familiar":true,"liked":true,"blocked":false,"good_plays":4,"early_skips":1,"listened_seconds":300,"plays":5}]}
            """);
        var profile = new WaveProfileStore(directory).Load(42);
        Assert.Equal(WaveMode.Discoveries, profile.Mode);
        var track = Assert.Single(profile.Snapshot().Tracks);
        Assert.True(track.Liked); Assert.Equal(5, track.Plays); Assert.Equal(4, track.GoodPlays);
        using var events = new ListeningEventStore(Database);
        Assert.Empty(events.Read(42));
    }
}
