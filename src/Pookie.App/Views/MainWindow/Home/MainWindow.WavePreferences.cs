using Pookie.App.Home;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private CancellationTokenSource waveObservationCancellation = new(), waveAudioCancellation = new();
    private readonly HashSet<long> waveMaintenanceAccounts = [];
    private bool CanLearnWave => preferences.WaveLearningEnabled && !waveMaintenanceAccounts.Contains(waveAccount);
    private bool CanAnalyzeWave => CanLearnWave && preferences.WaveAnalysisEnabled;

    private void CancelWaveLearningWork(bool observations)
    {
        ResetWaveCandidateAnalysis();
        waveAudioCancellation.Cancel(); waveAudioCancellation.Dispose(); waveAudioCancellation = new();
        if (observations)
        {
            waveObservationCancellation.Cancel(); waveObservationCancellation.Dispose(); waveObservationCancellation = new();
            waveListening.SuppressLearning();
        }
    }

    private void ApplyWavePreferences(AppConfiguration before)
    {
        if (before.WaveLearningEnabled != preferences.WaveLearningEnabled)
            CancelWaveLearningWork(observations: true);
        else if (before.WaveAnalysisEnabled != preferences.WaveAnalysisEnabled)
            CancelWaveLearningWork(observations: false);
        player?.SetAnalysisCaptureEnabled(CanAnalyzeWave);
        if (before.JournalRetentionDays != preferences.JournalRetentionDays && waveJournal != null)
        {
            var pending = waveJournal.SetRawHistoryDaysAsync(preferences.JournalRetentionDays, lifetime.Token);
            waveJournalWrites.RemoveAll(task => task.IsCompleted); waveJournalWrites.Add(pending);
            Run(async () => await pending);
        }
        if (CanAnalyzeWave) ScheduleWaveCandidateAnalysis();
    }

    private SoundCloudTrack[] WaveBlockedTracks()
    {
        EnsureWaveTaste();
        return waveTaste.Snapshot().Tracks.Where(item => item.Blocked).Select(item => item.Track.ToTrack())
            .DistinctBy(WaveTaste.WorkKey).OrderBy(track => track.Author, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    // The caller confirms the account and explains that SoundCloud history and
    // the learned model remain. New plays are suspended until this barrier ends.
    private Task<bool> ClearLocalWaveHistoryAsync(long account) => MaintainWaveAccountAsync(account, resetLearning: false);

    // Explicit likes, exclusions and Wave mode survive; raw events, vectors and
    // learned playback statistics for this account are discarded.
    private Task<bool> ResetWaveLearningAsync(long account) => MaintainWaveAccountAsync(account, resetLearning: true);

    private async Task<bool> MaintainWaveAccountAsync(long account, bool resetLearning)
    {
        EnsureWaveTaste();
        if (demo || disposed || account <= 0 || me?.Id != account || waveAccount != account ||
            !waveMaintenanceAccounts.Add(account)) return false;
        ++waveLearningEpoch;
        CancelWaveLearningWork(observations: true); player?.SetAnalysisCaptureEnabled(false);
        EnsureWaveJournal();
        try
        {
            if (resetLearning)
            {
                // Clear the live profile before yielding so shutdown cannot save
                // old listening statistics while durable maintenance is pending.
                waveTaste = WaveProfileStore.WithoutLearning(waveTaste.Snapshot());
                waveTaste.SessionId = waveListening.SessionId;
                waveLearning = null; waveAnalyzedTrack = null;
                await waveStore.SaveAsync(account, waveTaste.Snapshot());
            }
            // All accepted writes run on the same worker before this deletion.
            // Cancelled inference cannot publish a vector after the barrier.
            var durable = CommitWaveMaintenanceAsync(account, resetLearning);
            waveJournalWrites.RemoveAll(task => task.IsCompleted); waveJournalWrites.Add(durable);
            await durable;
            if (disposed || waveAccount != account || me?.Id != account) return false;
            localRecent.Clear(); RefreshCachedHome(); RefreshOverviewSections();
            if (resetLearning) await LoadWaveLearningAsync();
            return !disposed && waveAccount == account && me?.Id == account;
        }
        finally
        {
            if (waveAccount == account) waveListening.SuppressLearning();
            waveMaintenanceAccounts.Remove(account);
            if (!disposed)
            {
                player?.SetAnalysisCaptureEnabled(CanAnalyzeWave);
                if (CanAnalyzeWave) ScheduleWaveCandidateAnalysis();
            }
        }
    }

    private async Task CommitWaveMaintenanceAsync(long account, bool resetLearning)
    {
        // Once accepted, finish both durable stores even after the UI loop ends.
        if (resetLearning)
        {
            await waveJournal!.ResetLearningAsync(account).ConfigureAwait(false);
            waveStore.ResetLearning(account);
        }
        else await waveJournal!.ClearListeningHistoryAsync(account).ConfigureAwait(false);
    }
}
