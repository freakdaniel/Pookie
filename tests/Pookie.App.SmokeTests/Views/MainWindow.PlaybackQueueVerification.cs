using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Playback;
using Pookie.Audio;
using Pookie.Media;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlaybackQueueAsync()
    {
        var originalPlayer = player;
        var originalLoader = queueLoader;
        timer.Stop(); player = new QueueVerificationPlayer();
        try
        {
            var data = Enumerable.Range(1, 8).Select(id => new SoundCloudTrack
                { Id = id, Title = "Queue fixture " + id, Duration = 12000, User = new() { Username = "Queue artist" } }).ToArray();
            shuffle.Value = false; playbackQueue.SetRepeat(RepeatMode.Off); RefreshRepeatState();
            StartQueueContext(new("likes", "Понравившиеся треки", "Сейчас играет из подборки"), data, data[0]);
            await PlayAsync(data[0]);
            var first = playbackQueue.Current!;
            var duplicate = playbackQueue.Enqueue(data[0], next: true);
            var manual = playbackQueue.Enqueue(data[1]); RefreshQueue(); queueOpen.Value = true;
            RouteWaveformPointer(new Point(2, 2));
            await WaitForLikedLayoutAsync(() => compactTrackRows.Count(row => row.EntryId == duplicate.EntryId && queueList.IsAncestorOf(row.Root)) == 1 &&
                compactTrackRows.Any(row => row.EntryId == first.EntryId && queueList.IsAncestorOf(row.Root) && row.Overlay.Opacity > .999));
            var duplicateRow = compactTrackRows.Single(row => row.EntryId == duplicate.EntryId && queueList.IsAncestorOf(row.Root));
            var currentRow = compactTrackRows.Single(row => row.EntryId == first.EntryId && queueList.IsAncestorOf(row.Root));
            if (duplicateRow.Root.ContextMenu == null || currentRow.Overlay.Opacity < .99 || duplicateRow.Overlay.Opacity > .01)
                throw new InvalidOperationException("Duplicate queue occurrences shared active state or lacked editing actions.");
            async Task InvokeQueueMenu(CompactTrackRow row, string name)
            {
                var command = row.Root.ContextMenu!.Items.OfType<MenuItem>().Single(item => item.Command!.Id == "pookie.queue." + name).Command!;
                if (!await Window.CommandRouter.ExecuteAsync(command, CommandTarget.From(row.Root), row.Root, lifetime.Token))
                    throw new InvalidOperationException("Queue menu command did not resolve against its track row: " + name);
            }
            await InvokeQueueMenu(duplicateRow, "down");
            if (playbackQueue.Snapshot.ManualUpcoming[1].EntryId != duplicate.EntryId)
                throw new InvalidOperationException("Move-down menu did not reorder the selected occurrence.");
            await WaitForLoginFrameAsync();
            duplicateRow = compactTrackRows.Single(row => row.EntryId == duplicate.EntryId && queueList.IsAncestorOf(row.Root));
            await InvokeQueueMenu(duplicateRow, "up");
            if (playbackQueue.Snapshot.ManualUpcoming[0].EntryId != duplicate.EntryId)
                throw new InvalidOperationException("Move-up menu did not restore the selected occurrence.");
            var shown = expandedQueueData.Where(block => block.Kind == QueueBlockKind.Track).Select(block => block.EntryId).ToArray();
            var expected = new[] { first.EntryId }.Concat(playbackQueue.Snapshot.Upcoming.Select(entry => entry.EntryId)).Cast<long?>().ToArray();
            if (!shown.SequenceEqual(expected)) throw new InvalidOperationException("Queue view and actual next order differ.");
            await HandleMediaCommandAsync(new(MediaAction.Next));
            if (playbackQueue.Current?.EntryId != duplicate.EntryId || current?.Id != duplicate.Track.Id)
                throw new InvalidOperationException("System Next skipped a manually added duplicate.");
            ChangeManualQueue(() => playbackQueue.RemoveManual(manual.EntryId));
            updatingProgress = true; progress.Value = 4; updatingProgress = false;
            await SkipAsync(-1);
            await WaitForLikedLayoutAsync(() => !seeking);
            if (playbackQueue.Current.EntryId != duplicate.EntryId || ((QueueVerificationPlayer)player).Seeks != 1)
                throw new InvalidOperationException("Previous after three seconds changed the queue instead of restarting the track.");
            updatingProgress = true; progress.Value = 0; updatingProgress = false;
            await HandleMediaCommandAsync(new(MediaAction.Previous));
            if (playbackQueue.Current?.EntryId != first.EntryId) throw new InvalidOperationException("Previous lost actual history.");
            await HandleMediaCommandAsync(new(MediaAction.Next));
            manual = playbackQueue.Enqueue(data[1]); RefreshQueue();
            await HandleMediaCommandAsync(new(MediaAction.Next));
            if (playbackQueue.Current?.EntryId != manual.EntryId) throw new InvalidOperationException("Forward history consumed or duplicated a manual entry.");

            shuffle.Value = true;
            var order = playbackQueue.Snapshot.Upcoming.Select(entry => entry.EntryId).ToArray();
            RefreshQueue();
            if (!expandedUpcoming.Select(track => track.Id).SequenceEqual(playbackQueue.Snapshot.Upcoming.Select(entry => entry.Track.Id)))
                throw new InvalidOperationException("Fullscreen queue ignored Shuffle order.");
            await HandleMediaCommandAsync(new(MediaAction.Next));
            if (playbackQueue.Current?.EntryId != order[0]) throw new InvalidOperationException("Shuffle displayed a different next entry from system playback.");
            var sourceVersion = playbackQueue.SourceVersion;
            await NavigateAsync(Page.Feed);
            if (sourceVersion != playbackQueue.SourceVersion) throw new InvalidOperationException("Page navigation replaced the playback source.");

            // A single search result stops at its end; natural Repeat Track and
            // manual Next have separate behavior, both using the same model.
            page.Value = Page.Search; SetQueue(data[0]); await PlayAsync(data[0]);
            if (playbackQueue.Snapshot.Source.Count != 1 || playbackQueue.Snapshot.CanNext)
                throw new InvalidOperationException("Search selection included unrelated loaded results.");
            await SkipAsync(1, naturalEnd: true);
            if (isPlaying.Value || audioReady || playbackQueue.Preparation != PlaybackPreparation.Stopped)
                throw new InvalidOperationException("Source wrapped instead of stopping with Repeat off.");
            CycleRepeat(); CycleRepeat(); await ToggleAsync();
            var repeated = playbackQueue.Current.EntryId;
            EnqueueTrack(data[1], true);
            await SkipAsync(1, naturalEnd: true);
            if (playbackQueue.Current.EntryId != repeated) throw new InvalidOperationException("Repeat Track consumed upcoming entries.");
            await SkipAsync(1);
            if (current?.Id != data[1].Id) throw new InvalidOperationException("Manual Next could not escape Repeat Track.");

            shuffle.Value = false; playbackQueue.SetRepeat(RepeatMode.Off);
            page.Value = Page.LibraryCollection;
            activeCollection = new(new[] { data[0], data[1], data[0] }.Select(LibraryItem.FromTrack).ToArray(), null);
            await OpenLibraryItemAsync(activeCollection.Items[2], 2);
            if (playbackQueue.Current?.SourceIndex != 2 || playbackQueue.Snapshot.ContextUpcoming.Count != 0)
                throw new InvalidOperationException("Selecting a duplicate in a playlist started its first occurrence.");

            var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loaded = new TaskCompletionSource<TrackPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var fixtureLoader = new PlaybackQueueLoader(playbackQueue, (_, _, _) => { requested.TrySetResult(); return loaded.Task; }, RefreshQueue, lifetime.Token);
            queueLoader = fixtureLoader;
            shuffle.Value = false; playbackQueue.SetRepeat(RepeatMode.Off);
            StartQueueContext(new("paged", "Плейлист", "Сейчас играет из плейлиста", "page2"), [data[0]], data[0]);
            await PlayAsync(data[0]); await requested.Task;
            var delayedNext = SkipAsync(1);
            await PlayAsync(data[0]);
            await NavigateAsync(Page.Home);
            loaded.SetResult(new([data[1]], null));
            await fixtureLoader.EnsureAheadAsync();
            await delayedNext;
            if (current?.Id != data[0].Id) throw new InvalidOperationException("A delayed Next replaced a newer track selection.");
            await SkipAsync(1);
            if (current?.Id != data[1].Id || playbackQueue.Context?.Title != "Плейлист")
                throw new InvalidOperationException("Pagination followed the visible page rather than the playback context.");
            Console.WriteLine("UI_PLAYBACK_QUEUE_OK: duplicate occurrence state and editing menus, manual priority, three-second Previous, actual back/forward history, stable Shuffle, displayed/system next parity, isolated search playback, repeat modes, finite end and source pagination across navigation");
        }
        finally { player = originalPlayer; queueLoader = originalLoader; }
    }

    private sealed class QueueVerificationPlayer : IAudioPlayer
    {
        public int Seeks { get; private set; }
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public AudioState Poll() => new(0, 12, true, false, false);
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) { Seeks++; return Task.CompletedTask; }
        public void Pause(bool pause) { }
        public void Volume(double volume) { }
        public void Stop() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
