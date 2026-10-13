using Pookie.Recommendations;
using Pookie.Recommendations.Audio;
using Pookie.Recommendations.Learning;
using Pookie.Recommendations.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class WaveMoodTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pookie-moods-" + Guid.NewGuid().ToString("N"));
    public WaveMoodTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string Database => Path.Combine(directory, "events.sqlite3");
    private static WaveTrack Track(long id = 1) => new(id, "Song " + id, "Artist", 1, "Rock", 180000, null, null, null);
    private static AudioVector Vector(int index = 0, AudioMood? mood = null)
    {
        var values = new float[200]; values[index] = 1; return new(MusicnnEncoder.ModelId, values, mood);
    }
    private static WaveEvent Play(Guid session, WaveMood mood, ListeningEndReason reason, double seconds,
        FeatureSnapshot? features = null, long id = 1)
    {
        var now = DateTimeOffset.UtcNow;
        return WaveEvent.FromListening(42, new(Guid.NewGuid(), session, Track(id), now.AddSeconds(-seconds), now, seconds,
            ListeningSource.Wave, reason, WaveMode.Balance, Features: features, Mood: mood));
    }

    [Fact]
    public void LongRejectedListeningDoesNotRewardTheProfile()
    {
        var learner = new OnlineRanker();
        learner.Observe(1, Play(Guid.NewGuid(), WaveMood.Calm, ListeningEndReason.Rejected, 150), Vector());
        Assert.Equal(0, learner.Snapshot().Samples); Assert.Empty(learner.Snapshot().PositiveAudio);
        learner.Observe(2, new(Guid.NewGuid(), 42, DateTimeOffset.UtcNow, WaveEventKind.Block, Track(), Mood: WaveMood.Calm), Vector());
        Assert.Equal(1, learner.Snapshot().Samples); Assert.Empty(learner.Snapshot().PositiveAudio);
        Assert.Single(learner.Snapshot().NegativeAudioGroups!);
        Assert.Equal(1, learner.Snapshot().Contexts![WaveMood.Calm].Samples);
        var taste = new WaveTaste(); taste.Record(Track(), 150, false, DateTimeOffset.UtcNow, rejected: true);
        Assert.Equal(0, Assert.Single(taste.Snapshot().Tracks).GoodPlays);
    }

    [Fact]
    public void MoodSkipsTeachTheContextWithoutPunishingTheGeneralTaste()
    {
        var learner = new OnlineRanker();
        learner.Observe(1, new(Guid.NewGuid(), 42, DateTimeOffset.UtcNow, WaveEventKind.Like, Track()), Vector());
        var before = learner.Snapshot();
        learner.Observe(2, Play(Guid.NewGuid(), WaveMood.Calm, ListeningEndReason.Skip, 5), Vector());
        var after = learner.Snapshot();
        Assert.Equal(before.Weights, after.Weights); Assert.Equal(before.Samples, after.Samples);
        Assert.Equal(1, after.Contexts![WaveMood.Calm].Samples);
        Assert.NotNull(after.Contexts[WaveMood.Calm].SessionNegativeAudio);
    }

    [Fact]
    public void SkippingALikedTrackDoesNotUndoTheExplicitLike()
    {
        var learner = new OnlineRanker();
        learner.Observe(1, new(Guid.NewGuid(), 42, DateTimeOffset.UtcNow, WaveEventKind.Like, Track()), Vector());
        var before = learner.Snapshot();
        learner.Observe(2, Play(Guid.NewGuid(), WaveMood.Auto, ListeningEndReason.Skip, 5, learner.Evaluate(Track(), Vector(), liked: true)), Vector());
        Assert.Equal(before.Weights, learner.Snapshot().Weights); Assert.NotNull(learner.Snapshot().SessionNegativeAudio);
    }

    [Fact]
    public void NegativeEvidenceGrowsAcrossSessionsAndMultipleStylesStaySeparate()
    {
        var learner = new OnlineRanker(); var session = Guid.NewGuid();
        learner.Observe(1, Play(session, WaveMood.Auto, ListeningEndReason.Skip, 5), Vector());
        learner.Observe(2, Play(session, WaveMood.Auto, ListeningEndReason.Skip, 5), Vector());
        Assert.Equal(1, learner.Snapshot().Skips![1].Sessions);
        learner.Observe(3, Play(Guid.NewGuid(), WaveMood.Auto, ListeningEndReason.Skip, 5), Vector());
        Assert.Equal(2, learner.Snapshot().Skips![1].Sessions);
        for (var i = 0; i < 5; i++)
            learner.Observe(4 + i, new(Guid.NewGuid(), 42, DateTimeOffset.UtcNow, WaveEventKind.Like, Track(i + 10)), Vector(i));
        Assert.Equal(3, learner.Snapshot().PositiveAudio.Length);
        for (var i = 0; i < 5; i++)
            learner.Observe(9 + i, new(Guid.NewGuid(), 42, DateTimeOffset.UtcNow, WaveEventKind.Block, Track(i + 20)), Vector(i));
        Assert.Equal(3, learner.Snapshot().NegativeAudioGroups!.Length);
    }

    [Fact]
    public void MoodSelectionChangesOrderWhileUnknownAudioRemainsEligible()
    {
        var learner = new OnlineRanker();
        var calm = Vector(0, new(.95f, .05f, .5f, .05f));
        var energetic = Vector(1, new(.05f, .95f, .5f, .95f));
        Assert.True(learner.Score(Track(), calm, mood: WaveMood.Calm) > learner.Score(Track(2), energetic, mood: WaveMood.Calm));
        Assert.True(learner.Score(Track(), calm, mood: WaveMood.Energetic) < learner.Score(Track(2), energetic, mood: WaveMood.Energetic));
        Assert.Equal(.5, learner.Score(Track(3), null, mood: WaveMood.Calm));
        Assert.Equal(learner.Score(Track(), calm), learner.Score(Track(2), energetic));
    }

    [Fact]
    public void SessionPenaltiesDoNotLeakIntoTheNextListeningSession()
    {
        var session = Guid.NewGuid(); var learner = new OnlineRanker();
        learner.Observe(1, Play(session, WaveMood.Auto, ListeningEndReason.Skip, 5), Vector());
        var inSession = learner.Score(Track(), Vector(), session: session);
        var nextSession = learner.Score(Track(), Vector(), session: Guid.NewGuid());
        Assert.True(nextSession > inSession);
    }

    [Fact]
    public void CancellingAnExclusionRestoresTheWholeRecognisedWork()
    {
        var original = Track() with { Title = "Song", Isrc = "ABC" };
        var duplicate = Track(2) with { Title = "Another upload", Isrc = "ABC" };
        var taste = new WaveTaste(); taste.Block(original); taste.Block(duplicate);
        Assert.True(taste.IsBlocked(duplicate));
        Assert.Equal(2, taste.Unblock(original).Length);
        Assert.False(taste.IsBlocked(original));
        Assert.Single(taste.Rank([original, duplicate], [], 10, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MoodCacheAndContextCheckpointSurviveRestartAndUnblockPreservesLikes()
    {
        var descriptor = new AudioMood(.8f, .1f, .7f, .2f);
        using (var store = new ListeningEventStore(Database))
        {
            store.PutVector(42, Track(), Vector(mood: descriptor), DateTimeOffset.UtcNow);
            store.AppendBatch([Play(Guid.NewGuid(), WaveMood.Melancholic, ListeningEndReason.NaturalEnd, 150)]);
            var now = DateTimeOffset.UtcNow;
            store.AppendBatch([new(Guid.NewGuid(), 42, now, WaveEventKind.Block, Track()),
                new(Guid.NewGuid(), 42, now.AddSeconds(1), WaveEventKind.Unblock, Track()),
                new(Guid.NewGuid(), 42, now.AddSeconds(2), WaveEventKind.Like, Track())]);
            Assert.Equal((true, false), store.Preference(42, 1));
        }
        using var reopened = new ListeningEventStore(Database);
        Assert.Equal(descriptor, reopened.GetVector(42, 1)!.Mood);
        Assert.Equal(1, reopened.Snapshot(42, []).Model.Contexts![WaveMood.Melancholic].Samples);
        Assert.NotNull(Assert.Single(reopened.Snapshot(42, []).Vectors).Value.Mood);
    }

    [Fact]
    public void ExistingVersionTwoVectorCacheMigratesWithoutLosingEmbeddings()
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA user_version=2;
                CREATE TABLE audio_vectors(account_id INTEGER, track_id INTEGER, model TEXT, vector BLOB, metadata TEXT, used_at TEXT,
                    PRIMARY KEY(account_id,track_id,model));
                INSERT INTO audio_vectors VALUES(42,1,$model,$vector,'{}',$at);
                """;
            command.Parameters.AddWithValue("$model", MusicnnEncoder.ModelId);
            command.Parameters.AddWithValue("$vector", AudioVectorCodec.Encode(Vector().Values));
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
        }
        using var store = new ListeningEventStore(Database);
        var old = store.GetVector(42, 1)!; Assert.Null(old.Mood); Assert.Equal(1, old.Values[0]);
        store.PutVector(42, Track(), old with { Mood = new(.5f, .5f, .5f, .5f) }, DateTimeOffset.UtcNow);
        Assert.NotNull(store.GetVector(42, 1)!.Mood);
    }
}
