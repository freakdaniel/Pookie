using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyCollectionsUiAsync()
    {
        try
        {
            Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
            var transport = new CollectionFixtureTransport();
            api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Collection fixture" }; signedIn.Value = true;
            libraryLikes = new(transport.Playlist.Tracks, null); likedIdsReady = true;
            await ShowWorkspaceAsync();
            await imageDiskCache.WriteAsync(transport.Playlist.ArtworkUrl!, ArtworkFixtureBitmap(500), lifetime.Token);
            var item = LibraryData.FromPlaylist(transport.Playlist with { Tracks = [], TrackCount = 0, Title = "Old title" });
            await OpenLibraryItemAsync(item);
            await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth > 150 && collectionTrackList.ActualHeight > 100 &&
                likedRows.Values.Any(row => row.Track?.Id == 9000 && row.Root.ActualWidth > 200));
            if (!playlistCollectionVisible.Value || collectionTitle.Text != transport.Playlist.Title || librarySectionTitle.Value != transport.Playlist.Title || collectionGrid.IsVisible && collectionGrid.ItemsSource.Count > 0 ||
                activeCollection?.Items.Length != 12 || collectionMeta.Text != "12 треков · 24:00 · Приватный")
                throw new InvalidOperationException("Playlist did not hydrate its own metadata or show the track list.");
            await LoadFollowingAsync();
            var follow = artistFollowButtons.Single(entry => entry.User()?.Id == 7 && entry.Button.FindVisualRoot() == Window).Button;
            if (follow.StyleName != TrackButtons.Reposted || !follow.IsEnabled)
                throw new InvalidOperationException("Existing subscription did not appear selected.");
            var remove = ToggleArtistFollowAsync(transport.Playlist.User!);
            if (follow.IsEnabled || follow.Opacity != 1 || transport.LastFollowing)
                throw new InvalidOperationException("Pending subscription flashed or sent the wrong action.");
            await ToggleArtistFollowAsync(transport.Playlist.User!);
            if (transport.FollowWrites != 1) throw new InvalidOperationException("Duplicate subscription was sent.");
            transport.FollowCompletion.SetResult(); await remove;
            if (followingIds.Contains(7) || !follow.IsEnabled || follow.StyleName != TrackButtons.Tonal)
                throw new InvalidOperationException("Unfollow did not update the button.");
            transport.FollowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var add = ToggleArtistFollowAsync(transport.Playlist.User!); transport.FollowCompletion.SetResult(); await add;
            if (!followingIds.Contains(7) || !transport.LastFollowing) throw new InvalidOperationException("Follow did not update the account state.");
            Window.WindowSize = WindowSize.Resizable(1000, 900, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth is >= 280 and <= 300 && collectionTitle.Bounds.Right < collectionCover.Bounds.X);
            if (collectionPlay.ActualWidth != 64 || collectionPlay.Bounds.Right >= collectionTitle.Bounds.X ||
                Math.Abs(collectionPlay.Bounds.Y - collectionTitle.Bounds.Y) > 1 ||
                Math.Abs(collectionCopy.Bounds.X - collectionPlay.Bounds.X) > 1 || collectionCopy.Bounds.Y <= collectionTitle.Bounds.Bottom ||
                Math.Abs(collectionCover.ActualWidth - collectionCover.ActualHeight) > 1)
                throw new InvalidOperationException("Playlist hero did not match the track hero's title, play button, bottom actions and right-hand square cover layout.");
            await WaitForLikedLayoutAsync(() => !collectionLoadingView.Skeleton.IsVisible && collectionBackdrop.Source != null && collectionBackdrop.Opacity == 1);
            await WaitForLoginFrameAsync(); CaptureUiPreview("playlist-page-narrow");
            Window.WindowSize = WindowSize.Resizable(1400, 900, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth > 300 && collectionTitle.Bounds.Right < collectionCover.Bounds.X);
            await WaitForLoginFrameAsync(); CaptureUiPreview("playlist-page-wide");
            var first = activeCollection!.Items[0].Track!;
            SetQueue(first);
            if (playbackQueue.Context?.Key != item.Key || playbackQueue.Context.Kind != Pookie.App.Playback.PlaybackContextKind.Playlist || playbackQueue.Snapshot.Source.Count != 12)
                throw new InvalidOperationException("Playlist playback copied another page's queue.");
            var viewer = CollectionScroll!; viewer.SetScrollOffsets(0, 220);
            await WaitForLoginFrameAsync();
            var offset = viewer.VerticalOffset;
            ShowLibraryTracks(); await MoveNavigationAsync(-1);
            await WaitForLikedLayoutAsync(() => Math.Abs(CollectionScroll!.VerticalOffset - offset) < 1);
            if (!playlistCollectionVisible.Value || collectionTitle.Text != transport.Playlist.Title)
                throw new InvalidOperationException("Back navigation lost playlist presentation.");
            await OpenPlaylistPickerAsync(first);
            await WaitForLikedLayoutAsync(() => playlistChoices.Children.Count == 2 && playlistChoices.ActualWidth > 100);
            var button = (Button)playlistChoices.Children[0];
            RouteWaveformClick(new(button.Bounds.X + button.ActualWidth / 2, button.Bounds.Y + button.ActualHeight / 2));
            await WaitForLikedLayoutAsync(() => transport.PlaylistWrites == 1);
            if (button.IsEnabled || !((Button)playlistChoices.Children[1]).IsEnabled || button.Opacity != 1)
                throw new InvalidOperationException("Playlist pending state disabled unrelated choices or flashed.");
            transport.PlaylistCompletion.SetResult();
            await WaitForLikedLayoutAsync(() => pendingPlaylistAdds.Count == 0);
            if (button.IsEnabled || button.StyleName != TrackButtons.Reposted) throw new InvalidOperationException("Completed playlist choice did not show added state.");
            await WaitForLikedLayoutAsync(() => !playlistPickerLoading.Skeleton.IsVisible);
            await WaitForLoginFrameAsync(); CaptureUiPreview("playlist-picker");
            ClosePlaylistPicker();
            transport.FollowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var late = ToggleArtistFollowAsync(transport.Playlist.User!);
            api.Session = new("other", "fixture-token", "Other"); ResetFollowing();
            transport.FollowCompletion.SetResult(); await late;
            if (followingIds.Count != 0) throw new InvalidOperationException("Old account subscription reply changed the new account.");
            Console.WriteLine("COLLECTIONS_UI_OK: playlist hero/list, responsive layout, queue source, history scroll, account following, per-artist pending, picker click, per-playlist pending and stale account replies");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("COLLECTIONS_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private sealed class CollectionFixtureTransport : ISoundCloudBrowserTransport
    {
        public SoundCloudPlaylist Playlist { get; } = new()
        {
            Id = 11, Title = "Музыка для долгой дороги", Description = "Треки, которые хочется слушать по пути домой.", Sharing = "private",
            User = new() { Id = 7, Username = "Автор подборки" }, Duration = 1440000, TrackCount = 12,
            ArtworkUrl = "https://i1.sndcdn.com/collection-fixture-t500x500.jpg",
            PermalinkUrl = "https://soundcloud.com/fixture/sets/road",
            Tracks = Enumerable.Range(0, 12).Select(i => new SoundCloudTrack { Id = 9000 + i, Title = "Трек " + (i + 1), Duration = 120000,
                User = new() { Id = 7, Username = "Исполнитель" }, LikesCount = 12,
                PermalinkUrl = "https://soundcloud.com/fixture/track-" + i }).ToArray()
        };
        public int FollowWrites, PlaylistWrites;
        public bool LastFollowing;
        public TaskCompletionSource FollowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PlaylistCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath == "/playlists/11") return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(Playlist, SoundCloudJson.Default.SoundCloudPlaylist)));
            if (uri.AbsolutePath == "/users/42/followings/ids") return Task.FromResult(JsonDocument.Parse("[7]"));
            if (uri.AbsolutePath == "/users/42/playlists") return Task.FromResult(JsonDocument.Parse("""
                {"collection":[{"kind":"playlist","id":21,"title":"Моя музыка","track_count":2,"user":{"id":42}},
                {"kind":"playlist","id":22,"title":"На вечер","track_count":4,"sharing":"private","user":{"id":42}},
                {"kind":"playlist","id":23,"title":"Чужая музыка","user":{"id":8}}]}
                """));
            throw new InvalidOperationException("Unexpected collection fixture request: " + uri.AbsolutePath);
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetFollowingAsync(long userId, long artistId, bool following, CancellationToken cancellationToken = default)
        { FollowWrites++; LastFollowing = following; return FollowCompletion.Task; }
        public Task AddToPlaylistAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken = default)
        { PlaylistWrites++; return PlaylistCompletion.Task; }
    }
}
