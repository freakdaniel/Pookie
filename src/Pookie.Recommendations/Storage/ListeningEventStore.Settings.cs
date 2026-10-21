namespace Pookie.Recommendations.Storage;

public sealed partial class ListeningEventStore
{
    // Raw event retention never expires the learned checkpoint or explicit state.
    public void SetRawHistoryDays(int days)
    {
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(days));
        policy = policy with { RawHistoryDays = days };
        using (var accounts = Command("SELECT DISTINCT account_id FROM events"))
        using (var reader = accounts.ExecuteReader())
            while (reader.Read()) maintenanceAccounts.Add(reader.GetInt64(0));
        Maintain();
    }

    // Clearing raw listening history intentionally leaves model, vectors and
    // manual preferences unchanged. Receipts remain for retry idempotence.
    public void ClearListeningHistory(long account)
    {
        if (account <= 0) throw new ArgumentOutOfRangeException(nameof(account));
        using var command = Command("DELETE FROM events WHERE account_id=$account AND kind=0");
        command.Parameters.AddWithValue("$account", account); command.ExecuteNonQuery();
        neighborCache.Remove(account);
    }

    // A model reset preserves the authoritative explicit preference table, but
    // drops its raw event history too so opening the database cannot replay it.
    public void ResetLearning(long account)
    {
        if (account <= 0) throw new ArgumentOutOfRangeException(nameof(account));
        using var transaction = connection.BeginTransaction();
        using var command = Command("""
            DELETE FROM events WHERE account_id=$account;
            DELETE FROM models WHERE account_id=$account;
            DELETE FROM audio_vectors WHERE account_id=$account;
            DELETE FROM receipts WHERE account_id=$account;
            """, transaction);
        command.Parameters.AddWithValue("$account", account); command.ExecuteNonQuery();
        transaction.Commit();
        neighborCache.Remove(account); maintenanceAccounts.Remove(account);
    }
}
