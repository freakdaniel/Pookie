using Pookie.Audio;
using Pookie.Recommendations.Audio;
using Pookie.SoundCloud;
using WaveMode = Pookie.App.Home.WaveMode;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Task? wavePreviewAnalysis;
    private CancellationTokenSource? wavePreviewCancellation;
    private Guid? wavePreviewSession;
    private readonly HashSet<long> wavePreviewAttempts = [];
    private PreviewDownloadBudget wavePreviewBudget = new(12 * 1024 * 1024);
    private TimeSpan wavePreviewNextBatch;

    private void ResetWaveCandidateAnalysis()
    {
        wavePreviewCancellation?.Cancel();
        wavePreviewSession = null; wavePreviewAttempts.Clear();
        wavePreviewBudget = new(12 * 1024 * 1024); wavePreviewNextBatch = TimeSpan.Zero;
    }

    private void ScheduleWaveCandidateAnalysis()
    {
        if (demo || disposed || waveAccount <= 0 || playbackQueue.Context?.Key != WaveQueueKey ||
            waveTaste.Mode == WaveMode.Favorites || !isPlaying.Value || playbackLoading.Value)
        { wavePreviewCancellation?.Cancel(); return; }
        if (wavePreviewSession != waveListening.SessionId)
        { ResetWaveCandidateAnalysis(); wavePreviewSession = waveListening.SessionId; }
        // A cancelled batch still owns its decoder until its awaited shutdown completes.
        if (wavePreviewAnalysis is { IsCompleted: false } || waveTime.Elapsed < wavePreviewNextBatch ||
            wavePreviewAttempts.Count >= 24 || wavePreviewBudget.Remaining == 0) return;
        var upcoming = playbackQueue.Snapshot.ContextUpcoming.Select(entry => entry.Track);
        var candidates = upcoming.Concat(waveTaste.Rank(waveCandidates.Values, WavePlayed(), 40,
                DateTimeOffset.UtcNow, reserved: WaveReserved())).DistinctBy(track => track.Id)
            .Where(track => !wavePreviewAttempts.Contains(track.Id) && !waveTaste.IsBlocked(track) &&
                track.Id != current?.Id && waveLearning?.Vectors.GetValueOrDefault(track.Id)?.Model != MusicnnEncoder.ModelId)
            .Take(Math.Min(3, 24 - wavePreviewAttempts.Count)).ToArray();
        if (candidates.Length == 0) return;
        wavePreviewNextBatch = waveTime.Elapsed + TimeSpan.FromSeconds(20);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(12)); wavePreviewCancellation = cancellation;
        Func<SoundCloudTrack, HttpClient, CancellationToken, Task<AudioSample?>> capture = CaptureWaveCandidateAsync;
        CustomizeWaveCandidateCapture(ref capture);
        wavePreviewAnalysis = AnalyzeWaveCandidateBatchAsync(candidates, waveAccount, waveGeneration,
            wavePreviewSession.Value, wavePreviewBudget, cancellation, capture);
        waveJournalWrites.RemoveAll(task => task.IsCompleted); waveJournalWrites.Add(wavePreviewAnalysis);
        var batch = wavePreviewAnalysis;
        Run(async () => await batch);
    }

    private async Task AnalyzeWaveCandidateBatchAsync(SoundCloudTrack[] candidates, long account, long generation,
        Guid session, PreviewDownloadBudget budget, CancellationTokenSource cancellation,
        Func<SoundCloudTrack, HttpClient, CancellationToken, Task<AudioSample?>> capture)
    {
        await Task.Yield(); // Publish batch ownership before even an instant fixture can complete.
        bool Current() => !disposed && account == waveAccount && generation == waveGeneration &&
            session == waveListening.SessionId && playbackQueue.Context?.Key == WaveQueueKey &&
            waveTaste.Mode != WaveMode.Favorites && isPlaying.Value && !cancellation.IsCancellationRequested;
        try
        {
            using var http = new HttpClient(new PreviewBudgetHandler(budget, new HttpClientHandler { AllowAutoRedirect = false }));
            foreach (var track in candidates)
            {
                if (!Current() || budget.Remaining == 0) break;
                if (track.Id == current?.Id || waveTaste.IsBlocked(track) ||
                    waveLearning?.Vectors.GetValueOrDefault(track.Id)?.Model == MusicnnEncoder.ModelId) continue;
                wavePreviewAttempts.Add(track.Id);
                using var perTrack = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                perTrack.CancelAfter(TimeSpan.FromSeconds(6));
                try
                {
                    var sample = await capture(track, http, perTrack.Token);
                    if (sample != null && Current() && !perTrack.IsCancellationRequested)
                    { EnsureWaveJournal(); await AnalyzeWaveAudioAsync(account, generation, track, sample, session: session); }
                }
                catch (OperationCanceledException) when (perTrack.IsCancellationRequested) { }
                catch (Exception error) when (error is SoundCloudException or HttpRequestException or IOException or InvalidOperationException or
                    System.Text.Json.JsonException or NotSupportedException)
                {
                    if (Current()) RememberUnavailableWaveTrack(track, error);
                    Pookie.Logging.AppLog.For("Pookie.Wave").Debug("Фоновый анализ кандидата {TrackId} пропущен: {ErrorType}", track.Id, error.GetType().Name);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(wavePreviewCancellation, cancellation)) wavePreviewCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task<AudioSample?> CaptureWaveCandidateAsync(SoundCloudTrack track, HttpClient http, CancellationToken token)
    {
        // Ordinary excerpts only: never start the website player, license exchange or history reporting.
        var resolved = await new SoundCloudStreamResolver(api).ResolveAsync(track.Id, token, ["progressive", "hls"]);
        var stream = resolved.Stream;
        if (stream.Protected) return null;
        var source = new AudioSource(stream.Uri.AbsoluteUri,
            stream.Protocol == "hls" ? AudioTransport.Hls : AudioTransport.Progressive, stream.Duration);
        return await AudioPreviewSampler.CaptureAsync(http, source, SoundCloudWebClient.IsMediaUri, token);
    }

    partial void CustomizeWaveCandidateCapture(ref Func<SoundCloudTrack, HttpClient, CancellationToken, Task<AudioSample?>> capture);
}
