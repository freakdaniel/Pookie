using Pookie.Recommendations;
using Pookie.Recommendations.Audio;
using Pookie.Recommendations.Learning;
using Pookie.Recommendations.Storage;
using Xunit;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Pookie.Playback.Tests;

public sealed class WaveLearningTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pookie-learning-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "events.sqlite3");
    private readonly ITestOutputHelper output;
    public WaveLearningTests(ITestOutputHelper output) { this.output = output; Directory.CreateDirectory(directory); }
    public void Dispose() => Directory.Delete(directory, true);
    private static WaveTrack Track(long id = 1) => new(id, "Song", "Artist", 1, "Rock", 180000, null, null, null);
    private static WaveEvent Like(long account = 42, long id = 1) => new(Guid.NewGuid(), account, DateTimeOffset.UtcNow, WaveEventKind.Like, Track(id));
    private static WaveEvent Play(DateTimeOffset at, long id = 1, double seconds = 150)
    {
        var value = new ListeningObservation(Guid.NewGuid(), Guid.NewGuid(), Track(id), at.AddSeconds(-seconds), at, seconds,
            ListeningSource.Wave, ListeningEndReason.NaturalEnd, WaveMode.Balance);
        return WaveEvent.FromListening(42, value);
    }
    private static float[] Unit(int at)
    {
        var values = new float[200]; values[at] = 1; return values;
    }

    [Fact]
    public void LearnerPredictsBeforeFeedbackAndDoesNotTreatUnlikeAsDislike()
    {
        var learner = new OnlineRanker(); var track = Track();
        var before = learner.Evaluate(track, null).Prediction;
        learner.Observe(1, Like(), null);
        Assert.True(learner.Evaluate(track, null).Prediction > before);
        learner.Observe(2, Like() with { Kind = WaveEventKind.Unlike }, null);
        Assert.Equal(1, learner.Snapshot().Samples);
        learner.Observe(2, Like(), null);
        Assert.Equal(1, learner.Snapshot().Samples);
    }

    [Fact]
    public void CapturedPredictionIsUsedAndRepeatedListeningHasLimitedInfluence()
    {
        var learner = new OnlineRanker();
        var first = Play(DateTimeOffset.UtcNow);
        var features = learner.Evaluate(Track(), null);
        first = first with { Listening = first.Listening! with { Features = features } };
        learner.Observe(1, first, null);
        Assert.Equal(.25, learner.Snapshot().PredictionError, 6);
        for (var sequence = 2; sequence <= 10; sequence++) learner.Observe(sequence, Play(DateTimeOffset.UtcNow), null);
        Assert.Equal(3, learner.Snapshot().Samples); Assert.Equal(10, learner.Snapshot().Sequence);
    }

    [Fact]
    public void CleaningOldObservationsPreservesTheLearnedCheckpointAcrossRestart()
    {
        var item = Play(DateTimeOffset.UtcNow.AddDays(-100));
        RankerState before;
        using (var store = new ListeningEventStore(Database))
        {
            store.AppendBatch([item]); before = store.Snapshot(42, []).Model;
            store.Maintain(); Assert.Empty(store.Read(42)); Assert.Equal(1, before.Samples);
        }
        using var reopened = new ListeningEventStore(Database);
        var after = reopened.Snapshot(42, []).Model;
        Assert.Equal(before.Sequence, after.Sequence); Assert.Equal(before.Weights, after.Weights); Assert.Equal(before.Samples, after.Samples);
    }

    [Fact]
    public void PreferenceCompactionPreservesCurrentStateAndDeduplicatesRecentRetries()
    {
        using var store = new ListeningEventStore(Database, policy: new(RetainedPreferenceEvents: 1));
        var like = Like();
        var blocked = Like() with { Kind = WaveEventKind.Block, OccurredAt = like.OccurredAt.AddSeconds(1) };
        var reset = new WaveEvent(Guid.NewGuid(), 42, like.OccurredAt.AddSeconds(2), WaveEventKind.ResetBlocks);
        store.AppendBatch([like, blocked, reset]); store.Maintain();
        Assert.Single(store.Read(42)); Assert.Equal((false, false), store.Preference(42, 1));
        var checkpoint = store.Snapshot(42, []).Model;
        store.AppendBatch([like, blocked]);
        Assert.Equal(checkpoint.Sequence, store.Snapshot(42, []).Model.Sequence);
        Assert.Equal((false, false), store.Preference(42, 1));
    }

    [Fact]
    public void VectorSearchUsesAudioValuesAndRespectsAccountsAndBlocks()
    {
        using var store = new ListeningEventStore(Database);
        store.PutVector(42, Track(1), new(MusicnnEncoder.ModelId, Unit(0)), DateTimeOffset.UtcNow);
        store.PutVector(42, Track(2), new(MusicnnEncoder.ModelId, Unit(1)), DateTimeOffset.UtcNow);
        store.PutVector(43, Track(3), new(MusicnnEncoder.ModelId, Unit(0)), DateTimeOffset.UtcNow);
        var result = store.SearchAudio(42, Unit(0));
        Assert.Equal(new long[] { 1, 2 }, result.Select(match => match.Track.Id)); Assert.Equal(1, result[0].Similarity, 6);
        store.AppendBatch([Like() with { Kind = WaveEventKind.Block }]);
        Assert.Equal(2, Assert.Single(store.SearchAudio(42, Unit(0))).Track.Id);
        Assert.Throws<ArgumentException>(() => store.PutVector(42, Track(), new("another-model", Unit(0)), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void VectorCacheIsBoundedAndOldVectorsAreRemoved()
    {
        using var store = new ListeningEventStore(Database, policy: new(RetainedVectors: 2));
        store.PutVector(42, Track(1), new(MusicnnEncoder.ModelId, Unit(0)), DateTimeOffset.UtcNow.AddDays(-181));
        store.PutVector(42, Track(2), new(MusicnnEncoder.ModelId, Unit(0)), DateTimeOffset.UtcNow);
        store.PutVector(43, Track(3), new(MusicnnEncoder.ModelId, Unit(0)), DateTimeOffset.UtcNow);
        store.Maintain(); Assert.Null(store.GetVector(42, 1)); Assert.Equal(2, store.Statistics().Vectors);
    }

    [Fact]
    public void IncrementalVacuumReclaimsDiskSpaceAfterPruning()
    {
        using var store = new ListeningEventStore(Database, retainedListeningEvents: 1);
        var events = Enumerable.Range(1, 160).Select(id => {
            var item = Play(DateTimeOffset.UtcNow, id);
            var track = Track(id) with { Title = new string('x', 3000) };
            return item with { Track = track, Listening = item.Listening! with { Track = track } };
        }).ToArray();
        store.AppendBatch(events);
        var before = store.Statistics(); store.Maintain(); var after = store.Statistics();
        Assert.Equal(2, after.VacuumMode); Assert.Equal(1, after.Events); Assert.True(after.DatabaseBytes < before.DatabaseBytes);
        Assert.True(after.PhysicalFileBytes < before.PhysicalFileBytes);
        Assert.Equal(160, store.Snapshot(42, []).Model.Samples);
    }

    [Fact]
    public void DatabaseBudgetPrunesProcessedRawDataWithoutResettingTheModel()
    {
        using var store = new ListeningEventStore(Database, policy: new(MaximumDatabaseBytes: 4 * 1024 * 1024));
        for (var batch = 0; batch < 70; batch++)
        {
            var events = Enumerable.Range(0, 16).Select(_ => {
                var item = Play(DateTimeOffset.UtcNow); var track = Track() with { Title = new string('x', 2400) };
                return item with { Track = track, Listening = item.Listening! with { Track = track } };
            }).ToArray();
            store.AppendBatch(events);
        }
        store.Maintain();
        var stats = store.Statistics();
        Assert.InRange(stats.DatabaseBytes, 1, 4 * 1024 * 1024); Assert.True(stats.Events < 70 * 16);
        Assert.Equal(3, store.Snapshot(42, []).Model.Samples); Assert.InRange(stats.WalBytes, 0, 16 * 1024 * 1024);
    }

    [Fact]
    public void ExplicitPreferencesAreNotTruncatedByTheLegacyProfileLimit()
    {
        var taste = new WaveTaste();
        for (var id = 1; id <= 1600; id++) taste.Block(Track(id));
        Assert.Equal(1600, taste.Snapshot().Tracks.Length);
        Assert.Empty(new WaveTaste(taste.Snapshot()).Rank([Track(1600)], [], 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void VersionOneJournalIsLearnedBeforeMigrationPrunesItsOldRows()
    {
        var item = Play(DateTimeOffset.UtcNow.AddDays(-100));
        using (var old = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            old.Open(); using var command = old.CreateCommand();
            command.CommandText = """
                CREATE TABLE events(sequence INTEGER PRIMARY KEY AUTOINCREMENT,account_id INTEGER,event_id TEXT,kind INTEGER,occurred_at TEXT,payload TEXT,UNIQUE(account_id,event_id));
                PRAGMA user_version=1;
                INSERT INTO events(account_id,event_id,kind,occurred_at,payload) VALUES(42,$id,0,$at,$payload);
                """;
            command.Parameters.AddWithValue("$id", item.EventId.ToString("N")); command.Parameters.AddWithValue("$at", item.OccurredAt.ToString("O"));
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
            command.ExecuteNonQuery();
        }
        using var migrated = new ListeningEventStore(Database);
        Assert.Equal(1, migrated.Snapshot(42, []).Model.Samples); Assert.Equal(2, migrated.Statistics().VacuumMode);
        migrated.Maintain(); Assert.Empty(migrated.Read(42)); Assert.Equal(1, migrated.Snapshot(42, []).Model.Samples);
    }

    [Fact]
    public void ExactAudioSearchFindsTheNearestTrackInTheFullTenThousandVectorCache()
    {
        using var store = new ListeningEventStore(Database);
        using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            writer.Open(); using var transaction = writer.BeginTransaction(); using var insert = writer.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO audio_vectors(account_id,track_id,model,vector,metadata,used_at) VALUES(42,$id,$model,$vector,$metadata,$at)";
            var id = insert.Parameters.Add("$id", SqliteType.Integer); var vector = insert.Parameters.Add("$vector", SqliteType.Blob);
            var metadata = insert.Parameters.Add("$metadata", SqliteType.Text);
            insert.Parameters.AddWithValue("$model", MusicnnEncoder.ModelId); insert.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            var other = AudioVectorCodec.Encode(Unit(1)); var match = AudioVectorCodec.Encode(Unit(0));
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            for (var track = 1; track <= 10000; track++)
            {
                id.Value = track; vector.Value = track == 10000 ? match : other;
                metadata.Value = JsonSerializer.Serialize(Track(track), json); insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        var watch = Stopwatch.StartNew(); var result = store.SearchAudio(42, Unit(0), 20);
        output.WriteLine($"Exact SQL search of 10000 x 200 dimensions: {watch.Elapsed.TotalMilliseconds:F1} ms");
        watch.Restart(); store.SearchAudio(42, Unit(0), 20);
        output.WriteLine($"Warm SQL search: {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(10000, result[0].Track.Id); Assert.Equal(20, result.Length); Assert.Equal(1, result[0].Similarity, 6);
    }

    [Fact]
    public void AnOversizedOldJournalIsLearnedAndCompactedBeforeApplyingTheNewPageLimit()
    {
        using (var old = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            old.Open(); using var command = old.CreateCommand();
            command.CommandText = """
                CREATE TABLE events(sequence INTEGER PRIMARY KEY AUTOINCREMENT,account_id INTEGER,event_id TEXT,kind INTEGER,occurred_at TEXT,payload TEXT,UNIQUE(account_id,event_id));
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
            using var transaction = old.BeginTransaction(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO events(account_id,event_id,kind,occurred_at,payload) VALUES(42,$id,0,$at,$payload)";
            var id = command.Parameters.Add("$id", SqliteType.Text); var at = command.Parameters.Add("$at", SqliteType.Text);
            var payload = command.Parameters.Add("$payload", SqliteType.Text);
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            for (var trackId = 1; trackId <= 160; trackId++)
            {
                var item = Play(DateTimeOffset.UtcNow, trackId); var track = Track(trackId) with { Title = new string('x', 4000) };
                item = item with { Track = track, Listening = item.Listening! with { Track = track } };
                id.Value = item.EventId.ToString("N"); at.Value = item.OccurredAt.ToString("O"); payload.Value = JsonSerializer.Serialize(item, json);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        Assert.True(new FileInfo(Database).Length > 1024 * 1024);
        using var migrated = new ListeningEventStore(Database, policy: new(MaximumDatabaseBytes: 1024 * 1024));
        Assert.Equal(160, migrated.Snapshot(42, []).Model.Samples);
        Assert.InRange(migrated.Statistics().PhysicalFileBytes, 1, 1024 * 1024);
    }
}
