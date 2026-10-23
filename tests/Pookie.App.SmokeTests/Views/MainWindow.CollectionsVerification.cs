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
            await VerifyCollectionCardPlaybackAsync(item);
            await OpenLibraryItemAsync(item);
            await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth > 150 && collectionTrackList.ActualHeight > 100 &&
                compactTrackRows.Any(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.ActualWidth > 200));
            if (!playlistCollectionVisible.Value || collectionTitle.Text != transport.Playlist.Title || librarySectionTitle.Value != transport.Playlist.Title || collectionGrid.IsVisible && collectionGrid.ItemsSource.Count > 0 ||
                activeCollection?.Items.Length != 12 || collectionMeta.Text != "· Alternative Rock · 12 треков · 24:00 · Приватный · " + DetailAge(transport.Playlist.CreatedAt))
                throw new InvalidOperationException("Playlist did not hydrate its own metadata or show the track list.");
            await LoadCollectionActionsAsync();
            if (collectionLike.StyleName != TrackButtons.Selected || collectionRepost.StyleName != TrackButtons.Glass)
                throw new InvalidOperationException("Saved playlist action states were not restored.");
            var unlike = ToggleCollectionActionAsync(false);
            if (collectionLike.IsEnabled || !collectionRepost.IsEnabled || collectionLike.Opacity != 1)
                throw new InvalidOperationException("Playlist like pending disabled other actions or flashed.");
            await ToggleCollectionActionAsync(false);
            if (transport.PlaylistLikeWrites != 1) throw new InvalidOperationException("Duplicate playlist like was sent.");
            transport.PlaylistLikeCompletion.SetResult(); await unlike;
            if (likedPlaylistIds.Contains(11) || !collectionLike.IsEnabled) throw new InvalidOperationException("Playlist unlike did not update.");
            await ToggleCollectionActionAsync(true);
            if (!repostedPlaylistIds.Contains(11) || !transport.LastPlaylistReposted) throw new InvalidOperationException("Playlist repost did not update.");
            collectionFilter.Text = "Трек 10";
            if (pageItems[collectionTrackList].DataCount != 1 || activeCollection?.Items.Length != 12)
                throw new InvalidOperationException("Playlist filter changed source tracks.");
            SetQueue(activeCollection!.Items[9].Track!, 9);
            if (playbackQueue.Snapshot.Source.Count != 12 || playbackQueue.Current?.Track.Id != 9009)
                throw new InvalidOperationException("Filtered track did not retain the full playlist queue.");
            collectionFilter.Text = "";
            await LoadFollowingAsync();
            var follow = artistFollowButtons.Single(entry => entry.User()?.Id == 7 && entry.Button.FindVisualRoot() == Window).Button;
            if (follow.StyleName != TrackButtons.Confirmed || !follow.IsEnabled)
                throw new InvalidOperationException("Existing subscription did not appear selected.");
            var remove = ToggleArtistFollowAsync(transport.Playlist.User!);
            if (follow.IsEnabled || follow.Opacity != 1 || transport.LastFollowing)
                throw new InvalidOperationException("Pending subscription flashed or sent the wrong action.");
            await ToggleArtistFollowAsync(transport.Playlist.User!);
            if (transport.FollowWrites != 1) throw new InvalidOperationException("Duplicate subscription was sent.");
            transport.FollowCompletion.SetResult(); await remove;
            if (followingIds.Contains(7) || !follow.IsEnabled || follow.StyleName != TrackButtons.Glass)
                throw new InvalidOperationException("Unfollow did not update the button.");
            transport.FollowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var add = ToggleArtistFollowAsync(transport.Playlist.User!); transport.FollowCompletion.SetResult(); await add;
            if (!followingIds.Contains(7) || !transport.LastFollowing) throw new InvalidOperationException("Follow did not update the account state.");
            Window.WindowSize = WindowSize.Resizable(1000, 900, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth is >= 176 and <= 208 && collectionTitle.Bounds.Right < collectionCover.Bounds.X);
            if (Math.Abs(collectionPlay.ActualWidth - 64) >= 1 / Window.DpiScale || collectionPlay.Bounds.Right >= collectionTitle.Bounds.X ||
                Math.Abs(collectionPlay.Bounds.Y - collectionTitle.Bounds.Y) > 1 ||
                Math.Abs(collectionLike.Bounds.X - collectionPlay.Bounds.X) > 1 || collectionCopy.Bounds.Y <= collectionTitle.Bounds.Bottom ||
                Math.Abs(collectionAuthor.Bounds.Y - collectionMeta.Bounds.Y) > 2 || collectionAuthor.Bounds.X < collectionTitle.Bounds.X ||
                Math.Abs(follow.Bounds.Y - collectionLike.Bounds.Y) > 1 || follow.Bounds.X <= collectionCopy.Bounds.X ||
                Math.Abs(collectionPlay.Bounds.Y - collectionCover.Bounds.Y) > 1 ||
                Math.Abs(collectionCover.ActualWidth - collectionCover.ActualHeight) > 1 ||
                collectionCover.ActualHeight + 48 > 256 + 1 / Window.DpiScale)
                throw new InvalidOperationException("Playlist hero did not match the track hero's title, play button, bottom actions and right-hand square cover layout.");
            await WaitForLikedLayoutAsync(() => !collectionLoadingView.Skeleton.IsVisible && collectionBackdrop.Source != null && collectionBackdrop.Opacity == 1);
            VerifyCardGlassSource(collectionGlass!, collectionCover, collectionBackdrop, collectionPlay);
            await WaitForLoginFrameAsync(); CaptureUiPreview("playlist-page-narrow");
            Window.WindowSize = WindowSize.Resizable(1400, 900, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => Math.Abs(collectionCover.ActualWidth - 208) < 1 / Window.DpiScale && collectionTitle.Bounds.Right < collectionCover.Bounds.X);
            await WaitForLoginFrameAsync(); CaptureUiPreview("playlist-page-wide");
            if (Math.Abs(collectionRepost.Bounds.X - collectionLike.Bounds.Right - 2) > 1 ||
                collectionLike is not MaterialTrackButton { IsConnected: true } || collectionRepost is not MaterialTrackButton { IsConnected: true } ||
                ((MaterialTrackButton)collectionLike).ConnectedCorners.TopRight != 8 ||
                ((MaterialTrackButton)collectionRepost).ConnectedCorners.TopLeft != 22 ||
                collectionRepost.Background != TrackButtons.RepostedSurface || follow.Background != TrackButtons.ConfirmedSurface)
                throw new InvalidOperationException("Connected playlist actions lost their gap, selected shape or colours.");
            TrackButtons.SetConfirmed(follow, false, TrackButtons.Glass);
            await WaitForLikedLayoutAsync(() => follow.Background == TrackButtons.GlassSurface);
            if (follow.Background != collectionCopy.Background) throw new InvalidOperationException("Playlist follow background differs from other inactive actions.");
            RefreshFollowingButtons();
            var compact = compactTrackRows.First(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window);
            if (Math.Abs(compact.Cover.Bounds.X - compact.Root.Bounds.X - 8) > 1 || Math.Abs(compact.Root.ActualHeight - 64) >= 1 / Window.DpiScale || Math.Abs(compact.Cover.ActualWidth - 44) >= 1 / Window.DpiScale ||
                compact.More == null || compact.Duration.Bounds.Right >= compact.Root.Bounds.Right ||
                likedRows.Values.Any(row => row.Root.FindVisualRoot() == Window && row.Track?.Id == 9000))
                throw new InvalidOperationException("Playlist rows did not use compact unnumbered tracks and trailing actions.");
            RouteWaveformClick(new(compact.More.Bounds.X + compact.More.ActualWidth / 2, compact.More.Bounds.Y + compact.More.ActualHeight / 2));
            if (compact.Root.ContextMenu!.PlacementTarget != compact.More) throw new InvalidOperationException("Playlist row menu did not open from its button.");
            RouteWaveformClick(new(collectionMeta.Bounds.X + 4, collectionMeta.Bounds.Y + 4));
            var savedDescription = collectionDescription.Text;
            collectionDescription.Text = string.Join("\n", Enumerable.Repeat("Подробное описание подборки.", 35));
            collectionDescriptionExpanded = true; RefreshCollectionDescription();
            await WaitForLoginFrameAsync();
            if (collectionTrackList.ActualHeight < 100 || !collectionDescriptionToggle.IsVisible)
                throw new InvalidOperationException("Expanded playlist description hid its track list or collapse action.");
            collectionDescription.Text = savedDescription; collectionDescriptionExpanded = false; RefreshCollectionDescription();
            collectionFilter.Text = "нет совпадений";
            if (!collectionEmpty.IsVisible || pageItems[collectionTrackList].DataCount != 0) throw new InvalidOperationException("Missing filter results have no empty state.");
            await WaitForLoginFrameAsync();
            if (collectionTrackList.IsVisible) throw new InvalidOperationException("Empty playlist filter still paints retained track rows.");
            CaptureUiPreview("playlist-filter-empty");
            // A lone result is the reported trigger: move it between window
            // sizes, then filter it out and restore it repeatedly.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                collectionFilter.Text = "Трек 10";
                await WaitForLikedLayoutAsync(() => collectionTrackList.IsVisible && !collectionEmpty.IsVisible &&
                    compactTrackRows.Any(row => row.CollectionRow && row.Track?.Id == 9009 && row.Root.ActualWidth > 200));
                Window.WindowSize = WindowSize.Resizable(attempt % 2 == 0 ? 1000 : 1400, 900, minWidth: 1000, minHeight: 680);
                await WaitForLoginFrameAsync();
                var filteredRow = compactTrackRows.First(row => row.CollectionRow && row.Track?.Id == 9009 && row.Root.FindVisualRoot() == Window);
                if (Math.Abs(filteredRow.Cover.Bounds.X - collectionEmpty.Bounds.X) > 1)
                    throw new InvalidOperationException("Restored playlist result is offset from the empty-state alignment.");
                var previousListBounds = collectionTrackList.Bounds;
                collectionFilter.Text = "Трек 100-";
                await WaitForLoginFrameAsync();
                if (collectionTrackList.IsVisible || !collectionEmpty.IsVisible || pageItems[collectionTrackList].DataCount != 0)
                    throw new InvalidOperationException("A removed lone playlist result overlaps the empty state.");
                VerifyEmptyCollectionPaint(previousListBounds);
            }
            collectionFilter.Text = "";
            await WaitForLikedLayoutAsync(() => collectionTrackList.IsVisible && !collectionEmpty.IsVisible && pageItems[collectionTrackList].DataCount == 12);
            var first = activeCollection!.Items[0].Track!;
            SetQueue(first);
            if (playbackQueue.Context?.Key != item.Key || playbackQueue.Context.Kind != Pookie.App.Playback.PlaybackContextKind.Playlist || playbackQueue.Snapshot.Source.Count != 12)
                throw new InvalidOperationException("Playlist playback copied another page's queue.");
            collectionFilter.Text = "Трек";
            var viewer = CollectionScroll!; viewer.SetScrollOffsets(0, 220);
            await WaitForLoginFrameAsync();
            var offset = viewer.VerticalOffset;
            ShowLibraryTracks(); await MoveNavigationAsync(-1);
            await WaitForLikedLayoutAsync(() => Math.Abs(CollectionScroll!.VerticalOffset - offset) < 1);
            if (!playlistCollectionVisible.Value || collectionTitle.Text != transport.Playlist.Title || collectionFilter.Text != "Трек")
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
            if (button.IsEnabled || button.StyleName != TrackButtons.Confirmed) throw new InvalidOperationException("Completed playlist choice did not show added state.");
            await WaitForLikedLayoutAsync(() => !playlistPickerLoading.Skeleton.IsVisible);
            await WaitForLoginFrameAsync(); CaptureUiPreview("playlist-picker");
            ClosePlaylistPicker();
            transport.PlaylistLikeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var lateLike = ToggleCollectionActionAsync(false);
            transport.FollowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var late = ToggleArtistFollowAsync(transport.Playlist.User!);
            api.Session = new("other", "fixture-token", "Other"); ResetFollowing(); ResetCollectionActions();
            transport.PlaylistLikeCompletion.SetResult(); await lateLike;
            if (likedPlaylistIds.Count != 0 || collectionLike.IsEnabled) throw new InvalidOperationException("Old account playlist reply changed the new account.");
            transport.FollowCompletion.SetResult(); await late;
            if (followingIds.Count != 0) throw new InvalidOperationException("Old account subscription reply changed the new account.");
            Console.WriteLine("COLLECTIONS_UI_OK: compact unnumbered playlist, row menu, filter/full queue, expandable description, responsive hero, history filter/scroll, playlist likes/reposts, following/picker and stale account replies");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("COLLECTIONS_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private async Task VerifyCollectionCardPlaybackAsync(LibraryItem item)
    {
        libraryPages["collections"] = new([item], null);
        await ShowLibrarySectionAsync(Page.LibraryPlaylists);
        await WaitForLikedLayoutAsync(() => collectionCards.Any(pair => pair.Value.Item.Key == item.Key && pair.Key.ActualWidth > 100 && collectionGrid.IsAncestorOf(pair.Key)));
        var root = collectionCards.First(pair => pair.Value.Item.Key == item.Key && collectionGrid.IsAncestorOf(pair.Key)).Key;
        var card = CollectionCard(root);
        RouteWaveformPointer(new Point(2, 2));
        await WaitForLikedLayoutAsync(() => card.Playback.Opacity < .001);
        RouteWaveformPointer(new Point(card.Frame.Bounds.X + 10, card.Frame.Bounds.Y + 10));
        await WaitForLikedLayoutAsync(() => card.Playback.Opacity > .999 && Math.Abs(card.Playback.PlaybackButton!.ActualWidth - 46) < 1 / Window.DpiScale);
        CaptureUiPreview("playlist-card-hover");
        var originalPlayer = player;
        try
        {
            // Verify the real button's routing and full queue without requesting audio.
            player = null;
            var generation = navigationGeneration;
            var play = card.Playback.PlaybackButton!;
            RouteWaveformClick(new Point(play.Bounds.X + play.ActualWidth / 2, play.Bounds.Y + play.ActualHeight / 2));
            await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == item.Key);
            if (page.Value != Page.LibraryPlaylists || navigationGeneration != generation ||
                playbackQueue.Snapshot.Source.Count != 12 || playbackQueue.Current?.Track.Id != 9000)
                throw new InvalidOperationException("Playlist overlay navigated instead of starting its own complete queue.");
            current = playbackQueue.Current.Track; isPlaying.Value = true; RefreshLikedPlayback();
            RouteWaveformPointer(new Point(2, 2));
            await WaitForLikedLayoutAsync(() => card.Playback.Opacity > .999 && card.Playback.ShowsPause);
            isPlaying.Value = false; RefreshLikedPlayback();
            if (card.Playback.ShowsPause) throw new InvalidOperationException("Paused playlist card still shows pause.");
            playbackQueue.Clear(); current = null; RefreshLikedPlayback();
            await WaitForLikedLayoutAsync(() => card.Playback.Opacity < .001);
        }
        finally { player = originalPlayer; }
        Console.WriteLine("COLLECTION_CARD_PLAYBACK_OK: animated hover, isolated play-button click, full playlist queue, persistent active state and pause/play synchronization");
    }

    private void VerifyEmptyCollectionPaint(Rect previousBounds)
    {
        var scale = Window.DpiScale;
        var width = (int)Math.Ceiling(previousBounds.Width * scale);
        var height = (int)Math.Ceiling(Math.Min(256, previousBounds.Height) * scale);
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(width, height, scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.Transparent);
        context.Translate(-previousBounds.X, -previousBounds.Y);
        collectionTrackList.Render(context); context.EndFrame();
        var pixels = ((Aprillz.MewUI.Rendering.ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        for (var index = 3; index < pixels.Length; index += 4)
            if (pixels[index] != 0) throw new InvalidOperationException("Empty playlist still renders pixels from its previous track rows.");
    }

    private sealed class CollectionFixtureTransport : ISoundCloudBrowserTransport
    {
        public SoundCloudPlaylist Playlist { get; } = new()
        {
            Id = 11, Title = "Музыка для долгой дороги", Description = "Треки, которые хочется слушать по пути домой.", Sharing = "private",
            Genre = "Alternative Rock", CreatedAt = "2020-01-01T00:00:00Z",
            User = new() { Id = 7, Username = "Автор подборки" }, Duration = 1440000, TrackCount = 12,
            ArtworkUrl = "https://i1.sndcdn.com/collection-fixture-t500x500.jpg",
            PermalinkUrl = "https://soundcloud.com/fixture/sets/road",
            Tracks = Enumerable.Range(0, 12).Select(i => new SoundCloudTrack { Id = 9000 + i, Title = "Трек " + (i + 1), Duration = 120000,
                User = new() { Id = 7, Username = "Исполнитель" }, LikesCount = 12,
                PermalinkUrl = "https://soundcloud.com/fixture/track-" + i }).ToArray()
        };
        public int FollowWrites, PlaylistWrites, PlaylistLikeWrites;
        public bool LastPlaylistReposted;
        public TaskCompletionSource PlaylistLikeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool LastFollowing;
        public TaskCompletionSource FollowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PlaylistCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath == "/me/playlist_likes/ids") return Task.FromResult(JsonDocument.Parse("[11]"));
            if (uri.AbsolutePath == "/me/playlist_reposts/ids") return Task.FromResult(JsonDocument.Parse("[]"));
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
        public Task SetPlaylistLikedAsync(long userId, long playlistId, bool liked, CancellationToken cancellationToken = default)
        { PlaylistLikeWrites++; return PlaylistLikeCompletion.Task; }
        public Task SetPlaylistRepostedAsync(long playlistId, bool reposted, CancellationToken cancellationToken = default)
        { LastPlaylistReposted = reposted; return Task.CompletedTask; }
        public Task SetFollowingAsync(long userId, long artistId, bool following, CancellationToken cancellationToken = default)
        { FollowWrites++; LastFollowing = following; return FollowCompletion.Task; }
        public Task AddToPlaylistAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken = default)
        { PlaylistWrites++; return PlaylistCompletion.Task; }
    }
}
