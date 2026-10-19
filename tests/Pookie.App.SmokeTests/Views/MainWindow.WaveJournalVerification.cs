using Pookie.Recommendations;
using Pookie.Recommendations.Storage;
using Pookie.SoundCloud;
using Pookie.App.Playback;
using Pookie.Audio;
using Pookie.Recommendations.Audio;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyWaveJournalOnlyUiAsync()
    {
        var originalPlayer = player;
        try
        {
            player = null;
            api.Session = new("journal-fixture", "fixture-token", "Test");
            me = new() { Id = 42 }; signedIn.Value = true; EnsureWaveTaste();
            var fixtureTransport = new HomeFixtureTransport(); fixtureTransport.RelatedReady.SetResult();
            api.BrowserTransport = fixtureTransport;
            await ShowWorkspaceAsync();
            var track = new SoundCloudTrack { Id = 8001, Title = "Dislike fixture", Duration = 180000 };
            playbackQueue.StartContext(new(WaveQueueKey, "Wave", "", Kind: PlaybackContextKind.Wave), [track], 0);
            current = track;
            playerVisible.Value = true; UpdateLikeState();
            await WaitForLikedLayoutAsync(() => playerDislikeButton.ActualWidth > 20 && playerChrome.ActualHeight > 70);
            ClickHomeElement(playerDislikeButton);
            await WaitForLikedLayoutAsync(() => waveTaste.IsBlocked(track));
            CaptureUiPreview("player-dislike");
            playbackQueue.StartContext(new("fixture:playlist", "Playlist", "", Kind: PlaybackContextKind.Playlist), [track], 0);
            current = track; UpdateLikeState();
            await RejectWaveTrackAsync();
            if (waveTaste.IsBlocked(track) || playbackQueue.Current?.Track.Id != track.Id)
                throw new InvalidOperationException("Dislike cancellation outside Wave did not restore the track or changed the playlist.");
            api.BrowserTransport = new HomeFixtureTransport(); likedIdsReady = true;
            likedIds.Add(track.Id); waveTaste.Import(track, true, true); UpdateLikeState();
            await RejectWaveTrackAsync();
            if (!isDisliked.Value || isLiked.Value || likedIds.Contains(track.Id) || playbackQueue.Current?.Track.Id != track.Id)
                throw new InvalidOperationException("Dislike outside Wave was not exclusive with Like or changed the playlist.");
            await ToggleTrackLikeAsync(track);
            if (isDisliked.Value || !isLiked.Value || waveTaste.IsBlocked(track))
                throw new InvalidOperationException("Liking a rejected track did not cancel its exclusion.");
            me = new() { Id = 43 }; ClearWaveData();
            await LoadWaveLearningAsync(); // An in-flight refill may resume after the account was cleared.
            await VerifyWaveJournalAsync();
        }
        catch (Exception error)
        {
            VerificationFailure = error;
            Console.Error.WriteLine($"WAVE_JOURNAL_FAILED: {error}");
        }
        finally { player = originalPlayer; Window.Close(); }
    }

    private async Task VerifyWaveJournalAsync()
    {
        await Task.WhenAll(waveJournalWrites);
        using (var oldAccount = new ListeningEventStore(waveJournalPath))
            if (!oldAccount.Read(42).Any(row => row.Event.Kind == WaveEventKind.Block))
                throw new InvalidOperationException("The native dislike action did not reach the journal.");

        EnsureWaveTaste();
        var track = new SoundCloudTrack { Id = 9001, Title = "Journal fixture", Duration = 180000,
            User = new() { Id = 8, Username = "Fixture" }, TrackAuthorization = "PRIVATE_STREAM_TOKEN" };
        var time = waveTime.Elapsed;
        waveListening.Begin(track, true, time, started: DateTimeOffset.UtcNow.AddSeconds(-2));
        waveListening.Poll(0, true, time);
        waveListening.Poll(1, true, time.Add(TimeSpan.FromSeconds(1)));
        waveListening.Poll(2, true, time.Add(TimeSpan.FromSeconds(2)));
        var previousPlayer = player;
        var analysisPlayer = new AnalysisFixturePlayer();
        try { player = analysisPlayer; FinishWaveListening(reason: ListeningEndReason.Shutdown); }
        finally { player = previousPlayer; }
        WaveLikeChanged(track, true); WaveLikeChanged(track, false); ResetWaveBlocks();
        await Task.WhenAll(waveJournalWrites);
        using var reader = new ListeningEventStore(waveJournalPath);
        var events = reader.Read(43).Select(row => row.Event).ToArray();
        var play = events.Single(item => item.Kind == WaveEventKind.Listening).Listening!;
        if (play.ListenedSeconds != 2 || play.EndReason != ListeningEndReason.Shutdown || play.EarlySkip ||
            events.Count(item => item.Kind == WaveEventKind.Like) != 1 ||
            events.Count(item => item.Kind == WaveEventKind.Unlike) != 1 ||
            events.Count(item => item.Kind == WaveEventKind.ResetBlocks) != 1 ||
            reader.Read(42).Any(item => item.Event.Track?.Id == 9001))
            throw new InvalidOperationException("Listening/actions were duplicated, misclassified or assigned to another account.");
        var learned = reader.Snapshot(43, [Pookie.App.Home.WaveTrack.From(track)]);
        if (analysisPlayer.Takes != 1 || learned.Model.Samples != 1 || learned.Model.PositiveAudio.Length != 1 || learned.Vectors.Count != 1)
            throw new InvalidOperationException("PCM analysis did not reach the neural vector index and learned account profile.");
        VerifyLearnedWaveQueue();
        await VerifyWaveCandidateAnalysisAsync();
        await VerifyWaveAvailabilityAsync();
        Console.WriteLine("WAVE_JOURNAL_OK: native actions, listening, account switch, committed writes, neural audio vector, learned profile");
    }

    private void VerifyLearnedWaveQueue()
    {
        var first = new SoundCloudTrack { Id = 9201, Title = "Acoustic match", Duration = 180000, User = new() { Username = "A" } };
        var second = first with { Id = 9202, Title = "Different sound", User = new() { Username = "B" } };
        var anchor = first with { Id = 9203, Title = "Current", User = new() { Username = "C" } };
        waveCandidates.Clear(); AddWaveCandidates([first, second]);
        playbackQueue.StartContext(new(WaveQueueKey, "Wave", "", "pookie:wave:fixture", Kind: PlaybackContextKind.Wave), [anchor, second, first], 0);
        current = anchor; var manual = playbackQueue.Enqueue(anchor with { Id = 9204, Title = "Manual" });
        var positive = new float[200]; positive[0] = 1;
        var negative = new float[200]; negative[1] = 1;
        var model = Pookie.Recommendations.Learning.RankerState.Empty() with { Sequence = 100, Samples = 20,
            AudioModel = MusicnnEncoder.ModelId, PositiveAudio = [positive], NegativeAudio = negative };
        model.Weights[4] = 5; model.Weights[5] = -5;
        var snapshot = new RecommendationSnapshot(model, new() { [first.Id] = new(MusicnnEncoder.ModelId, positive),
            [second.Id] = new(MusicnnEncoder.ModelId, negative) }, [], []);
        var committed = playbackQueue.Snapshot.ContextUpcoming.ToArray();
        ApplyWaveLearning(43, waveGeneration, snapshot);
        if (!committed.SequenceEqual(playbackQueue.Snapshot.ContextUpcoming))
            throw new InvalidOperationException("Learning replaced already displayed wave entries.");
        RerankWaveQueue(rebuild: true);
        if (playbackQueue.Snapshot.ContextUpcoming.First().Track.Id != first.Id || playbackQueue.Current?.Track.Id != anchor.Id ||
            playbackQueue.Snapshot.ManualUpcoming.Single() != manual)
            throw new InvalidOperationException("Learned audio scores did not rerank the native queue while retaining current/manual entries.");
        var reversed = snapshot with { Model = model with { Sequence = 101, PositiveAudio = [negative], NegativeAudio = positive } };
        ApplyWaveLearning(43, waveGeneration, reversed);
        ApplyWaveLearning(43, waveGeneration, snapshot);
        if (playbackQueue.Snapshot.ContextUpcoming.First().Track.Id != first.Id)
            throw new InvalidOperationException("A background preference update reordered the committed wave.");
        RerankWaveQueue(rebuild: true);
        if (playbackQueue.Snapshot.ContextUpcoming.First().Track.Id != second.Id || waveLearning?.Model.Sequence != 101)
            throw new InvalidOperationException("A newer learned preference did not change the queue or a stale checkpoint overwrote it.");
        var cursor = playbackQueue.Context!.NextHref;
        ApplyWaveLearning(43, waveGeneration, reversed, rerank: false);
        if (playbackQueue.Context.NextHref != cursor) throw new InvalidOperationException("Snapshot refresh invalidated the in-flight refill cursor.");
        ApplyWaveLearning(42, waveGeneration, snapshot with { Model = model with { Sequence = 102 } });
        if (waveLearning?.Model.Sequence != 101) throw new InvalidOperationException("Another account replaced the active learned model.");
        var withMoods = reversed with { Vectors = new() {
            [first.Id] = new(MusicnnEncoder.ModelId, positive, new(.02f, .95f, .1f, .7f, .6f, .5f, Pookie.Recommendations.AudioMood.AnalysisVersion)),
            [second.Id] = new(MusicnnEncoder.ModelId, negative, new(.9f, .2f, .1f, .05f, .6f, .99f, Pookie.Recommendations.AudioMood.AnalysisVersion)) } };
        ApplyWaveLearning(43, waveGeneration, withMoods);
        if (playbackQueue.Snapshot.ContextUpcoming.First().Track.Id != second.Id ||
            playbackQueue.Snapshot.ContextUpcoming.Count() != 2 ||
            playbackQueue.Current?.Track.Id != anchor.Id || playbackQueue.Snapshot.ManualUpcoming.Single() != manual ||
            waveLearning?.Model.Sequence != 101)
            throw new InvalidOperationException("Mood metadata changed the base recommendations or disturbed current/manual entries.");
        ApplyWaveLearning(43, waveGeneration, withMoods with { Vectors = new() {
            [first.Id] = new(MusicnnEncoder.ModelId, negative), [second.Id] = new(MusicnnEncoder.ModelId, positive) } });
        if (playbackQueue.Snapshot.ContextUpcoming.First().Track.Id != second.Id)
            throw new InvalidOperationException("Background sound analysis reordered a displayed recommendation.");
        RerankWaveQueue(rebuild: true);
        if (playbackQueue.Snapshot.ContextUpcoming.First().Track.Id != first.Id ||
            playbackQueue.Current?.Track.Id != anchor.Id || playbackQueue.Snapshot.ManualUpcoming.Single() != manual)
            throw new InvalidOperationException("New sound vectors did not rerank at the same training checkpoint.");
        Console.WriteLine("WAVE_LIVE_RANKING_OK: displayed wave stays committed during learning; explicit rebuild uses latest sound; mood-independent; current/manual preserved; stale/account snapshots ignored; refill cursor stable");
    }

    private sealed class AnalysisFixturePlayer : IAudioPlayer
    {
        public int Takes { get; private set; }
        public AudioSample? TakeAnalysisSample()
        {
            Takes++;
            return new(Enumerable.Range(0, 48000).Select(i => (float)(.2 * Math.Sin(2 * Math.PI * 220 * i / 16000))).ToArray());
        }
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop() { }
        public void Pause(bool paused) { }
        public void Volume(double percent) { }
        public AudioState Poll() => new(0, 0, false, false, false);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
