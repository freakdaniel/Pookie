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
        playlistEditName.Text = "Обновлённый плейлист 🎵"; playlistEditDescription.Text = "Первая строка\nВторая строка";
        playlistEditPrivate = false; RefreshPlaylistEditor();
        await WaitForLikedLayoutAsync(() => !playlistEditPrivacy.Animating);
        CaptureUiPreview("web-playlist-editor");
        fixture.UpdateHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = SavePlaylistEditorAsync();
        if (playlistEditSave.IsEnabled || playlistEditCancel.IsEnabled) throw new InvalidOperationException("Pending edit remains actionable.");
        ClosePlaylistEditor();
        if (!playlistEditor.IsOpen) throw new InvalidOperationException("Pending edit was dismissed without completing.");
        fixture.UpdateHold.SetResult(); await save;
        if (collectionTitle.Text != "Обновлённый плейлист 🎵" || displayedCollection?.Playlist?.Sharing != "public" || playlistEditor.IsOpen ||
            !libraryPages["collections"].Items.Any(entry => entry.Title == "Обновлённый плейлист 🎵"))
            throw new InvalidOperationException("Confirmed edit did not update the page and library.");
        await EditPlaylistTrackAsync(activeCollection!.Items[0].Track!, 0, 1);
        if (activeCollection.Items[0].Track?.Id != 9001 || fixture.Playlist.Tracks[0].Id != 9001)
            throw new InvalidOperationException("Reordering did not refresh the displayed source order.");
        await EditPlaylistTrackAsync(activeCollection.Items[0].Track!, 0, 0);
        if (activeCollection.Items.Any(entry => entry.Track?.Id == 9001) || displayedCollection?.Playlist?.TrackCount != 11)
            throw new InvalidOperationException("Removed track stayed in the playlist view.");
        fixture.Playlist = fixture.Playlist with { TrackCount = 15 };
        await ReloadEditedPlaylistAsync(item, api.Session!);
        OpenPlaylistEditor();
        if (!playlistEditSave.IsEnabled || HasCompletePlaylistOrder())
            throw new InvalidOperationException($"Hidden-track editing state: save={playlistEditSave.IsEnabled}, complete={HasCompletePlaylistOrder()}, count={displayedCollection?.Playlist?.TrackCount}, tracks={displayedCollection?.Playlist?.Tracks.Length}, open={playlistEditor.IsOpen}, canEdit={CanEditDisplayedPlaylist()}.");
        var retained = fixture.Playlist.Tracks.Select(track => track.Id).ToArray();
        playlistEditName.Text = "Плейлист со скрытыми треками";
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
        Console.WriteLine("WEB_PLAYLIST_EDIT_UI_OK: web open, owner menus, metadata/privacy confirmation, pending isolation, reorder, remove, hidden-track protection and deletion dialog/library refresh; fixture only");
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
            if (request.Update is { } update) Playlist = Playlist with { Title = update.Title, Description = update.Description, Sharing = update.Sharing,
                Tracks = update.TrackIds?.Select(id => Playlist.Tracks.First(track => track.Id == id)).ToArray() ?? Playlist.Tracks };
            if (request.Kind == "remove" && Playlist.Tracks.Length != Playlist.TrackCount) throw new SoundCloudException("SoundCloud не подтвердил полный список треков плейлиста.");
            if (request.Kind == "remove") Playlist = Playlist with { Tracks = Playlist.Tracks.Where(track => track.Id != request.TrackId).ToArray(), TrackCount = Playlist.TrackCount - 1 };
            if (request.Kind == "delete") Deleted = true;
            return JsonSerializer.SerializeToDocument(Playlist, SoundCloudJson.Default.SoundCloudPlaylist);
        }
    }
}
