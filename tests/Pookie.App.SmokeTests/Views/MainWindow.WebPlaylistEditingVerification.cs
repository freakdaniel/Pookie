using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyWebPlaylistEditingUiAsync()
    {
        var fixture = new WebPlaylistEditingFixture(new CollectionFixtureTransport().Playlist);
        api.Session = new("fixture", "web-fixture", "Test");
        api.Transport = fixture; me = new() { Id = 42, Username = "Owner" };
        signedIn.Value = true;
        Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
        await ShowWorkspaceAsync();
        await imageDiskCache.WriteAsync(fixture.Playlist.ArtworkUrl!, ArtworkFixtureBitmap(500), lifetime.Token);
        ClearLibraryData(); ResetFollowing(); ResetReposts();
        var item = LibraryData.FromPlaylist(fixture.Playlist);
        await OpenLibraryItemAsync(item);
        await WaitForLikedLayoutAsync(() => compactTrackRows.Any(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window));
        if (!CanEditDisplayedPlaylist() || !HasCompletePlaylistOrder()) throw new InvalidOperationException("Owned web playlist has no editing actions.");
        var row = compactTrackRows.First(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window);
        foreach (var id in new[] { "remove", "up", "down" })
            if (!row.Root.ContextMenu!.Items.OfType<MenuItem>().Any(entry => entry.Command?.Id == "pookie.playlist." + id))
                throw new InvalidOperationException("Missing playlist track menu command: " + id);
        OpenPlaylistEditor();
        await WaitForLikedLayoutAsync(() => playlistEditor.IsOpen && !playlistEditor.Running);
        if (playlistEditSave.IsEnabled || !playlistEditFocus.IsFocused || !playlistEditor.HasGlassBackdrop || !playlistEditor.UsesCachedTransition)
            throw new InvalidOperationException("Playlist editor lost matte overlay glass, neutral focus or enabled an unchanged save.");
        await VerifyPlaylistEditorSizingAsync();
        SelectPlaylistEditTab(2);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        var typeButton = (Button)playlistEditType.Content!;
        RouteWaveformClick(SettingsCenter(typeButton));
        await WaitForLikedLayoutAsync(() => PopupAppearance.MenuFor(typeButton).Reveal == 1);
        RouteShareKey(Key.Escape);
        if (!playlistEditor.IsOpen || PopupAppearance.MenuFor(typeButton).IsOpen)
            throw new InvalidOperationException("Release type did not use the app popup or Escape dismissed the whole editor.");
        await WaitForLikedLayoutAsync(() => !PopupAppearance.MenuFor(typeButton).IsVisible);
        playlistEditDate.Text = "30.02.2026";
        if (playlistEditSave.IsEnabled || !playlistEditValidation.IsVisible) throw new InvalidOperationException("Invalid release date has no visible validation.");
        playlistEditDate.Text = playlistEditOriginalDate;
        Window.WindowSize = WindowSize.Resizable(1000, 680, minWidth: 1000, minHeight: 680);
        SelectPlaylistEditTab(1);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
        if (playlistEditSave.Bounds.Bottom > playlistEditFrame.Bounds.Bottom - 12 || playlistEditSave.ActualHeight < 44 ||
            playlistEditFrame.Bounds.Top < 0 || playlistEditFrame.Bounds.Bottom > Window.ClientSize.Height)
            throw new InvalidOperationException("Playlist editor lost its footer or escaped the smaller viewport.");
        CaptureUiPreview("web-playlist-editor-small");
        Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
        await WaitForLoginFrameAsync();
        SelectPlaylistEditTab(0);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        var originalIds = fixture.Playlist.Tracks.Select(track => track.Id).ToArray();
        SelectPlaylistEditTab(1);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        EditPlaylistDraftTrack(0, 1); EditPlaylistDraftTrack(2, 0);
        if (!fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(originalIds) || !playlistEditSave.IsEnabled)
            throw new InvalidOperationException("Draft track changes wrote before Save or did not enable it.");
        await WaitForLikedLayoutAsync(() => playlistEditTrackRows.Children.OfType<Grid>().All(row => row.ActualHeight == 56));
        await WaitForLoginFrameAsync();
        CaptureUiPreview("web-playlist-editor-tracks");
        ClosePlaylistEditor(); await WaitForLikedLayoutAsync(() => !playlistEditor.IsVisible);
        OpenPlaylistEditor(); await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        if (!playlistEditTracks.Select(track => track.Id).SequenceEqual(originalIds) || playlistEditTab.Value != 0 || playlistEditSave.IsEnabled)
            throw new InvalidOperationException("Cancel retained a staged reorder/removal or reopened on an outdated tab.");
        playlistEditName.Text = "Обновлённый плейлист 🎵"; playlistEditDescription.Text = "Первая строка\nВторая строка";
        playlistEditGenre.Text = "Alternative Rock"; playlistEditTags.Text = "rock \"indie rock\""; playlistEditDate.Text = "13.11.2026";
        playlistEditPermalink.Text = "updated-playlist";
        playlistEditBuy.Text = "https://example.com/buy"; playlistEditLabel.Text = "Pookie Records";
        playlistEditLicense.Select("cc-by");
        playlistEditPrivate = false; RefreshPlaylistEditor();
        await WaitForLikedLayoutAsync(() => !playlistEditPrivacy.Animating);
        CaptureUiPreview("web-playlist-editor");
        SelectPlaylistEditTab(2); await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        CaptureUiPreview("web-playlist-editor-release");
        SelectPlaylistEditTab(3); await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        CaptureUiPreview("web-playlist-editor-access");
        SelectPlaylistEditTab(0); await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        fixture.UpdateHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = SavePlaylistEditorAsync();
        if (playlistEditSave.IsEnabled || playlistEditCancel.IsEnabled) throw new InvalidOperationException("Pending edit remains actionable.");
        ClosePlaylistEditor();
        if (!playlistEditor.IsOpen) throw new InvalidOperationException("Pending edit was dismissed without completing.");
        fixture.UpdateHold.SetResult(); await save;
        if (collectionTitle.Text != "Обновлённый плейлист 🎵" || displayedCollection?.Playlist?.Sharing != "public" || playlistEditor.IsOpen ||
            !libraryPages["collections"].Items.Any(entry => entry.Title == "Обновлённый плейлист 🎵") ||
            fixture.Playlist is not { Genre: "Alternative Rock", TagList: "rock \"indie rock\"", ReleaseDate: "2026-11-13T00:00:00.000Z", Permalink: "updated-playlist",
                PurchaseUrl: "https://example.com/buy", LabelName: "Pookie Records", License: "cc-by" })
            throw new InvalidOperationException("Confirmed edit did not update the page and library.");
        await EditPlaylistTrackAsync(activeCollection!.Items[0].Track!, 0, 1);
        if (activeCollection.Items[0].Track?.Id != 9001 || fixture.Playlist.Tracks[0].Id != 9001)
            throw new InvalidOperationException("Reordering did not refresh the displayed source order.");
        await EditPlaylistTrackAsync(activeCollection.Items[0].Track!, 0, 0);
        if (activeCollection.Items.Any(entry => entry.Track?.Id == 9001) || displayedCollection?.Playlist?.TrackCount != 11)
            throw new InvalidOperationException("Removed track stayed in the playlist view.");
        OpenPlaylistEditor();
        await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        var beforeDraftSave = fixture.Playlist.Tracks.Select(track => track.Id).ToArray();
        EditPlaylistDraftTrack(0, 1); EditPlaylistDraftTrack(2, 0);
        var expectedDraft = playlistEditTracks.Select(track => track.Id).ToArray();
        if (!fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(beforeDraftSave))
            throw new InvalidOperationException("Staged tracks were written eagerly.");
        await SavePlaylistEditorAsync();
        if (!fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(expectedDraft) || fixture.Playlist.TrackCount != 10)
            throw new InvalidOperationException("Save did not commit the exact staged track order/count.");
        fixture.Playlist = fixture.Playlist with { TrackCount = 15 };
        await ReloadEditedPlaylistAsync(item, api.Session!);
        OpenPlaylistEditor();
        if (playlistEditSave.IsEnabled || playlistEditOrderComplete || HasCompletePlaylistOrder())
            throw new InvalidOperationException($"Hidden-track editing state: save={playlistEditSave.IsEnabled}, complete={HasCompletePlaylistOrder()}, count={displayedCollection?.Playlist?.TrackCount}, tracks={displayedCollection?.Playlist?.Tracks.Length}, open={playlistEditor.IsOpen}, canEdit={CanEditDisplayedPlaylist()}.");
        var retained = fixture.Playlist.Tracks.Select(track => track.Id).ToArray();
        playlistEditName.Text = "Плейлист со скрытыми треками";
        if (!playlistEditSave.IsEnabled) throw new InvalidOperationException("Incomplete track snapshot blocked a metadata-only save.");
        await SavePlaylistEditorAsync();
        if (fixture.Playlist.TrackCount != 15 || !fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(retained) || collectionTitle.Text != "Плейлист со скрытыми треками")
            throw new InvalidOperationException("Metadata editing changed hidden count or visible order.");
        var refusedPartialOrder = false;
        try { await EditPlaylistTrackAsync(activeCollection.Items[0].Track!, 0, 0); }
        catch (SoundCloudException) { refusedPartialOrder = true; }
        if (!refusedPartialOrder || fixture.Playlist.TrackCount != 15)
            throw new InvalidOperationException("A partial web snapshot was overwritten during removal.");
        OpenPlaylistEditor(deleting: true);
        await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        CaptureUiPreview("web-playlist-delete-confirmation");
        if (!playlistEditor.IsOpen || !playlistEditDeleting || !playlistEditSave.IsEnabled || fixture.Deleted)
            throw new InvalidOperationException("Playlist deletion skipped its confirmation.");
        await SavePlaylistEditorAsync();
        if (!fixture.Deleted || page.Value != Page.LibraryPlaylists || libraryPages["collections"].Items.Any(entry => entry.Playlist?.Id == fixture.Playlist.Id))
            throw new InvalidOperationException("Deleted playlist remained in the library.");
        Console.WriteLine("WEB_PLAYLIST_EDIT_UI_OK: matte glass overlay, four sliding tabs, neutral focus, changed-only save, metadata/privacy confirmation, cancelable staged track edits, pending isolation, reorder/remove, hidden-track protection and deletion dialog/library refresh; fixture only");
    }

    private async Task VerifyPlaylistEditorSizingAsync()
    {
        var basicHeight = playlistEditFrame.Bounds.Height;
        if (basicHeight >= 440 || playlistEditTrackSummary.IsVisible || playlistEditTrackSummary.Text.Length != 0)
            throw new InvalidOperationException("Basic editing still uses the oversized fixed card or retains redundant track copy.");
        var samples = new HashSet<double>();
        var backdrop = playlistEditor.BackdropVersion; var allocations = playlistEditor.OpticalAllocations;
        void Frame()
        {
            if (!playlistEditFrame.Resizing) return;
            samples.Add(Math.Round(playlistEditFrame.Bounds.Height, 1));
            if (playlistEditPages.ContentOverlaps || Math.Abs(playlistEditFocus.Bounds.Y - playlistEditFrame.Bounds.Y - 24) > .5)
                throw new InvalidOperationException("Resizing duplicated page content or displaced the fixed header inset.");
        }
        Window.FrameRendered += Frame;
        try
        {
            SelectPlaylistEditTab(3);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            if (playlistEditFrame.Bounds.Height >= basicHeight - 40)
                throw new InvalidOperationException("Access did not shrink to its short form.");
            SelectPlaylistEditTab(2);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            if (playlistEditFrame.Bounds.Height <= basicHeight + 40)
                throw new InvalidOperationException("Release did not grow to fit the additional settings.");
            SelectPlaylistEditTab(1);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            while (playlistEditTracks.Count > 1) EditPlaylistDraftTrack(playlistEditTracks.Count - 1, 0);
            await WaitForLikedLayoutAsync(() => !playlistEditFrame.Resizing);
            if (playlistEditFrame.Bounds.Height >= 280 || playlistEditTrackSummary.IsVisible ||
                Math.Abs(playlistEditFrame.Bounds.Bottom - playlistEditSave.Bounds.Bottom - 24) > 1)
                throw new InvalidOperationException("One-track draft retained the tall list viewport, redundant summary or misplaced footer.");
            CaptureUiPreview("web-playlist-editor-one-track");
            SelectPlaylistEditTab(2); await Task.Delay(45, lifetime.Token);
            SelectPlaylistEditTab(3); await Task.Delay(45, lifetime.Token);
            SelectPlaylistEditTab(0);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            if (Math.Abs(playlistEditFrame.Bounds.Height - basicHeight) > 1 || samples.Count < 6 ||
                playlistEditor.BackdropVersion != backdrop || playlistEditor.OpticalAllocations != allocations)
                throw new InvalidOperationException("Interrupted resize skipped its intermediate heights, lost the selected page size or rebuilt glass.");
        }
        finally { Window.FrameRendered -= Frame; }
        ClosePlaylistEditor(); await WaitForLikedLayoutAsync(() => !playlistEditor.IsVisible);
        OpenPlaylistEditor(); await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        Console.WriteLine("PLAYLIST_EDITOR_SIZE_OK: compact basic/access, measured release, animated/reversible height, one-track shrink, no redundant summary and retained glass");
    }

    private sealed class WebPlaylistEditingFixture : ISoundCloudTransport
    {
        internal SoundCloudPlaylist Playlist;
        internal bool Deleted;
        internal TaskCompletionSource? UpdateHold;
        internal WebPlaylistEditingFixture(SoundCloudPlaylist playlist) => Playlist = playlist with { User = new() { Id = 42, Username = "Owner" }, TrackPagesComplete = true };
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(
            uri.AbsolutePath.StartsWith("/playlists/") ? JsonSerializer.SerializeToDocument(Playlist, SoundCloudJson.Default.SoundCloudPlaylist) :
            JsonSerializer.SerializeToDocument(new { collection = Deleted ? Array.Empty<SoundCloudPlaylist>() : new[] { Playlist } },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<JsonDocument> EditPlaylistAsync(PlaylistEditRequest request, CancellationToken cancellationToken = default)
        {
            if (UpdateHold != null) { await UpdateHold.Task.WaitAsync(cancellationToken); UpdateHold = null; }
            if (request.Update is { } update)
            {
                var metadata = update.Metadata;
                Playlist = Playlist with { Title = update.Title, Description = update.Description, Sharing = update.Sharing,
                    Permalink = metadata?.Permalink ?? Playlist.Permalink, Genre = metadata?.Genre ?? Playlist.Genre, TagList = metadata?.TagList ?? Playlist.TagList,
                    ReleaseDate = metadata?.ReleaseDate is { } date ? date.Length == 0 ? null : date + "T00:00:00.000Z" : Playlist.ReleaseDate,
                    PlaylistType = metadata?.PlaylistType ?? Playlist.PlaylistType, PurchaseUrl = metadata?.PurchaseUrl ?? Playlist.PurchaseUrl,
                    LabelName = metadata?.LabelName ?? Playlist.LabelName, License = metadata?.License ?? Playlist.License,
                    Tracks = update.TrackIds?.Select(id => Playlist.Tracks.First(track => track.Id == id)).ToArray() ?? Playlist.Tracks,
                    TrackCount = update.TrackIds?.Length ?? Playlist.TrackCount };
            }
            if (request.Kind == "remove" && Playlist.Tracks.Length != Playlist.TrackCount) throw new SoundCloudException("SoundCloud не подтвердил полный список треков плейлиста.");
            if (request.Kind == "remove") Playlist = Playlist with { Tracks = Playlist.Tracks.Where(track => track.Id != request.TrackId).ToArray(), TrackCount = Playlist.TrackCount - 1 };
            if (request.Kind == "delete") Deleted = true;
            return JsonSerializer.SerializeToDocument(Playlist, SoundCloudJson.Default.SoundCloudPlaylist);
        }
    }
}
