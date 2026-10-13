using Pookie.Audio;
using Pookie.Recommendations;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Task? wavePreviewAnalysis;
    private CancellationTokenSource? wavePreviewCancellation;
    private Guid? wavePreviewSession;
    private readonly HashSet<long> wavePreviewAttempts = [];

    private void ResetWaveCandidateAnalysis()
    {
        wavePreviewCancellation?.Cancel();
        wavePreviewCancellation?.Dispose();
        wavePreviewCancellation = null;
        wavePreviewSession = null;
        wavePreviewAttempts.Clear();
    }

    private Task AnalyzeWaveCandidatesAsync(CancellationToken token)
    {
        if (demo || disposed || waveAccount <= 0 || waveTaste.Mood == WaveMood.Auto) return Task.CompletedTask;
        // One decoder at a time, three excerpts per refill, at most 24 attempts per listening session.
        if (wavePreviewAnalysis is { IsCompleted: false }) return wavePreviewAnalysis.WaitAsync(token);
        if (wavePreviewSession != waveListening.SessionId)
        {
            ResetWaveCandidateAnalysis();
            wavePreviewSession = waveListening.SessionId;
        }
        if (wavePreviewAttempts.Count >= 24) return Task.CompletedTask;
        var tracks = waveTaste.Rank(waveCandidates.Values, WavePlayed(), 60, DateTimeOffset.UtcNow, reserved: WaveReserved())
            .Where(track => !wavePreviewAttempts.Contains(track.Id) && waveLearning?.Vectors.GetValueOrDefault(track.Id)?.Mood?.IsCurrent != true)
            .Take(Math.Min(3, 24 - wavePreviewAttempts.Count)).ToArray();
        if (tracks.Length == 0) return Task.CompletedTask;
        foreach (var track in tracks) wavePreviewAttempts.Add(track.Id);
        wavePreviewCancellation?.Dispose();
        wavePreviewCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        wavePreviewCancellation.CancelAfter(TimeSpan.FromSeconds(10));
        EnsureWaveJournal();
        wavePreviewAnalysis = AnalyzeWaveCandidateBatchAsync(tracks, waveAccount, waveGeneration,
            wavePreviewSession.Value, wavePreviewCancellation.Token);
        waveJournalWrites.RemoveAll(task => task.IsCompleted);
        waveJournalWrites.Add(wavePreviewAnalysis);
        return wavePreviewAnalysis.WaitAsync(token);
    }

    private async Task AnalyzeWaveCandidateBatchAsync(SoundCloudTrack[] tracks, long account, long generation,
        Guid session, CancellationToken token)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        foreach (var track in tracks)
        {
            if (token.IsCancellationRequested || disposed || account != waveAccount || generation != waveGeneration || session != waveListening.SessionId) break;
            try
            {
                // Protected tracks are classified from actual playback, using its existing EME session.
                var resolved = await new SoundCloudStreamResolver(api).ResolveAsync(track.Id, token, ["progressive", "hls"]);
                var stream = resolved.Stream;
                var source = new AudioSource(stream.Uri.AbsoluteUri,
                    stream.Protocol == "hls" ? AudioTransport.Hls : AudioTransport.Progressive, stream.Duration);
                var sample = await AudioPreviewSampler.CaptureAsync(http, source, SoundCloudWebClient.IsMediaUri, token);
                if (sample != null && !token.IsCancellationRequested)
                    await AnalyzeWaveAudioAsync(account, generation, resolved.Track, sample, rerank: false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) when (error is SoundCloudException { StatusCode: not 401 } or HttpRequestException or IOException or InvalidOperationException)
            {
                Pookie.Logging.AppLog.For("Pookie.Wave").Debug(error, "Аудиооценка кандидата {TrackId} отложена", track.Id);
            }
        }
    }
}
