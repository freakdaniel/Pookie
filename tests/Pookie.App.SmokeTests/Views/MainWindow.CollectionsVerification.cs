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
            if (webPlaylistEditingOnly) { await VerifyWebPlaylistEditingUiAsync(); return; }
            Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
            var transport = new CollectionFixtureTransport();
            api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Collection fixture" }; signedIn.Value = true;
            libraryLikes = new(transport.Playlist.Tracks, null); likedIdsReady = true;
            await ShowWorkspaceAsync();
            await imageDiskCache.WriteAsync(transport.Playlist.ArtworkUrl!, ArtworkFixtureBitmap(500), lifetime.Token);
            var item = LibraryData.FromPlaylist(transport.Playlist with { Tracks = [], TrackCount = 0, Title = "Old title" });
            if (shareUiSmoke || playlistDialogUiSmoke)
            {
                await OpenLibraryItemAsync(item);
                await WaitForLikedLayoutAsync(() => collectionFilter.ActualWidth > 0);
                // Present the collection fixture before measuring Share's first
                // opening, rather than counting that page's initial raster work.
                await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
                await Task.Delay(300, lifetime.Token);
                if (playlistDialogUiSmoke)
                {
                    await VerifyPlaylistMatteAsync(transport.Playlist.Tracks[0], transport);
                    await VerifyPlaylistDialogAsync(transport.Playlist.Tracks[0], transport);
                    return;
                }
                if (Environment.GetEnvironmentVariable("POOKIE_WIDGET_REAL_ONLY") == "1") { await VerifyShareWidgetNativeAsync(transport.Playlist.Tracks[0]); return; }
                if (Environment.GetEnvironmentVariable("POOKIE_SHARE_FUNCTIONAL_ONLY") == "1")
                    shareWidgetPreview.BrowserFactory = events => { var browser = new WidgetBrowserFixture(events); widgetBrowsers.Add(browser); return browser; };
                else await VerifyShareGlassAsync(transport.Playlist.Tracks[0]);
                await VerifySharePanelAsync(transport.Playlist.Tracks[0], LibraryData.FromPlaylist(transport.Playlist));
                return;
            }
            if (Environment.GetEnvironmentVariable("POOKIE_COMPACT_INTERACTION_PROBE") == "1")
            {
                await OpenLibraryItemAsync(item);
                Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive &&
                    compactTrackRows.Any(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.ActualWidth > 200));
                var row = compactTrackRows.First(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window);
                await VerifyCompactTrackPresentationAsync(row, transport);
                return;
            }
            await VerifyOverviewCardHoverAsync(item);
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
                Math.Abs(collectionLike.Bounds.X - collectionPlay.Bounds.X) > 1 || collectionShare.Bounds.Y <= collectionTitle.Bounds.Bottom ||
                Math.Abs(collectionAuthor.Bounds.Y - collectionMeta.Bounds.Y) > 2 || collectionAuthor.Bounds.X < collectionTitle.Bounds.X ||
                Math.Abs(follow.Bounds.Y - collectionLike.Bounds.Y) > 1 || follow.Bounds.X <= collectionShare.Bounds.X ||
                Math.Abs(collectionPlay.Bounds.Y - collectionCover.Bounds.Y) > 1 ||
                Math.Abs(collectionCover.ActualWidth - collectionCover.ActualHeight) > 1 ||
                collectionCover.ActualHeight + 48 > 256 + 1 / Window.DpiScale)
                throw new InvalidOperationException("Playlist hero did not match the track hero's title, play button, bottom actions and right-hand square cover layout.");
            await WaitForLikedLayoutAsync(() => !collectionLoadingView.Skeleton.IsVisible && collectionBackdrop.Source != null && collectionBackdrop.Opacity == 1);
            await VerifyCardGlassSourceAsync(collectionGlass!, collectionCover, collectionBackdrop, collectionPlay);
            var collectionMore = (Button)VisualTree.Find(collectionPlay.Parent!.Parent!, element =>
                element is Button button && button.ContextMenu?.Items.OfType<MenuItem>()
                    .Any(item => item.Command?.Id == "pookie.collection.queue") == true)!;
            RouteWaveformClick(collectionMore.Bounds.Center);
            var collectionMenu = PopupAppearance.MenuFor(collectionMore);
            await WaitForLikedLayoutAsync(() => collectionMenu.IsOpen && collectionMenu.Reveal == 1);
            VerifyContextMenuIcons(collectionMenu);
            await VerifyMoreClosesMenuAsync(collectionMore, collectionMenu);
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
            if (follow.Background != collectionShare.Background) throw new InvalidOperationException("Playlist follow background differs from other inactive actions.");
            RefreshFollowingButtons();
            var compact = compactTrackRows.First(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window);
            if (Math.Abs(compact.Cover.Bounds.X - compact.Root.Bounds.X - 8) > 1 || Math.Abs(compact.Root.ActualHeight - 64) >= 1 / Window.DpiScale || Math.Abs(compact.Cover.ActualWidth - 44) >= 1 / Window.DpiScale ||
                compact.Duration.Bounds.Right >= compact.Root.Bounds.Right ||
                likedRows.Values.Any(row => row.Root.FindVisualRoot() == Window && row.Track?.Id == 9000))
                throw new InvalidOperationException("Playlist rows did not use compact unnumbered tracks and trailing actions.");
            await VerifyCompactTrackPresentationAsync(compact, transport);
            await VerifyCompactArtworkBudgetAsync();
            MoveActionPointer(new(compact.Cover.Bounds.X + 4, compact.Cover.Bounds.Y + 4));
            await WaitForLikedLayoutAsync(() => compact.Actions.Strength > .999);
            if (compact.More == null) throw new InvalidOperationException("Hovered playlist row did not realize its action menu.");
            RouteWaveformClick(new(compact.More.Bounds.X + compact.More.ActualWidth / 2, compact.More.Bounds.Y + compact.More.ActualHeight / 2));
            if (PopupAppearance.MenuFor(compact.Root).PlacementTarget != compact.More) throw new InvalidOperationException("Playlist row menu did not open from its button.");
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
            await VerifyPlaylistDialogAsync(first, transport);
            await VerifySharePanelAsync(first, LibraryData.FromPlaylist(transport.Playlist));
            transport.ExistingPickerTrack = first.Id;
            await OpenPlaylistPickerAsync(first);
            await WaitForLikedLayoutAsync(() => playlistChoices.Children.Count == 2 && !playlistPickerLoading.Skeleton.IsVisible);
            var presentChoice = (Button)playlistChoices.Children[1];
            if (presentChoice.IsEnabled || presentChoice.StyleName != TrackButtons.Text || !((Button)playlistChoices.Children[0]).IsEnabled)
                throw new InvalidOperationException("Existing playlist membership was not applied before showing the picker.");
            RouteWaveformClick(SettingsCenter(presentChoice));
            if (transport.PlaylistWrites != 0) throw new InvalidOperationException("An already-present track could be added again from the picker.");
            CaptureUiPreview("playlist-picker-existing");
            transport.ExistingPickerTrack = null;
            await OpenPlaylistPickerAsync(first);
            await WaitForLikedLayoutAsync(() => playlistChoices.Children.Count == 2 && playlistChoices.ActualWidth > 100);
            await WaitForLikedLayoutAsync(() => !playlistDialog.Running);
            var button = (Button)playlistChoices.Children[0];
            RouteWaveformClick(new(button.Bounds.X + button.ActualWidth / 2, button.Bounds.Y + button.ActualHeight / 2));
            await WaitForLikedLayoutAsync(() => transport.PlaylistWrites == 1);
            if (button.IsEnabled || !((Button)playlistChoices.Children[1]).IsEnabled || button.Opacity != 1)
                throw new InvalidOperationException("Playlist pending state disabled unrelated choices or flashed.");
            transport.PlaylistCompletion.SetResult();
            await WaitForLikedLayoutAsync(() => pendingPlaylistAdds.Count == 0);
            if (button.IsEnabled || button.StyleName != TrackButtons.Text) throw new InvalidOperationException("Completed playlist choice did not stay neutral and disabled.");
            VisualTree.Visit(button, element =>
            { if (element is TextBlock { Text: "Добавлено" }) throw new InvalidOperationException("Completed playlist choice retained its added label."); });
            await WaitForLikedLayoutAsync(() => !playlistPickerLoading.Skeleton.IsVisible);
            await WaitForLoginFrameAsync(); CaptureUiPreview("playlist-picker");
            transport.PlaylistFails = true;
            var failedChoice = (Button)playlistChoices.Children[1];
            RouteWaveformClick(SettingsCenter(failedChoice));
            await WaitForLikedLayoutAsync(() => transport.PlaylistWrites == 2 && pendingPlaylistAdds.Count == 0);
            if (playlistPickerMessage.IsVisible || !failedChoice.IsEnabled || status.Value.Contains("Ресурс"))
                throw new InvalidOperationException("Failed playlist addition inserted a technical error row or left the choice locked.");
            transport.PlaylistFails = false;
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
            await VerifyWebPlaylistEditingUiAsync();
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
        MoveActionPointer(new Point(2, 2));
        await WaitForLikedLayoutAsync(() => card.Playback.Opacity < .001);
        MoveActionPointer(new Point(card.Frame.Bounds.X + 10, card.Frame.Bounds.Y + 10));
        await WaitForLikedLayoutAsync(() => card.Playback.Opacity > .999 && Math.Abs(card.Playback.PlaybackButton!.ActualWidth - 46) < 1 / Window.DpiScale);
        CaptureUiPreview("playlist-card-hover");
        var originalPlayer = player;
        try
        {
            // Verify the real button's routing and full queue without requesting audio.
            player = null;
            var generation = navigationGeneration;
            var play = card.Playback.PlaybackButton!;
            MoveActionPointer(new Point(play.Bounds.X + play.ActualWidth / 2, play.Bounds.Y + play.ActualHeight / 2));
            RouteWaveformClick(new Point(play.Bounds.X + play.ActualWidth / 2, play.Bounds.Y + play.ActualHeight / 2));
            await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == item.Key);
            if (page.Value != Page.LibraryPlaylists || navigationGeneration != generation ||
                playbackQueue.Snapshot.Source.Count != 12 || playbackQueue.Current?.Track.Id != 9000)
                throw new InvalidOperationException("Playlist overlay navigated instead of starting its own complete queue.");
            current = playbackQueue.Current.Track; isPlaying.Value = true; RefreshLikedPlayback();
            MoveActionPointer(new Point(2, 2));
            await WaitForLikedLayoutAsync(() => card.Playback.Opacity > .999 && card.Playback.ShowsPause);
            isPlaying.Value = false; RefreshLikedPlayback();
            if (card.Playback.ShowsPause) throw new InvalidOperationException("Paused playlist card still shows pause.");
            playbackQueue.Clear(); current = null; RefreshLikedPlayback();
            await WaitForLikedLayoutAsync(() => card.Playback.Opacity < .001);
        }
        finally { player = originalPlayer; }
        Console.WriteLine("COLLECTION_CARD_PLAYBACK_OK: animated hover, isolated play-button click, full playlist queue, persistent active state and pause/play synchronization");
    }

    private async Task VerifyOverviewCardHoverAsync(LibraryItem item)
    {
        libraryPages["recent"] = libraryPages["collections"] = new([item], null);
        BeginNavigation(new(Page.Library));
        ReplaceTracks(libraryLikes!);
        RefreshLibraryCards();
        RefreshOverviewSections();
        var tabs = new[] { "Лайки", "Плейлисты", "Альбомы", "Станции", "Подписки", "История" };
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && overviewSections["recent"].Grid.ActualWidth > 100);
        foreach (var title in tabs)
        {
            var tab = (Button)VisualTree.Find(Window.Content, element => element is Button { StyleName: "section-button" } button &&
                button.FindVisualChild<TextBlock>() is TextBlock label && label.Text == title)!;
            var label = (TextBlock)tab.FindVisualChild<TextBlock>()!;
            MoveActionPointer(tab.Bounds.Center);
            try { await WaitForLikedLayoutAsync(() => label.Foreground == Color.White, timeoutMs: 900); }
            catch (InvalidOperationException error)
            { throw new InvalidOperationException($"No hover for {title}: hit={Window.HitTest(tab.Bounds.Center)?.GetType().Name}, hover={tab.IsMouseOver}.", error); }
        }
        async Task CheckCard(string section)
        {
            var grid = overviewSections[section].Grid;
            await WaitForLikedLayoutAsync(() => collectionCards.Keys.Any(root => grid.IsAncestorOf(root) && root.ActualWidth > 100));
            var root = collectionCards.Keys.First(grid.IsAncestorOf);
            var card = CollectionCard(root);
            var zoom = (TransformDecorator)card.Frame.FindVisualChild<TransformDecorator>()!;
            var before = card.Frame.Bounds;
            MoveActionPointer(before.Center);
            await WaitForLikedLayoutAsync(() => card.Playback.Opacity > .999 && zoom.Transform.M11 > 1.03f);
            if (card.Playback.Background != Color.FromArgb(64, 0, 0, 0) || card.Frame.Bounds.Size != before.Size)
                throw new InvalidOperationException("Artwork hover lost its dimming or changed the card layout.");
            if (!card.Playback.PlaybackButton!.Focus()) throw new InvalidOperationException("Overview play button cannot take focus.");
            RefreshOverviewSections();
            await WaitForLoginFrameAsync();
            if (!card.Playback.PlaybackButton.IsFocused || !root.IsMouseOver || card.Playback.Opacity < .99)
                throw new InvalidOperationException($"Refreshing {section} discarded the play button's hover or focus.");
            CaptureUiPreview("overview-hover-" + section);
            MoveActionPointer(new Point(2, 2));
            Window.FocusManager.ClearFocus();
            await WaitForLikedLayoutAsync(() => card.Playback.Opacity < .001 && zoom.Transform.IsIdentity);
            if (card.Playback.PlaybackButton.IsHitTestVisible)
                throw new InvalidOperationException("A hidden playback button remains a pointer target.");
        }
        await CheckCard("recent");
        var recentGrid = overviewSections["recent"].Grid;
        var playlistCard = CollectionCard(collectionCards.Keys.First(recentGrid.IsAncestorOf));
        RouteContextRightClick(playlistCard.Frame.Bounds.Center);
        var playlistMenu = PopupAppearance.MenuFor(playlistCard.Root);
        await WaitForLikedLayoutAsync(() => playlistMenu.IsOpen && playlistMenu.Reveal == 1);
        VerifyContextMenuIcons(playlistMenu);
        if (playlistMenu.Items.OfType<MenuItem>().Any(entry => entry.Command?.Id.StartsWith("pookie.queue.", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("A playlist card still offers track actions.");
        CaptureUiPreview("overview-playlist-menu");
        var add = playlistMenu.Buttons.Single(button => button.Command?.Id == "pookie.collection.card.queue");
        RouteWaveformClick(SettingsCenter(add));
        await WaitForLikedLayoutAsync(() => !playlistMenu.IsOpen && playbackQueue.Snapshot.ManualUpcoming.Count == libraryLikes!.Tracks.Length);
        if (page.Value != Page.Library || !playbackQueue.Snapshot.ManualUpcoming.Select(entry => entry.Track.Id).SequenceEqual(libraryLikes!.Tracks.Select(track => track.Id)))
            throw new InvalidOperationException("The playlist card did not enqueue the whole playlist in order.");
        playbackQueue.Clear();
        var tile = libraryTiles.Values.First(tile => tile.Track != null && tile.Root.ActualWidth > 100);
        shellScroll.SetScrollOffsets(0, Math.Max(0, shellScroll.VerticalOffset + tile.Author.Bounds.Y - startupHeader.Bounds.Bottom - 80));
        await WaitForLoginFrameAsync();
        await WaitForLikedLayoutAsync(() => tile.Author.Bounds.Y > startupHeader.Bounds.Bottom && tile.Author.Bounds.Bottom < Window.ClientSize.Height - 100);
        MoveActionPointer(new Point(tile.Author.Bounds.X + 5, tile.Author.Bounds.Center.Y));
        await WaitForLikedLayoutAsync(() => tile.Author.Foreground != Muted);
        MoveActionPointer(new Point(2, 2));
        await WaitForLikedLayoutAsync(() => tile.Author.Foreground == Muted);
        shellScroll.SetScrollOffsets(0, Math.Max(0, shellScroll.VerticalOffset + overviewSections["playlists"].Grid.Bounds.Y - startupHeader.Bounds.Bottom - 24));
        await WaitForLoginFrameAsync();
        await CheckCard("playlists");
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
                User = new() { Id = 7, Username = "Исполнитель" }, LikesCount = 12, PlaybackCount = 123456 + i * 100,
                PermalinkUrl = "https://soundcloud.com/fixture/track-" + i }).ToArray()
        };
        public int FollowWrites, PlaylistWrites, PlaylistLikeWrites;
        internal bool PlaylistFails;
        internal TaskCompletionSource? TrackLikeCompletion;
        public bool LastPlaylistReposted;
        public TaskCompletionSource PlaylistLikeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool LastFollowing;
        public TaskCompletionSource FollowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PlaylistCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? PickerHold;
        internal int PickerCount = 2;
        internal bool PickerFails;
        internal long? ExistingPickerTrack;
        internal int CreateWrites;
        internal PlaylistCreateRequest? LastCreate;
        internal TaskCompletionSource? CreateHold;
        internal bool CreateFails;
        public async Task<JsonDocument> CreatePlaylistAsync(PlaylistCreateRequest request, CancellationToken cancellationToken = default)
        {
            CreateWrites++; LastCreate = request;
            if (CreateHold != null) await CreateHold.Task.WaitAsync(cancellationToken);
            if (CreateFails) throw new HttpRequestException("Fixture lost response");
            return JsonSerializer.SerializeToDocument(new SoundCloudPlaylist
            {
                Id = 777, Title = request.Title, Sharing = request.Sharing, TrackCount = 1,
                User = new() { Id = request.UserId }, Tracks = [new() { Id = request.TrackId }]
            }, SoundCloudJson.Default.SoundCloudPlaylist);
        }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath == "/search/users") return Task.FromResult(JsonDocument.Parse("{\"collection\":[]}"));
            if (uri.AbsolutePath == "/me/playlist_likes/ids") return Task.FromResult(JsonDocument.Parse("[11]"));
            if (uri.AbsolutePath == "/me/playlist_reposts/ids") return Task.FromResult(JsonDocument.Parse("[]"));
            if (uri.AbsolutePath == "/playlists/11") return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(Playlist, SoundCloudJson.Default.SoundCloudPlaylist)));
            if (uri.AbsolutePath == "/users/42/followings/ids") return Task.FromResult(JsonDocument.Parse("[7]"));
            if (uri.AbsolutePath == "/users/42/playlists" && PickerHold != null) return WaitPickerAsync(uri, cancellationToken);
            if (uri.AbsolutePath == "/users/42/playlists" && PickerFails) throw new SoundCloudException("Не удалось получить плейлисты", 503);
            if (uri.AbsolutePath == "/users/42/playlists" && PickerCount != 2)
                return Task.FromResult(JsonSerializer.SerializeToDocument(new { collection = Enumerable.Range(0, PickerCount).Select(i => new
                    { kind = "playlist", id = 100 + i, title = "Подборка " + i, track_count = i + 1, user = new { id = 42 },
                        tracks = Enumerable.Range(0, i + 1).Select(j => new { id = 10000 + i * 100 + j }) }) }));
            if (uri.AbsolutePath == "/users/42/playlists") return Task.FromResult(JsonSerializer.SerializeToDocument(new
            {
                collection = new[]
                {
                    new { kind = "playlist", id = 21, title = "Моя музыка", track_count = 2, sharing = "public", user = new { id = 42 }, tracks = new[] { new { id = 101L }, new { id = 102L } } },
                    new { kind = "playlist", id = 22, title = "На вечер", track_count = 4, sharing = "private", user = new { id = 42 }, tracks = new[] { 201L, 202L, 203L, ExistingPickerTrack ?? 204L }.Select(id => new { id }).ToArray() },
                    new { kind = "playlist", id = 23, title = "Чужая музыка", track_count = 0, sharing = "public", user = new { id = 8 }, tracks = Enumerable.Empty<long>().Select(id => new { id }).ToArray() }
                }
            }));
            throw new InvalidOperationException("Unexpected collection fixture request: " + uri.AbsolutePath);
        }
        private async Task<JsonDocument> WaitPickerAsync(Uri uri, CancellationToken token)
        { await PickerHold!.Task.WaitAsync(token); PickerHold = null; return await GetJsonAsync(uri, token); }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) =>
            TrackLikeCompletion?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;
        public Task SetPlaylistLikedAsync(long userId, long playlistId, bool liked, CancellationToken cancellationToken = default)
        { PlaylistLikeWrites++; return PlaylistLikeCompletion.Task; }
        public Task SetPlaylistRepostedAsync(long playlistId, bool reposted, CancellationToken cancellationToken = default)
        { LastPlaylistReposted = reposted; return Task.CompletedTask; }
        public Task SetFollowingAsync(long userId, long artistId, bool following, CancellationToken cancellationToken = default)
        { FollowWrites++; LastFollowing = following; return FollowCompletion.Task; }
        public Task AddToPlaylistAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken = default)
        { PlaylistWrites++; return PlaylistFails ? Task.FromException(new SoundCloudException("Ресурс SoundCloud не найден.", 404)) : PlaylistCompletion.Task; }
    }
}
