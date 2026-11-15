using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyGlobalPlaybackUiAsync()
    {
        var savedPlayer = player;
        timer.Stop(); player = new QueueVerificationPlayer();
        try
        {
            await ShowWorkspaceAsync();
            Window.WindowSize = WindowSize.Resizable(1160, 1000, minWidth: 1000, minHeight: 680);
            var data = Enumerable.Range(1, 3).Select(id => new SoundCloudTrack
            { Id = id, Title = "Recording " + id, Duration = 12000, User = new() { Id = 77, Username = "Artist" } }).ToArray();
            var cover = ImageSource.FromBytes(ArtworkFixtureBitmap(184));
            foreach (var track in data) libraryCoverCache[track.Id] = cover;
            api.Session = new("fixture", "fixture-token", "Test");
            api.BrowserTransport = new GlobalPlaybackTransport();
            me = new() { Id = 42, Username = "Fixture" };
            shuffle.Value = false; playbackQueue.SetAutoContinue(false); playbackQueue.SetRepeat(RepeatMode.Off);
            StartQueueContext(new("original-source", "Original source", "Fixture"), data, data[0]);
            await PlayAsync(data[0]);
            var version = playbackQueue.SourceVersion;
            var occurrence = playbackQueue.Current!.EntryId;

            foreach (var mode in Enum.GetValues<TrackViewMode>())
            {
                libraryLikes = new(data, null); ShowLibraryTracks(); likesMode.Value = mode;
                likedFilter.Text = "Artist"; RefreshLikedPlayback();
                await WaitForLikedLayoutAsync(() => ActiveLibraryButton(mode) is { ActualWidth: > 0 });
                if (mode == TrackViewMode.Cards && !likedTiles.Values.Any(tile => tile.Track?.Id == 1 && tile.ShowsPause) ||
                    mode == TrackViewMode.Waveform && !likedRows.Values.Any(row => row.Track?.Id == 1 && row.ShowsPause) ||
                    mode == TrackViewMode.List && !compactTrackRows.Any(row => row.Track?.Id == 1 && row.Overlay.ShowsPause))
                    throw new InvalidOperationException("A content view hid the globally active recording behind its local filter.");
                var button = ActiveLibraryButton(mode)!;
                var art = mode == TrackViewMode.Cards ? likedTiles.Values.First(tile => tile.Track?.Id == 1).Cover :
                    mode == TrackViewMode.List ? compactTrackRows.First(row => row.Track?.Id == 1 && likedCompactList.IsAncestorOf(row.Root)).Cover : null;
                RouteWaveformPointer(art?.Bounds.Center ?? button.Bounds.Center);
                await WaitForLikedLayoutAsync(() => button.IsHitTestVisible);
                RouteWaveformClick(button.Bounds.Center);
                await WaitForLikedLayoutAsync(() => !isPlaying.Value);
                AssertOrigin();
                RouteWaveformClick(button.Bounds.Center);
                await WaitForLikedLayoutAsync(() => isPlaying.Value);
                if (mode == TrackViewMode.Waveform) await SeekLikedTrackAsync(data[0], .25);
                AssertOrigin();
            }

            await NavigateRouteAsync(new(Page.Search, Search: "another query", Section: SearchSection.Tracks), _ =>
            {
                searchSection.Value = SearchSection.Tracks; SetSearchResults(new(data.Select(LibraryItem.FromTrack).ToArray(), null));
                return Task.CompletedTask;
            });
            await WaitForLikedLayoutAsync(() => searchViews.Values.Any(v => v.LibraryTrack?.Track?.Id == 1 && v.LibraryTrack.ShowsPause));
            await SeekLikedTrackAsync(data[0], .5); AssertOrigin();

            await NavigateRouteAsync(new(Page.Feed), _ =>
            {
                feedState = new(data.Select(track => new FeedEntry("global-" + track.Id, LibraryItem.FromTrack(track), track.User, "track", null)).ToArray(), null, api.Session, true);
                RenderFeed(); return Task.CompletedTask;
            });
            await WaitForLikedLayoutAsync(() => feedViews.Values.Any(v => v.Track?.Track?.Id == 1 && v.Track.ShowsPause));
            await SeekFeedTrackAsync(data[0], .3); AssertOrigin();
            await PlayFeedTrackAsync(data[0]); AssertOrigin();
            if (isPlaying.Value) throw new InvalidOperationException("Feed play button restarted the active recording instead of pausing.");
            await PlayFeedTrackAsync(data[0]);

            var user = data[0].User!;
            await NavigateRouteAsync(new(Page.UserProfile, Item: UserItem(user)), _ =>
            {
                userSection.Value = UserSection.Overview;
                userProfileState = new(user, new() { [UserSection.Overview] = new(data.Select(LibraryItem.FromTrack).ToArray(), null) }, [], [], [], false, LinksLoaded: true);
                RenderUserProfile(); return Task.CompletedTask;
            });
            await SeekUserTrackAsync(data[0], .4); AssertOrigin();
            await PlayUserTrackAsync(data[0]); AssertOrigin();
            if (isPlaying.Value) throw new InvalidOperationException("Profile play button changed the source instead of pausing.");
            await PlayUserTrackAsync(data[0]);

            var playlist = new SoundCloudPlaylist { Id = 91, Title = "Album", IsAlbum = true, User = user, Tracks = data, TrackCount = data.Length };
            var item = LibraryData.FromPlaylist(playlist);
            collectionDetails[item.Key] = new(item, new(data, null));
            await PlayUserPlaylistTrackAsync(item, data[0]); AssertOrigin();
            await PlayUserPlaylistTrackAsync(item, data[0]); AssertOrigin();
            await PlayUserPlaylistTrackAsync(item, data[1]);
            if (playbackQueue.Context is not { Kind: PlaybackContextKind.Album, ResourceUrn: "soundcloud:playlists:91" } ||
                playbackQueue.Current?.Track.Id != 2 || !playbackQueue.Snapshot.Source.Select(e => e.Track.Id).SequenceEqual([1L, 2L, 3L]))
                throw new InvalidOperationException("Selecting another preview recording lost its full collection/history context.");
            await VerifyCollectionEnqueueAsync(data, item);
            Console.WriteLine("GLOBAL_PLAYBACK_UI_OK: all library views and cross-page search/feed/profile controls, global active recording, pause/resume/seek retain source and occurrence, different-track collection selection and atomic paginated enqueue");

            Button? ActiveLibraryButton(TrackViewMode mode) => mode switch
            {
                TrackViewMode.Cards => likedTiles.Values.FirstOrDefault(tile => tile.Track?.Id == 1)?.PlaybackButton,
                TrackViewMode.Waveform => likedRows.Values.FirstOrDefault(row => row.Track?.Id == 1 && row.Root.FindVisualRoot() == Window)?.PlayButton,
                _ => compactTrackRows.FirstOrDefault(row => row.Track?.Id == 1 && likedCompactList.IsAncestorOf(row.Root))?.PlaybackButton
            };
            void AssertOrigin()
            {
                if (playbackQueue.SourceVersion != version || playbackQueue.Current?.EntryId != occurrence || playbackQueue.Context?.Key != "original-source" ||
                    !playbackQueue.Snapshot.Source.Select(e => e.Track.Id).SequenceEqual([1L, 2L, 3L]))
                    throw new InvalidOperationException("A control for the globally active recording replaced the original queue.");
            }
        }
        finally { player = savedPlayer; }
    }

    private async Task VerifyCollectionEnqueueAsync(SoundCloudTrack[] data, LibraryItem item)
    {
        var transport = new GlobalPlaybackTransport(); api.BrowserTransport = transport;
        playbackQueue.ClearManual();
        collectionDetails[item.Key] = new(item, new([data[0], data[0]], GlobalPlaybackTransport.Page));
        await EnqueueUserPlaylistAsync(item);
        if (!playbackQueue.Snapshot.ManualUpcoming.Select(e => e.Track.Id).SequenceEqual([1L, 1L, 2L, 3L]))
            throw new InvalidOperationException("Shared collection enqueue lost pages, order or intentional duplicate occurrences.");
        playbackQueue.ClearManual();
        collectionDetails[item.Key] = new(item, new([], null));
        await EnqueueUserPlaylistAsync(item);
        if (playbackQueue.Snapshot.ManualUpcoming.Count != 0 || status.Value != "В подборке пока нет треков.")
            throw new InvalidOperationException("Empty collection enqueue misreported success.");
        transport.Cycle = true; collectionDetails[item.Key] = new(item, new([data[0]], GlobalPlaybackTransport.Page));
        try { await EnqueueUserPlaylistAsync(item); throw new InvalidOperationException("Cursor cycle was not rejected."); }
        catch (SoundCloudException) { }
        if (playbackQueue.Snapshot.ManualUpcoming.Count != 0) throw new InvalidOperationException("Failed pagination left a partial manual queue.");
        transport.Cycle = false; transport.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueue = EnqueueUserPlaylistAsync(item);
        StartQueueContext(new("new-source", "New source", "Fixture"), data, data[0]);
        transport.Hold.SetResult(); await enqueue;
        if (playbackQueue.Snapshot.ManualUpcoming.Count != 0) throw new InvalidOperationException("A stale enqueue resurrected additions in a newer source.");
    }

    private sealed class GlobalPlaybackTransport : ISoundCloudBrowserTransport
    {
        internal const string Page = "https://api-v2.soundcloud.com/tracks?cursor=enqueue";
        internal bool Cycle;
        internal TaskCompletionSource? Hold;
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken token = default)
        {
            if (Hold != null) await Hold.Task;
            return JsonDocument.Parse(uri.AbsolutePath == "/tracks" ?
                "{\"collection\":[{\"id\":2,\"title\":\"Second\"},{\"id\":3,\"title\":\"Third\"}],\"next_href\":" + (Cycle ? JsonSerializer.Serialize(Page) : "null") + "}" :
                "{\"collection\":[],\"next_href\":null}");
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken token = default) => Task.CompletedTask;
    }
}
