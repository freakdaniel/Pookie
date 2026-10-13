using Microsoft.Data.Sqlite;
using System.Text.Json;
using Pookie.Recommendations.Audio;
using Pookie.Recommendations.Learning;

namespace Pookie.Recommendations.Storage;

public sealed record VectorMatch(WaveTrack Track, double Similarity);
public sealed record ScoredTrack(long TrackId, double Score);
public sealed record RecommendationSnapshot(RankerState Model, Dictionary<long, AudioVector> Vectors, VectorMatch[] Neighbors, ScoredTrack[] Ranking)
{
    public FeatureSnapshot Evaluate(WaveTrack track, bool liked = false, WaveMood mood = WaveMood.Auto, Guid? session = null) => new OnlineRanker(Model).Evaluate(track, Vectors.GetValueOrDefault(track.Id), liked, mood, session);
}

public sealed partial class ListeningEventStore
{
    private readonly Dictionary<long, (long Sequence, VectorMatch[] Matches)> neighborCache = [];
    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = transaction; return command;
    }
    private void InitializeLearning()
    {
        using var schema = Command("""
            CREATE TABLE IF NOT EXISTS preferences (
                account_id INTEGER NOT NULL, track_id INTEGER NOT NULL, liked INTEGER NOT NULL DEFAULT 0,
                blocked INTEGER NOT NULL DEFAULT 0, metadata TEXT NOT NULL, occurred_at TEXT NOT NULL,
                PRIMARY KEY(account_id,track_id));
            CREATE TABLE IF NOT EXISTS models (
                account_id INTEGER PRIMARY KEY, sequence INTEGER NOT NULL, state TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS receipts (
                account_id INTEGER NOT NULL,event_id TEXT NOT NULL,occurred_at TEXT NOT NULL,PRIMARY KEY(account_id,event_id));
            CREATE INDEX IF NOT EXISTS receipts_at ON receipts(occurred_at);
            INSERT OR IGNORE INTO receipts SELECT account_id,event_id,occurred_at FROM events;
            CREATE TABLE IF NOT EXISTS account_state (account_id INTEGER PRIMARY KEY,reset_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS audio_vectors (
                account_id INTEGER NOT NULL, track_id INTEGER NOT NULL, model TEXT NOT NULL,
                vector BLOB NOT NULL CHECK(length(vector)=800), metadata TEXT NOT NULL, used_at TEXT NOT NULL,
                PRIMARY KEY(account_id,track_id,model));
            CREATE INDEX IF NOT EXISTS audio_vectors_used ON audio_vectors(used_at);
            """);
        schema.ExecuteNonQuery();
        using (var columns = Command("SELECT count(*) FROM pragma_table_info('audio_vectors') WHERE name='mood'"))
            if (Convert.ToInt32(columns.ExecuteScalar()) == 0)
            {
                using var migration = Command("ALTER TABLE audio_vectors ADD COLUMN mood TEXT"); migration.ExecuteNonQuery();
            }
        connection.CreateFunction<byte[], byte[], double>("audio_cosine", AudioVectorCodec.BlobDot, isDeterministic: true);
        connection.CreateFunction<byte[], byte[], double>("audio_max_cosine", AudioVectorCodec.BlobMaxDot, isDeterministic: true);
        using var accounts = Command("SELECT DISTINCT account_id FROM events");
        var users = new List<long>();
        using (var reader = accounts.ExecuteReader()) while (reader.Read()) users.Add(reader.GetInt64(0));
        using var transaction = connection.BeginTransaction();
        foreach (var account in users)
        {
            // Version-1 events are real observations; checkpoint them before any retention removes them.
            TrainAccount(account, transaction, replayPreferences: true);
            maintenanceAccounts.Add(account);
        }
        transaction.Commit();
    }

    private RankerState LoadModel(long account, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT state FROM models WHERE account_id=$account", transaction);
        command.Parameters.AddWithValue("$account", account);
        var state = command.ExecuteScalar() as string;
        return state == null ? RankerState.Empty() : JsonSerializer.Deserialize(state, JournalJson.Default.RankerState)
            ?? throw new InvalidDataException("Invalid wave model checkpoint.");
    }

    private void TrainAccount(long account, SqliteTransaction transaction, bool replayPreferences = false)
    {
        var learner = new OnlineRanker(LoadModel(account, transaction));
        var cursor = learner.Snapshot().Sequence;
        while (true)
        {
            using var read = Command("SELECT sequence,payload FROM events WHERE account_id=$account AND sequence>$cursor ORDER BY sequence LIMIT 256", transaction);
            read.Parameters.AddWithValue("$account", account); read.Parameters.AddWithValue("$cursor", cursor);
            var batch = new List<StoredWaveEvent>();
            using (var reader = read.ExecuteReader())
                while (reader.Read()) batch.Add(new(reader.GetInt64(0), JsonSerializer.Deserialize(reader.GetString(1), JournalJson.Default.WaveEvent)
                    ?? throw new InvalidDataException("Invalid event payload.")));
            if (batch.Count == 0) break;
            foreach (var row in batch)
            {
                row.Event.Validate();
                if (replayPreferences) ApplyPreference(row.Event, transaction);
                learner.Observe(row.Sequence, row.Event, row.Event.Track == null ? null : GetVector(account, row.Event.Track.Id, transaction));
                cursor = row.Sequence;
            }
        }
        var snapshot = learner.Snapshot();
        using var save = Command("INSERT INTO models VALUES($account,$sequence,$state) ON CONFLICT(account_id) DO UPDATE SET sequence=excluded.sequence,state=excluded.state", transaction);
        save.Parameters.AddWithValue("$account", account); save.Parameters.AddWithValue("$sequence", snapshot.Sequence);
        save.Parameters.AddWithValue("$state", JsonSerializer.Serialize(snapshot, JournalJson.Default.RankerState));
        save.ExecuteNonQuery();
    }

    private void ApplyPreference(WaveEvent item, SqliteTransaction transaction)
    {
        if (item.Kind == WaveEventKind.Listening) return;
        neighborCache.Remove(item.AccountId);
        if (item.Kind == WaveEventKind.ResetBlocks)
        {
            using var reset = Command("""
                INSERT INTO account_state VALUES($account,$at) ON CONFLICT(account_id) DO UPDATE SET reset_at=max(account_state.reset_at,excluded.reset_at);
                UPDATE preferences SET blocked=0,occurred_at=$at WHERE account_id=$account AND occurred_at<=$at;
                """, transaction);
            reset.Parameters.AddWithValue("$account", item.AccountId);
            reset.Parameters.AddWithValue("$at", item.OccurredAt.ToUniversalTime().ToString("O")); reset.ExecuteNonQuery(); return;
        }
        if (item.Kind == WaveEventKind.Block)
        {
            using var reset = Command("SELECT reset_at FROM account_state WHERE account_id=$account", transaction);
            reset.Parameters.AddWithValue("$account", item.AccountId);
            if (reset.ExecuteScalar() is string at && string.CompareOrdinal(at, item.OccurredAt.ToUniversalTime().ToString("O")) > 0) return;
        }
        using var command = Command("""
            INSERT INTO preferences VALUES($account,$track,$liked,$blocked,$metadata,$at)
            ON CONFLICT(account_id,track_id) DO UPDATE SET
                liked=CASE WHEN $kind IN (1,2,3) THEN excluded.liked ELSE preferences.liked END,
                blocked=CASE WHEN $kind IN (1,3,5) THEN excluded.blocked ELSE preferences.blocked END,
                metadata=excluded.metadata,occurred_at=excluded.occurred_at
            WHERE excluded.occurred_at>=preferences.occurred_at;
            """, transaction);
        command.Parameters.AddWithValue("$account", item.AccountId); command.Parameters.AddWithValue("$track", item.Track!.Id);
        command.Parameters.AddWithValue("$liked", item.Kind == WaveEventKind.Like ? 1 : 0);
        command.Parameters.AddWithValue("$blocked", item.Kind == WaveEventKind.Block ? 1 : 0);
        command.Parameters.AddWithValue("$kind", (int)item.Kind);
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(item.Track, JournalJson.Default.WaveTrack));
        command.Parameters.AddWithValue("$at", item.OccurredAt.ToUniversalTime().ToString("O")); command.ExecuteNonQuery();
    }

    public AudioVector? GetVector(long account, long track, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT vector,mood FROM audio_vectors WHERE account_id=$account AND track_id=$track AND model=$model", transaction);
        command.Parameters.AddWithValue("$account", account); command.Parameters.AddWithValue("$track", track);
        command.Parameters.AddWithValue("$model", MusicnnEncoder.ModelId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(MusicnnEncoder.ModelId, AudioVectorCodec.Decode((byte[])reader[0]), ReadMood(reader, 1)) : null;
    }

    public void PutVector(long account, WaveTrack track, AudioVector vector, DateTimeOffset now, SqliteTransaction? transaction = null)
    {
        if (account <= 0 || track.Id <= 0 || vector.Model != MusicnnEncoder.ModelId) throw new ArgumentException("Invalid audio vector identity.");
        vector.Mood?.Validate();
        neighborCache.Remove(account);
        using var command = Command("""
            INSERT INTO audio_vectors(account_id,track_id,model,vector,metadata,used_at,mood) VALUES($account,$track,$model,$vector,$metadata,$at,$mood)
            ON CONFLICT(account_id,track_id,model) DO UPDATE SET vector=excluded.vector,metadata=excluded.metadata,used_at=excluded.used_at,mood=excluded.mood;
            """, transaction);
        command.Parameters.AddWithValue("$account", account); command.Parameters.AddWithValue("$track", track.Id);
        command.Parameters.AddWithValue("$model", vector.Model); command.Parameters.AddWithValue("$vector", AudioVectorCodec.Encode(vector.Values));
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(track, JournalJson.Default.WaveTrack));
        command.Parameters.AddWithValue("$mood", vector.Mood == null ? DBNull.Value : JsonSerializer.Serialize(vector.Mood, JournalJson.Default.AudioMood));
        command.Parameters.AddWithValue("$at", now.ToUniversalTime().ToString("O")); command.ExecuteNonQuery();
    }

    public VectorMatch[] SearchAudio(long account, float[] vector, int count = 20)
        => SearchCenters(account, [vector], count);

    private VectorMatch[] SearchCenters(long account, float[][] centers, int count)
    {
        if (account <= 0 || count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        if (centers.Length == 0) return [];
        using var command = Command("""
            SELECT metadata,audio_max_cosine(vector,$query) AS similarity FROM audio_vectors
            WHERE account_id=$account AND model=$model AND NOT EXISTS (
                SELECT 1 FROM preferences p WHERE p.account_id=audio_vectors.account_id AND p.track_id=audio_vectors.track_id AND p.blocked=1)
            ORDER BY similarity DESC,track_id LIMIT $count;
            """);
        command.Parameters.AddWithValue("$query", centers.SelectMany(center => AudioVectorCodec.Encode(center)).ToArray()); command.Parameters.AddWithValue("$account", account);
        command.Parameters.AddWithValue("$model", MusicnnEncoder.ModelId); command.Parameters.AddWithValue("$count", count);
        var result = new List<VectorMatch>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new(JsonSerializer.Deserialize(reader.GetString(0), JournalJson.Default.WaveTrack)
            ?? throw new InvalidDataException("Invalid vector metadata."), reader.GetDouble(1)));
        return result.ToArray();
    }

    public RecommendationSnapshot Snapshot(long account, IReadOnlyList<WaveTrack> candidates)
    {
        if (account <= 0 || candidates.Count > 2000) throw new ArgumentOutOfRangeException(nameof(account));
        var model = LoadModel(account);
        var centers = (model.AudioModel == MusicnnEncoder.ModelId ? model.PositiveAudio : [])
            .Concat(model.Contexts?.Values.Where(context => context.AudioModel == MusicnnEncoder.ModelId).SelectMany(context => context.PositiveAudio) ?? []);
        if (!neighborCache.TryGetValue(account, out var cached) || cached.Sequence != model.Sequence)
        {
            cached = (model.Sequence, SearchCenters(account, centers.Take(15).ToArray(), 40));
            neighborCache[account] = cached;
            if (neighborCache.Count > 4) neighborCache.Remove(neighborCache.Keys.First());
        }
        var neighbors = cached.Matches.ToArray();
        using var command = Command("SELECT track_id,vector,mood FROM audio_vectors WHERE account_id=$account AND model=$model AND track_id IN (SELECT value FROM json_each($ids))");
        command.Parameters.AddWithValue("$account", account); command.Parameters.AddWithValue("$model", MusicnnEncoder.ModelId);
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(candidates.Select(track => track.Id).Concat(neighbors.Select(match => match.Track.Id)).Distinct().ToArray(), JournalJson.Default.Int64Array));
        var vectors = new Dictionary<long, AudioVector>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) vectors[reader.GetInt64(0)] = new(MusicnnEncoder.ModelId, AudioVectorCodec.Decode((byte[])reader[1]), ReadMood(reader, 2));
        using var preferences = Command("SELECT track_id,liked,blocked FROM preferences WHERE account_id=$account");
        preferences.Parameters.AddWithValue("$account", account);
        var liked = new HashSet<long>(); var blocked = new HashSet<long>();
        using (var reader = preferences.ExecuteReader())
            while (reader.Read())
            {
                if (reader.GetBoolean(1)) liked.Add(reader.GetInt64(0));
                if (reader.GetBoolean(2)) blocked.Add(reader.GetInt64(0));
            }
        var learner = new OnlineRanker(model);
        var ranked = candidates.Where(WaveTaste.IsPlayable).Where(track => !blocked.Contains(track.Id)).DistinctBy(track => track.Id)
            .Select(track => new ScoredTrack(track.Id, learner.Evaluate(track, vectors.GetValueOrDefault(track.Id), liked.Contains(track.Id)).Prediction))
            .OrderByDescending(track => track.Score).ThenBy(track => track.TrackId).ToArray();
        return new(model, vectors, neighbors, ranked);
    }

    private static AudioMood? ReadMood(SqliteDataReader reader, int column)
    {
        if (reader.IsDBNull(column)) return null;
        var mood = JsonSerializer.Deserialize(reader.GetString(column), JournalJson.Default.AudioMood)
            ?? throw new InvalidDataException("Invalid cached mood.");
        mood.Validate(); return mood;
    }

    public (bool Liked, bool Blocked) Preference(long account, long track)
    {
        using var command = Command("SELECT liked,blocked FROM preferences WHERE account_id=$account AND track_id=$track");
        command.Parameters.AddWithValue("$account", account); command.Parameters.AddWithValue("$track", track);
        using var reader = command.ExecuteReader(); return reader.Read() ? (reader.GetBoolean(0), reader.GetBoolean(1)) : (false, false);
    }
}
