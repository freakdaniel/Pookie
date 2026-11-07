using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    private void InvalidatePlaylistMetadata(long id)
    {
        lock (libraryMetadataGate) { libraryMetadata.Remove($"playlists/{id}"); libraryMetadata.Remove($"playlists/{id}?representation=full"); }
    }
    private async Task<SoundCloudPlaylist> EditWebPlaylistAsync(PlaylistEditRequest edit, CancellationToken token)
    {
        if (!edit.IsValid()) throw new ArgumentException("Некорректное изменение плейлиста.");
        if (Session == null || BrowserTransport == null) throw new SoundCloudException("Войди в SoundCloud, чтобы изменить плейлист.");
        try
        {
            using var result = await BrowserTransport.EditPlaylistAsync(edit, token);
            if (edit.Kind == "delete") return new() { Id = edit.PlaylistId };
            var playlist = LibraryData.ParsePlaylist(result.RootElement);
            if (playlist.Id != edit.PlaylistId || playlist.User?.Id != edit.UserId || playlist.IsAlbum || playlist.IsSystemPlaylist ||
                edit.Update is { } update && (playlist.Title != update.Title || (playlist.Description ?? "") != update.Description || playlist.Sharing != update.Sharing ||
                    update.TrackIds != null && !playlist.Tracks.Select(track => track.Id).SequenceEqual(update.TrackIds)) ||
                edit.Kind == "remove" && playlist.Tracks.Any(track => track.Id == edit.TrackId))
                throw new SoundCloudException("SoundCloud не подтвердил изменение плейлиста.");
            return playlist;
        }
        finally { InvalidatePlaylistMetadata(edit.PlaylistId); }
    }
    public async Task RemovePlaylistTrackAsync(long userId, long playlistId, long trackId, CancellationToken token = default) =>
        _ = await EditWebPlaylistAsync(new("remove", userId, playlistId, TrackId: trackId), token);
    public Task<SoundCloudPlaylist> UpdatePlaylistAsync(PlaylistUpdateRequest request, CancellationToken token = default) =>
        EditWebPlaylistAsync(new("update", request.UserId, request.PlaylistId, request), token);
    public async Task DeletePlaylistAsync(long userId, long playlistId, CancellationToken token = default) =>
        _ = await EditWebPlaylistAsync(new("delete", userId, playlistId), token);
    public Task<SoundCloudPlaylist> SetPlaylistArtworkAsync(long userId, long playlistId, byte[] jpeg, CancellationToken token = default) =>
        EditWebPlaylistAsync(new("artwork", userId, playlistId, Artwork: Convert.ToBase64String(jpeg)), token);
}
