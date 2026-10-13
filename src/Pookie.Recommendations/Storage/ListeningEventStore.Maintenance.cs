namespace Pookie.Recommendations.Storage;

public sealed partial class ListeningEventStore
{
    private bool oversizedMigration;
    private void ConfigureStorage(int version)
    {
        if (policy.MaximumDatabaseBytes < 1024 * 1024 || policy.RawHistoryDays < 1 || policy.VectorHistoryDays < 1 ||
            policy.RetainedListeningEvents < 1 || policy.RetainedPreferenceEvents < 1 || policy.RetainedVectors < 1)
            throw new ArgumentException("Invalid journal storage policy.");
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA auto_vacuum";
        var vacuum = Convert.ToInt32(command.ExecuteScalar());
        if (vacuum != 2)
        {
            command.CommandText = "PRAGMA auto_vacuum=INCREMENTAL"; command.ExecuteNonQuery();
            if (version > 0) { command.CommandText = "VACUUM"; command.ExecuteNonQuery(); }
        }
        command.CommandText = "PRAGMA page_size";
        var pageSize = Convert.ToInt64(command.ExecuteScalar());
        command.CommandText = "PRAGMA page_count";
        oversizedMigration = Convert.ToInt64(command.ExecuteScalar()) * pageSize > policy.MaximumDatabaseBytes;
        if (!oversizedMigration)
        {
            command.CommandText = $"PRAGMA max_page_count={policy.MaximumDatabaseBytes / pageSize}"; command.ExecuteScalar();
        }
        command.CommandText = "PRAGMA wal_autocheckpoint=256; PRAGMA journal_size_limit=4194304; PRAGMA cache_size=-4096;";
        command.ExecuteNonQuery();
    }

    private void FinishStorageMigration()
    {
        if (!oversizedMigration) return;
        // Old journals must be learned before reducing their raw data and physical page limit.
        for (var pass = 0; pass < 128; pass++)
        {
            var before = Statistics();
            if (before.DatabaseBytes - before.FreeBytes < policy.MaximumDatabaseBytes * .8) break;
            Maintain();
            var after = Statistics();
            if (after.DatabaseBytes - after.FreeBytes >= before.DatabaseBytes - before.FreeBytes)
                throw new IOException("The preserved wave state exceeds the configured database budget.");
        }
        using var vacuum = Command("VACUUM"); vacuum.ExecuteNonQuery();
        using var size = Command("PRAGMA page_size");
        var pageSize = Convert.ToInt64(size.ExecuteScalar());
        using var limit = Command($"PRAGMA max_page_count={policy.MaximumDatabaseBytes / pageSize}");
        var applied = Convert.ToInt64(limit.ExecuteScalar());
        if (applied * pageSize > policy.MaximumDatabaseBytes)
            throw new IOException("The migrated wave journal could not fit the database budget.");
    }

    public JournalStatistics Statistics()
    {
        long Scalar(string sql) { using var command = Command(sql); return Convert.ToInt64(command.ExecuteScalar()); }
        var pageSize = Scalar("PRAGMA page_size");
        var path = connection.DataSource + "-wal";
        return new(Scalar("PRAGMA page_count") * pageSize, Scalar("PRAGMA freelist_count") * pageSize,
            File.Exists(path) ? new FileInfo(path).Length : 0, Scalar("SELECT count(*) FROM events"),
            Scalar("SELECT count(*) FROM audio_vectors"), (int)Scalar("PRAGMA auto_vacuum"), new FileInfo(connection.DataSource).Length);
    }

    public void Maintain()
    {
        neighborCache.Clear();
        foreach (var account in maintenanceAccounts)
        {
            PruneListening(account);
            using var command = Command("""
                DELETE FROM events WHERE account_id=$account AND sequence<=(SELECT sequence FROM models WHERE account_id=$account)
                AND (occurred_at<$cutoff OR sequence IN (
                    SELECT sequence FROM events WHERE account_id=$account AND kind<>0 ORDER BY sequence DESC LIMIT -1 OFFSET $keep));
                """);
            command.Parameters.AddWithValue("$account", account);
            command.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-policy.RawHistoryDays).ToString("O"));
            command.Parameters.AddWithValue("$keep", policy.RetainedPreferenceEvents); command.ExecuteNonQuery();
        }
        using (var vectors = Command("""
            DELETE FROM audio_vectors WHERE used_at<$cutoff OR rowid IN (
                SELECT rowid FROM audio_vectors ORDER BY used_at DESC,rowid DESC LIMIT -1 OFFSET $keep);
            DELETE FROM preferences WHERE liked=0 AND blocked=0;
            """))
        {
            vectors.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-policy.VectorHistoryDays).ToString("O"));
            vectors.Parameters.AddWithValue("$keep", policy.RetainedVectors); vectors.ExecuteNonQuery();
        }
        using (var receipts = Command("""
            DELETE FROM receipts WHERE occurred_at<$cutoff AND NOT EXISTS (
                SELECT 1 FROM events e WHERE e.account_id=receipts.account_id AND e.event_id=receipts.event_id);
            DELETE FROM receipts WHERE rowid IN (SELECT rowid FROM receipts ORDER BY occurred_at DESC,rowid DESC LIMIT -1 OFFSET 100000);
            """))
        {
            receipts.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-policy.RawHistoryDays).ToString("O"));
            receipts.ExecuteNonQuery();
        }
        // Under pressure, discard processed raw observations; model and explicit state remain intact.
        for (var pass = 0; pass < 64; pass++)
        {
            var sizes = Statistics();
            if (sizes.DatabaseBytes - sizes.FreeBytes < policy.MaximumDatabaseBytes * .8) break;
            using var prune = Command("""
                DELETE FROM events WHERE sequence IN (SELECT e.sequence FROM events e JOIN models m ON m.account_id=e.account_id
                    WHERE e.sequence<=m.sequence ORDER BY e.sequence LIMIT 256);
                """);
            if (prune.ExecuteNonQuery() == 0) break;
        }
        using var reclaim = Command("PRAGMA incremental_vacuum(256)"); reclaim.ExecuteNonQuery();
        using var checkpoint = Command("PRAGMA wal_checkpoint(PASSIVE)"); checkpoint.ExecuteNonQuery();
    }

    private void CheckWriteBudget()
    {
        using var pages = Command("SELECT (page_count-freelist_count)*page_size FROM pragma_page_count,pragma_freelist_count,pragma_page_size");
        if (Convert.ToInt64(pages.ExecuteScalar()) > policy.MaximumDatabaseBytes * .8) Maintain();
        var wal = connection.DataSource + "-wal";
        if (File.Exists(wal) && new FileInfo(wal).Length > 4 * 1024 * 1024)
        {
            using var checkpoint = Command("PRAGMA wal_checkpoint(TRUNCATE)"); checkpoint.ExecuteNonQuery();
            if (File.Exists(wal) && new FileInfo(wal).Length > 16 * 1024 * 1024)
                throw new IOException("An active SQLite reader prevents reclaiming the wave journal WAL.");
        }
    }
}
