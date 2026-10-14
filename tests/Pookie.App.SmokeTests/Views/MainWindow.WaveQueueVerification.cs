using Aprillz.MewUI;
using Pookie.App.Playback;
using Pookie.Recommendations.Learning;
using Pookie.Recommendations.Storage;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyWaveQueueUiAsync()
    {
        var original = player;
        timer.Stop(); player = new QueueVerificationPlayer();
        try
        {
            var data = Enumerable.Range(1, 100).Select(id => new SoundCloudTrack { Id = 98000 + id,
                Title = "Трек волны " + id, Duration = 180000, User = new() { Id = id, Username = "Исполнитель " + id } }).ToArray();
            waveCandidates.Clear(); AddWaveCandidates(data);
            foreach (var track in data.Take(8)) waveTaste.Import(track, familiar: true, liked: true);
            shuffle.Value = false;
            StartQueueContext(new(WaveQueueKey, "Твоя волна", "", "pookie:wave:fixture", Kind: PlaybackContextKind.Wave), data, data[0]);
            await PlayAsync(data[0]);
            queueOpen.Value = true;
            await WaitForLikedLayoutAsync(() => popupQueueBlocks.Values.Any(block => block?.EntryId == playbackQueue.Current?.EntryId));
            await WaitForLikedLayoutAsync(() => !queueList.IsMeasureDirty && !queueList.IsArrangeDirty && !popupQueueBlocks.Keys.Any(row => row.Animating));
            var popupCommitted = playbackQueue.Snapshot.ContextUpcoming.ToArray();
            var popupKeys = popupQueueData.Select(block => block.Key).ToArray();
            for (var i = 1; i <= 10; i++)
                ApplyWaveLearning(waveAccount, waveGeneration, new RecommendationSnapshot(RankerState.Empty() with { Sequence = i }, [], [], []));
            if (!popupKeys.SequenceEqual(popupQueueData.Select(block => block.Key)) || popupQueueBlocks.Keys.Any(row => row.Animating))
                throw new InvalidOperationException("Background learning animated the popup queue.");
            var popupPositions = new HashSet<double>();
            void SamplePopup()
            {
                var row = popupQueueBlocks.FirstOrDefault(pair => pair.Value?.EntryId == popupCommitted[1].EntryId).Key;
                if (row != null) popupPositions.Add(Math.Round(row.VisualY, 1));
            }
            Window.FrameRendered += SamplePopup;
            try { await SkipAsync(1); await WaitForLikedLayoutAsync(() => !queueList.IsMeasureDirty && !queueList.IsArrangeDirty && !popupQueueBlocks.Keys.Any(row => row.Animating)); await WaitForLoginFrameAsync(); }
            finally { Window.FrameRendered -= SamplePopup; }
            if (popupPositions.Count < 3 || !popupCommitted.Skip(1).SequenceEqual(playbackQueue.Snapshot.ContextUpcoming.Take(2)))
                throw new InvalidOperationException($"Popup wave advance did not move its retained rows smoothly: retained={popupCommitted.Skip(1).SequenceEqual(playbackQueue.Snapshot.ContextUpcoming.Take(2))}, positions={string.Join(',', popupPositions)}.");
            var popupShown = popupQueueData.Where(block => block.Kind == QueueBlockKind.Track).Select(block => block.EntryId);
            if (!popupShown.SequenceEqual(new[] { playbackQueue.Current! }.Concat(playbackQueue.Snapshot.Upcoming).Select(entry => (long?)entry.EntryId)))
                throw new InvalidOperationException("Popup queue differs from the actual next order.");
            queueOpen.Value = false;
            expandedQueue.Height = 360;
            SetExpandedPlayer(true); ToggleExpandedPanel(PlayerPanel.Queue);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning && !panelAnimation.IsRunning && panelReveal > .999);
            await WaitForExpandedQueueSettledAsync();
            var committed = playbackQueue.Snapshot.ContextUpcoming.ToArray();
            var keys = expandedQueueData.Select(block => block.Key).ToArray();
            var changedAt = expandedQueueChangedAt;
            for (var i = 11; i <= 30; i++)
                ApplyWaveLearning(waveAccount, waveGeneration, new RecommendationSnapshot(RankerState.Empty() with { Sequence = i }, [], [], []));
            await WaitForLoginFrameAsync();
            if (!committed.SequenceEqual(playbackQueue.Snapshot.ContextUpcoming) ||
                !keys.SequenceEqual(expandedQueueData.Select(block => block.Key)) || expandedQueueChangedAt != changedAt ||
                expandedQueueBlocks.Keys.Any(row => row.Animating))
                throw new InvalidOperationException("Background learning changed or animated the displayed wave queue.");

            var manual = playbackQueue.Enqueue(data[99]); RefreshQueue();
            await WaitForExpandedQueueSettledAsync();
            if (!expandedQueueData.Any(block => block.Title == "Добавлено тобой") ||
                !expandedQueueData.Any(block => block.Title == "Далее на твоей волне"))
                throw new InvalidOperationException("Manual and wave entries lack separate headings.");
            await SkipAsync(1);
            if (playbackQueue.Current != manual || !committed.SequenceEqual(playbackQueue.Snapshot.ContextUpcoming))
                throw new InvalidOperationException("Playing a manual entry disturbed the committed wave horizon.");
            await WaitForExpandedQueueSettledAsync();

            var survivor = committed[1]; var positions = new HashSet<double>();
            void Sample()
            {
                var row = expandedQueueBlocks.FirstOrDefault(pair => pair.Value?.EntryId == survivor.EntryId).Key;
                if (row != null) positions.Add(Math.Round(row.VisualY, 1));
            }
            Window.FrameRendered += Sample;
            try { await SkipAsync(1); await WaitForExpandedQueueSettledAsync(); }
            finally { Window.FrameRendered -= Sample; }
            if (playbackQueue.Current?.EntryId != committed[0].EntryId ||
                !committed.Skip(1).SequenceEqual(playbackQueue.Snapshot.ContextUpcoming.Take(2)) ||
                playbackQueue.Snapshot.ContextUpcoming.Count != 3 || positions.Count < 3)
                throw new InvalidOperationException($"Wave advance did not retain/move its remaining rows and append one candidate: current={playbackQueue.Current?.EntryId}, expected={committed[0].EntryId}, retained={committed.Skip(1).SequenceEqual(playbackQueue.Snapshot.ContextUpcoming.Take(2))}, count={playbackQueue.Snapshot.ContextUpcoming.Count}, positions={string.Join(',', positions)}.");

            var replacementFrames = new HashSet<double>();
            void SampleReplacement()
            {
                var future = playbackQueue.Snapshot.ContextUpcoming.Select(entry => entry.EntryId).ToHashSet();
                foreach (var (row, block) in expandedQueueBlocks)
                    if (block?.EntryId is { } id && future.Contains(id))
                    {
                        if (Math.Abs(row.VisualY - row.Bounds.Y) > .5)
                            throw new InvalidOperationException("Explicit wave rebuild flew recommendation rows across the list.");
                        replacementFrames.Add(Math.Round(row.Opacity, 2));
                    }
            }
            Window.FrameRendered += SampleReplacement;
            try { SetWaveMode(Home.WaveMode.Favorites); await WaitForExpandedQueueSettledAsync(); }
            finally { Window.FrameRendered -= SampleReplacement; }
            if (playbackQueue.Snapshot.ContextUpcoming.Any(entry => !waveTaste.IsFamiliar(entry.Track)) ||
                replacementFrames.Count < 3)
                throw new InvalidOperationException("Mode change did not rebuild the short horizon with an opacity transition.");
            var unchanged = playbackQueue.Snapshot;
            SetWaveMode(Home.WaveMode.Favorites);
            if (!ReferenceEquals(unchanged, playbackQueue.Snapshot))
                throw new InvalidOperationException("Clicking the selected wave mode rebuilt its queue again.");
            SetWaveMode(Home.WaveMode.Discoveries); await WaitForExpandedQueueSettledAsync();
            for (var i = 0; i < 15; i++) { await SkipAsync(1); await WaitForExpandedQueueSettledAsync(); }
            expandedQueueScroll.SetScrollOffsets(0, 0); await WaitForLoginFrameAsync();
            var offset = expandedQueueScroll.VerticalOffset;
            await SkipAsync(1); await WaitForExpandedQueueSettledAsync();
            if (Math.Abs(expandedQueueScroll.VerticalOffset - offset) > 1)
                throw new InvalidOperationException("Wave advance pulled the viewport away from reading playback history.");
            var shown = expandedQueueData.Where(block => block.Kind == QueueBlockKind.Track).Select(block => block.EntryId);
            var actual = playbackQueue.Snapshot.History.Concat([playbackQueue.Current!]).Concat(playbackQueue.Snapshot.Upcoming).Select(entry => (long?)entry.EntryId);
            if (!shown.SequenceEqual(actual)) throw new InvalidOperationException("Displayed wave order differs from actual playback order.");
            expandedQueueStartPending = true; RefreshQueue();
            await WaitForExpandedQueueSettledAsync(); CaptureExpandedPanelPreview("wave-queue-stable");
            Console.WriteLine("WAVE_QUEUE_UI_OK: stable three-track horizon during learning; manual block; retained advance animation; mode dissolve; actual playback order; history viewport preserved");
            SetExpandedPlayer(false);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            await VerifyPlaybackQueueAsync();
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("WAVE_QUEUE_UI_FAILED: " + error); }
        finally { player = original; Window.Close(); }
    }
}
