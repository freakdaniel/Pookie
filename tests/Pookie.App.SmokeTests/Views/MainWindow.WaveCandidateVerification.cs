using Pookie.Audio;
using Pookie.Recommendations.Audio;
using Pookie.Recommendations.Storage;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Func<SoundCloudTrack, HttpClient, CancellationToken, Task<AudioSample?>>? waveCandidateFixtureCapture;
    partial void CustomizeWaveCandidateCapture(ref Func<SoundCloudTrack, HttpClient, CancellationToken, Task<AudioSample?>> capture)
        => capture = waveCandidateFixtureCapture ?? ((_, _, _) => Task.FromResult<AudioSample?>(null));

    private async Task VerifyWaveCandidateAnalysisAsync()
    {
        ResetWaveCandidateAnalysis();
        waveListening.NewSession(); waveTaste.SessionId = waveListening.SessionId;
        waveLearning = null; waveTaste.Learning = null;
        var anchor = new SoundCloudTrack { Id = 9400, Title = "Current", Duration = 180000 };
        var candidates = Enumerable.Range(1, 30).Select(id => anchor with { Id = 9400 + id, Title = "Candidate " + id }).ToArray();
        waveCandidates.Clear(); AddWaveCandidates(candidates);
        playbackQueue.StartContext(new(WaveQueueKey, "Wave", "", Kind: Pookie.App.Playback.PlaybackContextKind.Wave), [anchor, .. candidates], 0);
        current = anchor; isPlaying.Value = true; playbackLoading.Value = false;
        var manual = playbackQueue.Enqueue(anchor with { Id = 9499, Title = "Manual" });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captures = 0; var active = 0; var maximumActive = 0;
        waveCandidateFixtureCapture = async (track, _, token) =>
        {
            captures++; active++; maximumActive = Math.Max(maximumActive, active);
            if (track.Id is 9400 or 9499) throw new InvalidOperationException("Preview selected current/manual audio.");
            started.TrySetResult();
            try
            {
                await release.Task.WaitAsync(token);
                return new(Enumerable.Range(0, 48000).Select(i => (float)(.2 * Math.Sin(2 * Math.PI * 440 * i / 16000))).ToArray());
            }
            finally { active--; }
        };
        using var reader = new ListeningEventStore(waveJournalPath);
        var before = reader.Statistics().Events; var samples = reader.Snapshot(waveAccount, []).Model.Samples;
        ScheduleWaveCandidateAnalysis();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ScheduleWaveCandidateAnalysis();
        if (captures != 1 || wavePreviewAnalysis?.IsCompleted != false)
            throw new InvalidOperationException("Background preview blocked scheduling or ran overlapping decoders.");
        release.SetResult(); await wavePreviewAnalysis!;
        await WaitForLikedLayoutAsync(() => waveLearning?.Vectors.Keys.Count(id => id is >= 9401 and <= 9430) == 3);
        if (captures != 3 || maximumActive != 1 || playbackQueue.Current?.Track.Id != anchor.Id ||
            playbackQueue.Snapshot.ManualUpcoming.Single() != manual || reader.Statistics().Events != before ||
            reader.Snapshot(waveAccount, []).Model.Samples != samples)
            throw new InvalidOperationException("Preview changed listening/rewards/current/manual entries or ignored sequential batch bounds.");
        // Fast, unsuccessful fixtures exercise the session attempt limit without network or wall-clock waiting.
        waveCandidateFixtureCapture = (_, _, _) => { captures++; return Task.FromResult<AudioSample?>(null); };
        for (var attempt = 0; attempt < 10; attempt++)
        { wavePreviewNextBatch = TimeSpan.Zero; ScheduleWaveCandidateAnalysis(); if (wavePreviewAnalysis != null) await wavePreviewAnalysis; }
        if (captures != 24) throw new InvalidOperationException("Preview attempts exceeded or missed the session limit.");
        ResetWaveCandidateAnalysis(); waveListening.NewSession();
        started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        waveCandidateFixtureCapture = async (_, _, token) =>
        { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return null; };
        ScheduleWaveCandidateAnalysis(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        SetWaveMode(Pookie.App.Home.WaveMode.Favorites);
        await wavePreviewAnalysis!.WaitAsync(TimeSpan.FromSeconds(5));
        if (reader.Statistics().Events != before) throw new InvalidOperationException("Cancelled preview invented a listening event.");
        waveCandidateFixtureCapture = null; ResetWaveCandidateAnalysis();
        await VerifyDelayedWaveLikeIdentityAsync();
        Console.WriteLine("WAVE_CANDIDATE_ANALYSIS_OK: background sound cache, sequential batches, session limits, cancellation, no history/rewards, current/manual preserved");
    }

    private async Task VerifyDelayedWaveLikeIdentityAsync()
    {
        var transport = new DelayedWaveLikeTransport(); var originalTransport = api.BrowserTransport;
        api.BrowserTransport = transport; likedIdsReady = true;
        var track = new SoundCloudTrack { Id = 9500, Title = "Delayed like", Duration = 180000 };
        current = track; likedIds.Remove(track.Id);
        waveListening.Begin(track, true, waveTime.Elapsed, selection: Pookie.Recommendations.PlaybackSelection.QueueAdvance, familiarAtSelection: false);
        var originalPlayback = waveListening.PlaybackId;
        try
        {
            var pending = ToggleTrackLikeAsync(track);
            await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            FinishWaveListening(reason: Pookie.Recommendations.ListeningEndReason.Stopped);
            waveListening.Begin(track, true, waveTime.Elapsed, selection: Pookie.Recommendations.PlaybackSelection.QueueAdvance, familiarAtSelection: true);
            if (waveListening.PlaybackId == originalPlayback) throw new InvalidOperationException("Playback identity was reused.");
            transport.Release.SetResult(); await pending;
            await Task.WhenAll(waveJournalWrites);
            using var reader = new ListeningEventStore(waveJournalPath);
            var like = reader.Read(waveAccount).Single(row => row.Event.Track?.Id == track.Id && row.Event.Kind == Pookie.Recommendations.WaveEventKind.Like);
            if (like.Event.PlaybackId != originalPlayback) throw new InvalidOperationException("Delayed confirmation credited another play of the same song.");
            FinishWaveListening(reason: Pookie.Recommendations.ListeningEndReason.Shutdown);
        }
        finally { transport.Release.TrySetResult(); api.BrowserTransport = originalTransport; }
    }

    private sealed class DelayedWaveLikeTransport : ISoundCloudBrowserTransport
    {
        private readonly HomeFixtureTransport inner = new();
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DelayedWaveLikeTransport() => inner.RelatedReady.SetResult();
        public Task<System.Text.Json.JsonDocument> GetJsonAsync(Uri uri, CancellationToken token = default) => inner.GetJsonAsync(uri, token);
        public async Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken token = default)
        { Started.TrySetResult(); await Release.Task.WaitAsync(token); }
    }
}
