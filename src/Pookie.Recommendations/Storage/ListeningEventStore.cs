using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.Recommendations.Storage;

public sealed record StoredWaveEvent(long Sequence, WaveEvent Event);

// Synchronous SQLite access belongs to the journal worker, never the UI or audio callback.
public sealed partial class ListeningEventStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly int retainedListeningEvents;
    private readonly HashSet<long> maintenanceAccounts = [];
    private int pendingMaintenance;
    private bool disposed;
    private readonly JournalPolicy policy;

    public ListeningEventStore(string path, int retainedListeningEvents = 50000, JournalPolicy? policy = null)
    {
        if (retainedListeningEvents < 1) throw new ArgumentOutOfRangeException(nameof(retainedListeningEvents));
        this.policy = policy ?? new(RetainedListeningEvents: retainedListeningEvents);
        this.retainedListeningEvents = this.policy.RetainedListeningEvents;
        connection = new(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Pooling = false, DefaultTimeout = 2 }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            var version = Convert.ToInt32(command.ExecuteScalar());
            if (version > 3) throw new InvalidOperationException("The wave journal was created by a newer application.");
            ConfigureStorage(version);
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS events (
                    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                    account_id INTEGER NOT NULL,
                    event_id TEXT NOT NULL,
                    kind INTEGER NOT NULL,
                    occurred_at TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    UNIQUE(account_id, event_id)
                );
                CREATE INDEX IF NOT EXISTS events_account_sequence ON events(account_id, sequence);
                CREATE INDEX IF NOT EXISTS events_account_kind_sequence ON events(account_id, kind, sequence);
                """;
            command.ExecuteNonQuery();
            InitializeLearning();
            command.CommandText = "PRAGMA user_version=3"; command.ExecuteNonQuery();
            FinishStorageMigration();
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void AppendBatch(IReadOnlyList<WaveEvent> events, IReadOnlyDictionary<Guid, Audio.AudioVector>? vectors = null)
    {
        foreach (var item in events) item.Validate();
        CheckWriteBudget();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO events(account_id, event_id, kind, occurred_at, payload)
            VALUES ($account, $id, $kind, $at, $payload)
            ON CONFLICT(account_id, event_id) DO NOTHING;
            """;
        var account = command.Parameters.Add("$account", SqliteType.Integer);
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var kind = command.Parameters.Add("$kind", SqliteType.Integer);
        var at = command.Parameters.Add("$at", SqliteType.Text);
        var payload = command.Parameters.Add("$payload", SqliteType.Text);
        foreach (var item in events)
        {
            var serialized = JsonSerializer.Serialize(item, JournalJson.Default.WaveEvent);
            if (System.Text.Encoding.UTF8.GetByteCount(serialized) > 32768) throw new ArgumentException("An event exceeds the journal payload budget.");
            using var receipt = Command("INSERT OR IGNORE INTO receipts VALUES($account,$id,$at)", transaction);
            receipt.Parameters.AddWithValue("$account", item.AccountId); receipt.Parameters.AddWithValue("$id", item.EventId.ToString("N"));
            receipt.Parameters.AddWithValue("$at", item.OccurredAt.ToUniversalTime().ToString("O"));
            if (receipt.ExecuteNonQuery() == 0) continue;
            account.Value = item.AccountId; id.Value = item.EventId.ToString("N"); kind.Value = (int)item.Kind;
            at.Value = item.OccurredAt.ToUniversalTime().ToString("O");
            payload.Value = serialized;
            if (command.ExecuteNonQuery() > 0) ApplyPreference(item, transaction);
            if (vectors?.TryGetValue(item.EventId, out var vector) == true && item.Track != null)
                PutVector(item.AccountId, item.Track, vector, item.OccurredAt, transaction);
            if (item.Track != null)
            {
                using var touch = Command("UPDATE audio_vectors SET used_at=max(used_at,$at) WHERE account_id=$account AND track_id=$track", transaction);
                touch.Parameters.AddWithValue("$at", item.OccurredAt.ToUniversalTime().ToString("O"));
                touch.Parameters.AddWithValue("$account", item.AccountId); touch.Parameters.AddWithValue("$track", item.Track.Id); touch.ExecuteNonQuery();
            }
        }
        foreach (var user in events.Select(item => item.AccountId).Distinct()) TrainAccount(user, transaction);
        transaction.Commit();
        foreach (var item in events) maintenanceAccounts.Add(item.AccountId);
        pendingMaintenance += events.Count;
        if (pendingMaintenance >= 256)
        {
            // Bound raw listening history. Explicit actions are preserved, and SQLite reuses freed pages.
            Maintain();
            maintenanceAccounts.Clear(); pendingMaintenance = 0;
        }
    }

    private void PruneListening(long accountId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM events WHERE account_id=$account AND kind=$kind AND sequence < (
                SELECT sequence FROM events WHERE account_id=$account AND kind=$kind
                ORDER BY sequence DESC LIMIT 1 OFFSET $offset
            );
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$kind", (int)WaveEventKind.Listening);
        command.Parameters.AddWithValue("$offset", retainedListeningEvents - 1);
        command.ExecuteNonQuery();
    }

    public StoredWaveEvent[] Read(long accountId, long afterSequence = 0, int limit = 256)
    {
        if (accountId <= 0 || afterSequence < 0 || limit is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(accountId));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence, payload FROM events WHERE account_id=$account AND sequence>$after ORDER BY sequence LIMIT $limit";
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$after", afterSequence);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var result = new List<StoredWaveEvent>();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize(reader.GetString(1), JournalJson.Default.WaveEvent)
                ?? throw new InvalidDataException("An event has no payload.");
            item.Validate();
            if (item.AccountId != accountId) throw new InvalidDataException("An event belongs to another account.");
            result.Add(new(reader.GetInt64(0), item));
        }
        return result.ToArray();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            // Also maintain short sessions that never accumulated a full maintenance batch.
            Maintain();
        }
        finally { connection.Dispose(); }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WaveEvent))]
[JsonSerializable(typeof(WaveTrack))]
[JsonSerializable(typeof(Learning.RankerState))]
[JsonSerializable(typeof(long[]))]
[JsonSerializable(typeof(AudioMood))]
internal partial class JournalJson : JsonSerializerContext;
