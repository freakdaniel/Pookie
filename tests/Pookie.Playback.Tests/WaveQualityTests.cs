using Pookie.Recommendations;
using Pookie.Recommendations.Storage;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class WaveQualityTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("pookie-quality-").FullName;
    private string Database => Path.Combine(directory, "events.sqlite3");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddMinutes(-1);
    private static WaveEvent Play(long id, ListeningEndReason reason, double seconds, bool? familiar = false,
        string version = "learned-base-v2", long account = 42, PlaybackSelection selection = PlaybackSelection.QueueAdvance)
    {
        var track = new WaveTrack(id, "Fixture", "Artist " + id, id, "", 180000, null, null, null);
        return WaveEvent.FromListening(account, new(Guid.NewGuid(), Guid.NewGuid(), track, Now.AddSeconds(-seconds), Now,
            seconds, ListeningSource.Wave, reason, WaveMode.Discoveries, version, selection, FamiliarAtSelection: familiar));
    }
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void ReportSeparatesNewMusicVersionsAndAccountsAndDoesNotJudgeInterruptedPlays()
    {
        using var store = new ListeningEventStore(Database);
        var ended = Play(1, ListeningEndReason.NaturalEnd, 150);
        var skipped = Play(2, ListeningEndReason.Skip, 5);
        var interrupted = Play(3, ListeningEndReason.Shutdown, 2);
        var like = new WaveEvent(Guid.NewGuid(), 42, Now.AddSeconds(-1), WaveEventKind.Like, ended.Track,
            PlaybackId: ended.Listening!.PlaybackId);
        store.AppendBatch([ended, skipped, interrupted, like, Play(4, ListeningEndReason.Skip, 3, true),
            Play(5, ListeningEndReason.NaturalEnd, 150, null, "learned-base-v1"),
            Play(6, ListeningEndReason.Skip, 2, account: 43),
            Play(7, ListeningEndReason.Skip, 2, selection: PlaybackSelection.TrackSelection)]);
        store.AppendBatch([ended, like]); // Receipt deduplication must also protect reported counts.
        var before = store.Statistics(); var samples = store.Snapshot(42, []).Model.Samples;
        var rows = WaveQualityReportReader.Read(Database, Now.AddHours(-1), Now.AddSeconds(1));
        var fresh = Assert.Single(rows, row => row.AccountId == 42 && row.AlgorithmVersion == "learned-base-v2" && row.FamiliarAtSelection == false);
        Assert.Equal(3, fresh.Plays); Assert.Equal(2, fresh.Judged); Assert.Equal(1, fresh.Interrupted);
        Assert.Equal(1, fresh.EarlySkips); Assert.Equal(1, fresh.LongListens); Assert.Equal(1, fresh.NaturalEnds);
        Assert.Equal(1, fresh.Likes); Assert.Equal(.5, fresh.EarlySkipRate); Assert.Equal(.5, fresh.LikeRate);
        Assert.Equal(3, fresh.DistinctTracks); Assert.Equal(3, fresh.DistinctArtists);
        Assert.Null(Assert.Single(rows, row => row.AlgorithmVersion == "learned-base-v1").Likes);
        Assert.Equal(1, Assert.Single(rows, row => row.AccountId == 43).Plays);
        Assert.Equal(before.Events, store.Statistics().Events); Assert.Equal(samples, store.Snapshot(42, []).Model.Samples);
        Assert.Contains("early_skip_rate", WaveQualityReportReader.ToJson(rows));
    }

    [Fact]
    public void UndoingALikeDoesNotCountItAsASuccessOrAffectAnotherPlaybackOfTheSameTrack()
    {
        using var store = new ListeningEventStore(Database);
        var first = Play(1, ListeningEndReason.Skip, 5);
        var second = Play(1, ListeningEndReason.Skip, 6);
        var like = new WaveEvent(Guid.NewGuid(), 42, Now.AddSeconds(-3), WaveEventKind.Like, first.Track,
            PlaybackId: first.Listening!.PlaybackId);
        store.AppendBatch([first, second, like, like with { EventId = Guid.NewGuid(), Kind = WaveEventKind.Unlike,
            OccurredAt = Now.AddSeconds(-2) }]);
        var row = Assert.Single(WaveQualityReportReader.Read(Database, Now.AddHours(-1), Now.AddSeconds(1)));
        Assert.Equal(2, row.Plays); Assert.Equal(1, row.DistinctTracks); Assert.Equal(0, row.Likes);
        Assert.Equal(2, row.EarlySkips);
    }

    [Fact]
    public void MissingJournalIsNotCreatedByAReport()
    {
        Assert.Empty(WaveQualityReportReader.Read(Database, Now.AddDays(-7), Now));
        Assert.False(File.Exists(Database));
    }

    [Fact]
    public void HistoricalReportCannotUseFeedbackFromAfterItsCutoff()
    {
        using var store = new ListeningEventStore(Database);
        var play = Play(1, ListeningEndReason.NaturalEnd, 150);
        store.AppendBatch([play, new(Guid.NewGuid(), 42, Now.AddSeconds(5), WaveEventKind.Like, play.Track,
            PlaybackId: play.Listening!.PlaybackId)]);
        Assert.Equal(0, Assert.Single(WaveQualityReportReader.Read(Database, Now.AddHours(-1), Now.AddSeconds(1))).Likes);
        Assert.Equal(1, Assert.Single(WaveQualityReportReader.Read(Database, Now.AddHours(-1), Now.AddSeconds(6))).Likes);
    }
}
