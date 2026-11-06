using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyMobilePlaylistEditingUiAsync()
    {
        var fixture = new MobilePlaylistEditingFixture(new CollectionFixtureTransport().Playlist);
        api.Session = new("fixture", "mobile-fixture", "Test") { MobileIdentity = new("0123456789abcdef", "Fixture", "Desktop", "17") };
        api.Transport = fixture; me = new() { Id = 42, Username = "Owner" };
        signedIn.Value = true;
        Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
        await ShowWorkspaceAsync();
        await imageDiskCache.WriteAsync(fixture.Playlist.ArtworkUrl!, ArtworkFixtureBitmap(500), lifetime.Token);
        ClearLibraryData(); ResetFollowing(); ResetReposts();
        var item = LibraryData.FromPlaylist(fixture.Playlist);
        await OpenLibraryItemAsync(item);
        await WaitForLikedLayoutAsync(() => compactTrackRows.Any(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window));
        if (!CanEditDisplayedPlaylist() || !HasCompletePlaylistOrder()) throw new InvalidOperationException("Owned mobile playlist has no editing actions.");
        var row = compactTrackRows.First(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window);
        foreach (var id in new[] { "remove", "up", "down" })
            if (!row.Root.ContextMenu!.Items.OfType<MenuItem>().Any(entry => entry.Command?.Id == "pookie.playlist." + id))
                throw new InvalidOperationException("Missing playlist track menu command: " + id);
        OpenPlaylistEditor();
        await WaitForLikedLayoutAsync(() => playlistEditor.IsOpen && !playlistEditor.Running);
        playlistEditName.Text = "Обновлённый плейлист 🎵"; playlistEditDescription.Text = "Первая строка\nВторая строка";
        playlistEditPrivate = false; RefreshPlaylistEditor();
        await WaitForLikedLayoutAsync(() => !playlistEditPrivacy.Animating);
        CaptureUiPreview("mobile-playlist-editor");
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
            throw new InvalidOperationException("Hidden tracks blocked metadata editing or enabled a filtered order replacement.");
        var retained = fixture.Playlist.Tracks.Select(track => track.Id).ToArray();
        playlistEditName.Text = "Плейлист со скрытыми треками";
        await SavePlaylistEditorAsync();
        if (fixture.Playlist.TrackCount != 15 || !fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(retained) || collectionTitle.Text != "Плейлист со скрытыми треками")
            throw new InvalidOperationException("Metadata editing changed hidden count or visible order.");
        await EditPlaylistTrackAsync(activeCollection.Items[0].Track!, 0, 0);
        if (fixture.Playlist.TrackCount != 14) throw new InvalidOperationException("Unavailable tracks blocked individual removal.");
        OpenPlaylistEditor(deleting: true);
        await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        CaptureUiPreview("mobile-playlist-delete-confirmation");
        if (!playlistEditor.IsOpen || !playlistEditDeleting || !playlistEditSave.IsEnabled || fixture.Deleted)
            throw new InvalidOperationException("Playlist deletion skipped its confirmation.");
        await SavePlaylistEditorAsync();
        if (!fixture.Deleted || page.Value != Page.LibraryPlaylists || libraryPages["collections"].Items.Any(entry => entry.Playlist?.Id == fixture.Playlist.Id))
            throw new InvalidOperationException("Deleted playlist remained in the library.");
        Console.WriteLine("MOBILE_PLAYLIST_EDIT_UI_OK: mobile open, owner menus, metadata/privacy confirmation, pending isolation, reorder, remove, hidden-track protection and deletion dialog/library refresh; fixture only");
    }

    private sealed class MobilePlaylistEditingFixture : ISoundCloudTransport, ISoundCloudMobileApi
    {
        internal SoundCloudPlaylist Playlist;
        internal bool Deleted;
        internal TaskCompletionSource? UpdateHold;
        internal MobilePlaylistEditingFixture(SoundCloudPlaylist playlist) => Playlist = playlist with { User = new() { Id = 42, Username = "Owner" }, TrackPagesComplete = true };
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Mobile playlist UI reached a web API: " + uri.AbsolutePath);
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SoundCloudMobileConfiguration> GetMobileConfigurationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JsonDocument> GetMobileSduiAsync(bool catalog = false, string? after = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JsonDocument> ResolveMobileStreamAsync(Uri uri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SoundCloudPlaylist> GetMobilePlaylistAsync(long playlistId, CancellationToken cancellationToken = default) => Task.FromResult(Playlist);
        public Task<LibraryItem[]> GetMobileCollectionsAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Deleted ? Array.Empty<LibraryItem>() : new[] { LibraryData.FromPlaylist(Playlist) });
        public Task<HashSet<long>> GetMobileActionIdsAsync(string kind, bool reposts = false, long? userId = null, CancellationToken cancellationToken = default) => Task.FromResult(new HashSet<long>());
        public async Task<SoundCloudPlaylist> UpdateMobilePlaylistAsync(PlaylistUpdateRequest request, CancellationToken cancellationToken = default)
        {
            if (UpdateHold != null) { await UpdateHold.Task.WaitAsync(cancellationToken); UpdateHold = null; }
            Playlist = Playlist with { Title = request.Title, Description = request.Description, Sharing = request.Sharing,
                Tracks = request.TrackIds?.Select(id => Playlist.Tracks.First(track => track.Id == id)).ToArray() ?? Playlist.Tracks };
            return Playlist;
        }
        public Task RemoveMobilePlaylistTrackAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken = default)
        { Playlist = Playlist with { Tracks = Playlist.Tracks.Where(track => track.Id != trackId).ToArray(), TrackCount = Playlist.TrackCount - 1 }; return Task.CompletedTask; }
        public Task DeleteMobilePlaylistAsync(long userId, long playlistId, CancellationToken cancellationToken = default) { Deleted = true; return Task.CompletedTask; }
    }
}
