using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ModalDialog playlistEditor = null!;
    private TextBox playlistEditName = null!;
    private MultiLineTextBox playlistEditDescription = null!;
    private PlaylistPrivacySelector playlistEditPrivacy = null!;
    private TextBlock playlistEditHeading = null!, playlistEditMessage = null!;
    private StackPanel playlistEditFields = null!;
    private Button playlistEditSave = null!, playlistEditCancel = null!;
    private LibraryItem? playlistEditTarget;
    private WebSession? playlistEditSession;
    private bool playlistEditPrivate, playlistEditDeleting;
    private readonly HashSet<(WebSession Session, long Playlist)> pendingPlaylistEdits = [];

    private bool CanEditDisplayedPlaylist() => !demo && page.Value == Page.LibraryCollection && me is { } user &&
        api.Session is { } session && displayedCollection is { Playlist: { Id: > 0, IsSystemPlaylist: false } playlist } &&
        playlist.User?.Id == user.Id && !pendingPlaylistEdits.Contains((session, playlist.Id));

    private bool HasCompletePlaylistOrder() => displayedCollection?.Playlist is { TrackPagesComplete: true } playlist &&
        playlist.Tracks.Length == playlist.TrackCount && activeCollection?.Items.Length == playlist.Tracks.Length;

    private void ClosePlaylistEditor()
    {
        if (playlistEditSession is { } session && playlistEditTarget?.Playlist is { } playlist && pendingPlaylistEdits.Contains((session, playlist.Id))) return;
        playlistEditType.CloseMenu(); playlistEditLicense.CloseMenu();
        playlistEditor?.Close(); playlistEditTarget = null; playlistEditSession = null;
    }

    private void ResetPlaylistEditor()
    {
        playlistEditType?.CloseMenu(); playlistEditLicense?.CloseMenu();
        playlistEditor?.Close(immediate: true); playlistEditTarget = null; playlistEditSession = null;
    }

    private void OpenPlaylistEditor(bool deleting = false)
    {
        if (!CanEditDisplayedPlaylist()) return;
        playlistEditTarget = displayedCollection; playlistEditSession = api.Session;
        var playlist = playlistEditTarget!.Playlist!;
        playlistEditDeleting = deleting;
        playlistEditLoadingFields = true;
        ClearPlaylistEditArtwork();
        playlistEditTabs.IsVisible = !deleting; playlistEditHeading.IsVisible = deleting;
        playlistEditDeleteText.IsVisible = deleting;
        ((TextBlock)playlistEditDeleteText).Text = $"Плейлист «{playlist.Title}» будет удалён из SoundCloud.";
        playlistEditFields.IsVisible = !deleting;
        playlistEditName.Text = playlist.Title; playlistEditDescription.Text = playlist.Description ?? "";
        playlistEditPrivate = playlist.Sharing == "private";
        playlistEditOriginalPermalink = playlist.Permalink ?? playlist.PermalinkUrl?.Split('/').LastOrDefault() ?? "";
        playlistEditOriginalDate = PlaylistEditDateValue(playlist);
        playlistEditOriginalType = playlist.PlaylistType ?? (playlist.IsAlbum ? "album" : "playlist");
        playlistEditPermalink.Text = playlistEditOriginalPermalink; playlistEditDate.Text = playlistEditOriginalDate;
        playlistEditGenre.Text = playlist.Genre ?? ""; playlistEditTags.Text = playlist.TagList ?? "";
        playlistEditBuy.Text = playlist.PurchaseUrl ?? ""; playlistEditLabel.Text = playlist.LabelName ?? "";
        playlistEditType.Select(playlistEditOriginalType); playlistEditLicense.Select(playlist.License ?? "all-rights-reserved");
        playlistEditTracks.Clear(); playlistEditTracks.AddRange(playlist.Tracks);
        playlistEditOrderComplete = playlist.TrackPagesComplete && playlist.Tracks.Length == playlist.TrackCount;
        RenderPlaylistEditTracks(); playlistEditTab.Value = 0; playlistEditPages.Show(0, animate: false);
        SetCollectionArtwork(playlistEditArtwork, playlistEditTarget);
        var artworkTarget = playlistEditTarget;
        var artworkSession = playlistEditSession;
        Run(async () =>
        {
            var image = await GetCollectionArtworkAsync(artworkTarget);
            if (!disposed && playlistEditTarget == artworkTarget && playlistEditSession == artworkSession && playlistEditArtworkBytes == null)
                SetCollectionArtwork(playlistEditArtwork, artworkTarget, image, finished: true);
        });
        SetPlaylistEditMessage(""); playlistEditLoadingFields = false;
        ((TextBlock)playlistEditSave.Content!).Text = deleting ? "Удалить" : "Сохранить";
        RefreshPlaylistEditor(); playlistEditor.Show();
    }

    private void RefreshPlaylistEditor()
    {
        if (playlistEditSave == null) return;
        var target = playlistEditTarget?.Playlist;
        var busy = playlistEditChoosingArtwork || playlistEditSession is { } session && target != null && pendingPlaylistEdits.Contains((session, target.Id));
        playlistEditFields.IsEnabled = playlistEditTabs.IsEnabled = !busy;
        playlistEditCancel.IsEnabled = playlistEditClose.IsEnabled = !busy;
        playlistEditPrivacy.Select(playlistEditPrivate);
        var request = target == null ? null : PlaylistEditorRequest(target);
        ValidatePlaylistEdit(request);
        var valid = playlistEditDeleting || request?.IsValid() == true && PlaylistEditorDirty(target!, request);
        TrackButtons.SetAvailability(playlistEditSave, valid && !busy && target != null && api.Session == playlistEditSession, busy);
        FitPlaylistEditor();
    }

    private PlaylistUpdateRequest PlaylistEditorRequest(SoundCloudPlaylist playlist)
    {
        var ids = playlistEditTracks.Select(track => track.Id).ToArray();
        var changed = playlistEditOrderComplete && !ids.SequenceEqual(playlist.Tracks.Select(track => track.Id));
        var metadata = PlaylistEditMetadata(playlist);
        return new(me?.Id ?? 0, playlist.Id, playlistEditName.Text.Trim(), playlistEditDescription.Text,
            playlistEditPrivate ? "private" : "public", changed ? ids : null,
            changed ? playlist.Tracks.Select(track => track.Id).ToArray() : null, metadata.HasChanges() ? metadata : null);
    }
    private bool PlaylistEditorDirty(SoundCloudPlaylist playlist, PlaylistUpdateRequest request) =>
        request.Title != playlist.Title || request.Description != (playlist.Description ?? "") || request.Sharing != playlist.Sharing ||
        request.TrackIds != null || request.Metadata != null || playlistEditArtworkBytes != null;

    private async Task SavePlaylistEditorAsync()
    {
        if (!playlistEditSave.IsEnabled || playlistEditTarget is not { Playlist: { } playlist } item || playlistEditSession is not { } session || api.Session != session || me is not { } user ||
            !pendingPlaylistEdits.Add((session, playlist.Id))) return;
        var deleting = playlistEditDeleting;
        var request = PlaylistEditorRequest(playlist);
        var artwork = playlistEditArtworkBytes;
        var confirmed = false;
        SetPlaylistEditMessage(""); RefreshPlaylistEditor();
        try
        {
            SoundCloudPlaylist? updated = null;
            if (deleting) await api.DeletePlaylistAsync(user.Id, playlist.Id, lifetime.Token);
            else updated = await api.UpdatePlaylistAsync(request, lifetime.Token);
            confirmed = true;
            if (disposed || api.Session != session) return;
            InvalidatePlaylistLibrary(playlist.Id);
            if (updated != null)
            {
                ApplyEditedPlaylist(item, updated);
                // Keep confirmed metadata as the new baseline if a subsequent
                // artwork upload fails. Retrying must not restore old metadata.
                playlistEditTarget = item with { Playlist = updated };
                playlistEditOriginalPermalink = updated.Permalink ?? updated.PermalinkUrl?.Split('/').LastOrDefault() ?? "";
                playlistEditOriginalDate = PlaylistEditDateValue(updated);
                playlistEditOriginalType = updated.PlaylistType ?? (updated.IsAlbum ? "album" : "playlist");
                if (artwork != null)
                {
                    var withArtwork = await api.SetPlaylistArtworkAsync(user.Id, playlist.Id, artwork, lifetime.Token);
                    if (disposed || api.Session != session) return;
                    ApplyEditedPlaylist(item, withArtwork);
                }
            }
            playlistEditor.Close(); playlistEditTarget = null; playlistEditSession = null;
            if (deleting)
            {
                likedPlaylistIds.Remove(playlist.Id); repostedPlaylistIds.Remove(playlist.Id);
                if (displayedCollection?.Playlist?.Id == playlist.Id) await ShowLibrarySectionAsync(Page.LibraryPlaylists);
            }
            await RefreshEditedPlaylistLibraryAsync(session);
            if (!disposed && api.Session == session) status.Value = deleting ? "Плейлист удалён." : "Плейлист сохранён.";
        }
        catch (Exception error)
        {
            if (!disposed && api.Session == session)
            {
                InvalidatePlaylistLibrary(playlist.Id, details: !confirmed);
                status.Value = confirmed ? "Основные изменения сохранены. " + FriendlyError(error) : FriendlyError(error);
                if (playlistEditTarget?.Playlist?.Id == playlist.Id) SetPlaylistEditMessage(confirmed
                    ? "Основные изменения сохранены. " + FriendlyError(error) : FriendlyError(error));
            }
        }
        finally
        {
            pendingPlaylistEdits.Remove((session, playlist.Id));
            if (!disposed) RefreshPlaylistEditor();
        }
    }

    private void ApplyEditedPlaylist(LibraryItem previous, SoundCloudPlaylist playlist)
    {
        var item = LibraryData.FromPlaylist(playlist) with { Key = previous.Key };
        var tracks = playlist.Tracks.Where(track => !string.IsNullOrWhiteSpace(track.Title)).ToArray();
        collectionDetails[previous.Key] = new(item, new(tracks, null));
        if (page.Value != Page.LibraryCollection || displayedCollection?.Key != previous.Key) return;
        activeCollection = new(tracks.Select(LibraryItem.FromTrack).ToArray(), null);
        librarySectionTitle.Value = item.Title;
        RenderLibrarySection();
    }

    private async Task ReloadEditedPlaylistAsync(LibraryItem item, WebSession session)
    {
        var generation = navigationGeneration;
        var detail = await api.GetCollectionDetailAsync(item, lifetime.Token);
        if (disposed || api.Session != session) return;
        collectionDetails[item.Key] = detail;
        if (page.Value == Page.LibraryCollection && displayedCollection?.Key == item.Key && navigationGeneration == generation)
        {
            activeCollection = new(detail.Tracks.Tracks.Select(LibraryItem.FromTrack).ToArray(), detail.Tracks.NextHref);
            librarySectionTitle.Value = detail.Item.Title; RenderLibrarySection();
        }
    }

    private async Task RefreshEditedPlaylistLibraryAsync(WebSession session)
    {
        if (disposed || api.Session != session) return;
        await LoadLibrarySourceAsync("collections");
        if (disposed || api.Session != session) return;
        RefreshOverviewSections();
        if (page.Value is Page.LibraryPlaylists or Page.LibraryAlbums) RenderLibrarySection();
    }

    private void AttachPlaylistTrackEdits(FrameworkElement root, Func<SoundCloudTrack?> track, Func<int> position)
    {
        if (root.ContextMenu is not { } menu) return;
        void Item(string id, string label, string icon, Action action, Func<bool> enabled)
        {
            var command = PopupAppearance.MenuCommand("pookie.playlist." + id, label, icon);
            root.Commands.Register(command, action, enabled); menu.Item(command);
        }
        Item("remove", "Удалить из плейлиста", "x", () => { if (track() is { } selected) Run(() => EditPlaylistTrackAsync(selected, position(), 0)); },
            () => track() != null && CanEditDisplayedPlaylist() && HasCompletePlaylistOrder());
        Item("up", "Выше в плейлисте", "caret-up", () => { if (track() is { } selected) Run(() => EditPlaylistTrackAsync(selected, position(), -1)); },
            () => track() != null && CanEditDisplayedPlaylist() && HasCompletePlaylistOrder() && position() > 0);
        Item("down", "Ниже в плейлисте", "caret-down", () => { if (track() is { } selected) Run(() => EditPlaylistTrackAsync(selected, position(), 1)); },
            () => track() != null && CanEditDisplayedPlaylist() && HasCompletePlaylistOrder() && position() >= 0 && position() < activeCollection!.Items.Length - 1);
    }

    private async Task EditPlaylistTrackAsync(SoundCloudTrack track, int position, int direction)
    {
        if (!CanEditDisplayedPlaylist() || displayedCollection is not { Playlist: { } playlist } item || api.Session is not { } session || me is not { } user ||
            position < 0 || position >= activeCollection!.Items.Length || activeCollection.Items[position].Track?.Id != track.Id ||
            direction != 0 && (!HasCompletePlaylistOrder() || position + direction < 0 || position + direction >= activeCollection.Items.Length) ||
            !pendingPlaylistEdits.Add((session, playlist.Id))) return;
        try
        {
            SoundCloudPlaylist? updated = null;
            if (direction == 0) await api.RemovePlaylistTrackAsync(user.Id, playlist.Id, track.Id, lifetime.Token);
            else
            {
                var ids = playlist.Tracks.Select(entry => entry.Id).ToArray(); var order = ids.ToArray();
                (order[position], order[position + direction]) = (order[position + direction], order[position]);
                updated = await api.UpdatePlaylistAsync(new(user.Id, playlist.Id, playlist.Title, playlist.Description ?? "", playlist.Sharing ?? "private", order, ids), lifetime.Token);
            }
            if (disposed || api.Session != session) return;
            InvalidatePlaylistLibrary(playlist.Id);
            if (updated != null) ApplyEditedPlaylist(item, updated);
            else await ReloadEditedPlaylistAsync(item, session);
            await RefreshEditedPlaylistLibraryAsync(session);
            if (!disposed && api.Session == session) status.Value = direction == 0 ? "Трек удалён из плейлиста." : "Порядок треков сохранён.";
        }
        catch
        {
            if (!disposed && api.Session == session) InvalidatePlaylistLibrary(playlist.Id);
            throw;
        }
        finally { pendingPlaylistEdits.Remove((session, playlist.Id)); }
    }

    private async Task PickPlaylistArtworkAsync()
    {
        if (!CanEditDisplayedPlaylist() || displayedCollection is not { Playlist: { } playlist } item || api.Session is not { } session || me is not { } user) return;
        var files = await FileDialog.OpenFilesAsync(new OpenFileDialogOptions { Owner = Window, Title = "Обложка плейлиста", Multiselect = false,
            Filters = [new FileFilter("JPEG до 5 МБ", "*.jpg", "*.jpeg")] }, lifetime.Token);
        if (files is not { Length: > 0 } || disposed || api.Session != session || !pendingPlaylistEdits.Add((session, playlist.Id))) return;
        try
        {
            var file = new FileInfo(files[0]);
            if (file.Length > 5 * 1024 * 1024) throw new ArgumentException("Нужна обложка JPEG размером до 5 МБ.");
            var bytes = await File.ReadAllBytesAsync(file.FullName, lifetime.Token);
            await Task.Run(() => Storage.ArtworkImage.Prepare(bytes), lifetime.Token);
            if (disposed || api.Session != session) return;
            var updated = await api.SetPlaylistArtworkAsync(user.Id, playlist.Id, bytes, lifetime.Token);
            if (disposed || api.Session != session) return;
            InvalidatePlaylistLibrary(playlist.Id); ApplyEditedPlaylist(item, updated);
            await RefreshEditedPlaylistLibraryAsync(session);
            if (!disposed && api.Session == session) status.Value = "Обложка плейлиста обновлена.";
        }
        finally { pendingPlaylistEdits.Remove((session, playlist.Id)); }
    }
}
