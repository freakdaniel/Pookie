namespace Pookie.Recommendations.Storage;

public sealed record JournalPolicy(int RetainedListeningEvents = 50000, int RetainedPreferenceEvents = 5000,
    int RawHistoryDays = 90, int RetainedVectors = 10000, int VectorHistoryDays = 180,
    long MaximumDatabaseBytes = 128L * 1024 * 1024);
public sealed record JournalStatistics(long DatabaseBytes, long FreeBytes, long WalBytes, long Events, long Vectors, int VacuumMode, long PhysicalFileBytes);
