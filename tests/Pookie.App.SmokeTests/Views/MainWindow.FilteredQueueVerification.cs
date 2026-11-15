using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyFilteredQueueUiAsync()
    {
        var previousPlayer = player; var previousLoader = queueLoader;
        timer.Stop(); player = new QueueVerificationPlayer();
        try
        {
            await ShowWorkspaceAsync();
            Window.WindowSize = WindowSize.Resizable(1160, 1000, minWidth: 1000, minHeight: 680);
            SoundCloudTrack Track(long id, string title, string author) => new()
            { Id = id, Title = title, Duration = 12000, User = new() { Id = 7, Username = author } };
            var data = new[] { Track(1, "Другой трек", "Другой артист"), Track(2, "Первая", "T-Fest"),
                Track(3, "Ещё один", "Другой артист"), Track(4, "Вторая T-Fest", "Исполнитель"), Track(5, "Третья", "T-Fest") };
            shuffle.Value = false; playbackQueue.SetRepeat(RepeatMode.Off); playbackQueue.SetAutoContinue(false);
            foreach (var mode in Enum.GetValues<TrackViewMode>())
            {
                current = null; likedFilter.Text = "";
                libraryLikes = new(data, null); ShowLibraryTracks();
                likesMode.Value = mode; likedFilter.Text = " t-fEST ";
                await ClickFilteredTrackAsync(mode, collection: false, 4);
                await WaitForLikedLayoutAsync(() => current?.Id == 4 && audioReady);
                AssertSource([2, 4, 5], 3);
                await SkipAsync(1);
                if (current?.Id != 5) throw new InvalidOperationException("Library Next left the filtered results.");
                await SkipAsync(-1);
                if (current?.Id != 4) throw new InvalidOperationException("Library Previous lost the filtered history.");
                var version = playbackQueue.SourceVersion;
                likedFilter.Text = "";
                if (version != playbackQueue.SourceVersion) throw new InvalidOperationException("Editing the filter replaced a playing queue.");
                AssertSource([2, 4, 5], 3);
                await ClickFilteredTrackAsync(mode, collection: false, 4);
                await WaitForLikedLayoutAsync(() => playbackQueue.SourceVersion != version && audioReady);
                AssertSource([1, 2, 3, 4, 5], 3);
                if (playbackQueue.Context?.Filter != null) throw new InvalidOperationException("Cleared library filter was retained on reselection.");
                if (mode == TrackViewMode.Waveform)
                {
                    likedFilter.Text = "T-Fest";
                    await SeekLikedTrackAsync(data[3], .25);
                    AssertSource([2, 4, 5], 3);
                }
            }

            var playlist = new SoundCloudPlaylist { Id = 11, Title = "Filtered playlist", TrackCount = 5, Tracks = data,
                User = new() { Id = 7, Username = "Fixture" } };
            var item = LibraryData.FromPlaylist(playlist);
            var duplicates = new[] { data[0], data[1], data[2], data[3], data[1] };
            BeginNavigation(new(Page.LibraryCollection, Item: item));
            collectionDetails[item.Key] = new(item, new(duplicates, null));
            activeCollection = new(duplicates.Select(LibraryItem.FromTrack).ToArray(), null);
            librarySectionTitle.Value = item.Title; RenderLibrarySection();
            foreach (var mode in Enum.GetValues<TrackViewMode>())
            {
                current = null; collectionMode.Value = mode; collectionFilter.Text = "T-Fest";
                RefreshCollectionTracks();
                await ClickFilteredTrackAsync(mode, collection: true, 2, position: 4);
                await WaitForLikedLayoutAsync(() => current?.Id == 2 && audioReady);
                AssertSource([2, 4, 2], 4);
                playbackQueue.SetRepeat(RepeatMode.Context);
                await SkipAsync(1);
                AssertSource([2, 4, 2], 1);
                if (mode == TrackViewMode.Waveform)
                {
                    await SeekCollectionTrackAsync(data[1], 4, .25);
                    AssertSource([2, 4, 2], 4);
                }
                playbackQueue.SetRepeat(RepeatMode.Off);
                var version = playbackQueue.SourceVersion;
                collectionFilter.Text = "";
                if (version != playbackQueue.SourceVersion) throw new InvalidOperationException("Collection filter changed an existing queue.");
                await PlayDisplayedCollectionAsync();
                AssertSource([1, 2, 3, 4, 2], 0);
                collectionFilter.Text = "ничего не найдено";
                await WaitForLoginFrameAsync();
                version = playbackQueue.SourceVersion;
                await PlayDisplayedCollectionAsync();
                if (collectionPlay.IsEnabled || version != playbackQueue.SourceVersion)
                    throw new InvalidOperationException("Empty collection results started unrelated tracks.");
            }

            // More than four unmatching pages must not end a filtered queue.
            var calls = 0;
            using var loader = new PlaybackQueueLoader(playbackQueue, (context, cursor, token) =>
            {
                if (context.Filter != "T-Fest") throw new InvalidOperationException("Pagination lost its captured filter.");
                ++calls;
                return Task.FromResult(new TrackPage(calls < 7 ? [data[0]] : [data[3]], calls < 7 ? "page" + calls : null));
            }, RefreshQueue, lifetime.Token);
            queueLoader = loader;
            StartQueueContext(new("filtered-pages", "T-Fest", "Фильтр", "page0", Filter: "T-Fest"), [data[1]], data[1]);
            await PlayAsync(data[1]);
            BeginNavigation(new(Page.Home));
            likedFilterText = "Другой"; collectionFilterText = "Другой";
            await SkipAsync(1);
            if (calls != 7 || current?.Id != 4 || playbackQueue.Current?.SourceIndex != 7 || playbackQueue.Context?.Filter != "T-Fest")
                throw new InvalidOperationException("Filtered pagination stopped early or followed the newly visible page/filter.");
            Console.WriteLine("FILTERED_QUEUE_UI_OK: real play clicks in all three library/playlist views, original duplicate positions, next/previous, seek, changed/empty filters, repeat, and seven-page continuation after navigation");
        }
        finally { player = previousPlayer; queueLoader = previousLoader; }

        void AssertSource(long[] expected, int selected)
        {
            if (!playbackQueue.Snapshot.Source.Select(e => e.Track.Id).SequenceEqual(expected) || playbackQueue.Current?.SourceIndex != selected)
                throw new InvalidOperationException($"Filtered source mismatch: ids={string.Join(',', playbackQueue.Snapshot.Source.Select(e => e.Track.Id))}, position={playbackQueue.Current?.SourceIndex}, expected={string.Join(',', expected)}/{selected}.");
        }
    }

    private async Task ClickFilteredTrackAsync(TrackViewMode mode, bool collection, long id, int? position = null)
    {
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
        shellScroll.SetScrollOffsets(0, SmoothScroll.Maximum(shellScroll));
        Button? button = null; Image? cover = null;
        await WaitForLikedLayoutAsync(() =>
        {
            button = null; cover = null;
            if (collection)
            {
                if (mode == TrackViewMode.Cards && collectionTrackTiles.Values.FirstOrDefault(e => e.Tile.Track?.Id == id && e.Position == position) is { Tile: { } tile })
                { button = tile.PlaybackButton; cover = tile.Cover; }
                if (mode == TrackViewMode.Waveform && collectionWaveRows.Values.FirstOrDefault(e => e.Row.Track?.Id == id && e.Position == position) is { Row: { } row }) button = row.PlayButton;
                if (mode == TrackViewMode.List && compactTrackRows.FirstOrDefault(r => collectionTrackList.IsAncestorOf(r.Root) && r.Track?.Id == id && r.SourceIndex == position) is { } compact)
                { button = compact.PlaybackButton; cover = compact.Cover; }
            }
            else
            {
                if (mode == TrackViewMode.Cards && likedTiles.Values.FirstOrDefault(t => t.Track?.Id == id) is { } tile)
                { button = tile.PlaybackButton; cover = tile.Cover; }
                if (mode == TrackViewMode.Waveform && likedRows.Values.FirstOrDefault(r => likedList.IsAncestorOf(r.Root) && r.Track?.Id == id) is { } row) button = row.PlayButton;
                if (mode == TrackViewMode.List && compactTrackRows.FirstOrDefault(r => likedCompactList.IsAncestorOf(r.Root) && r.Track?.Id == id) is { } compact)
                { button = compact.PlaybackButton; cover = compact.Cover; }
            }
            return button is { ActualWidth: > 0, ActualHeight: > 0 } && button.Bounds.IntersectsWith(shellScroll.Bounds);
        });
        RouteWaveformPointer(cover?.Bounds.Center ?? button!.Bounds.Center);
        if (cover != null) await WaitForLikedLayoutAsync(() => button!.IsHitTestVisible);
        RouteWaveformClick(button!.Bounds.Center);
    }
}
