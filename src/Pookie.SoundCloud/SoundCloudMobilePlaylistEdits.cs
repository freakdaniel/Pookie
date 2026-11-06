using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudHttpTransport
{
    private async Task<SoundCloudPlaylist> EditableMobilePlaylistAsync(WebSession account, long userId, long playlistId, CancellationToken token)
    {
        ValidateId(userId); ValidateId(playlistId);
        if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
        if (account.MobileIdentity == null || await UserIdAsync(token).ConfigureAwait(false) != userId)
            throw new SoundCloudException("Для редактирования нужен мобильный профиль владельца.");
        var playlist = await GetMobilePlaylistAsync(playlistId, token).ConfigureAwait(false);
        if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
        if (playlist.User?.Id != userId || playlist.IsAlbum || playlist.IsSystemPlaylist || LibraryData.FromPlaylist(playlist).IsAlbum)
            throw new SoundCloudException("Этот плейлист нельзя редактировать.");
        return playlist;
    }

    public async Task RemoveMobilePlaylistTrackAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken = default)
    {
        ValidateId(trackId);
        var account = Account();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await playlistEdits.WaitAsync(stop.Token).ConfigureAwait(false);
        try
        {
            var playlist = await EditableMobilePlaylistAsync(account, userId, playlistId, stop.Token).ConfigureAwait(false);
            if (!playlist.Tracks.Any(track => track.Id == trackId)) return;
            using var removed = await ExecuteAsync(HttpMethod.Delete, SoundCloudMobileRoutes.Uri($"/playlists/{SoundCloudMobileRoutes.Urn("playlists", playlistId)}/tracks/{SoundCloudMobileRoutes.Urn("tracks", trackId)}"),
                null, null, stop.Token, expectJson: false, expectedAccount: account).ConfigureAwait(false);
            var confirmed = await GetMobilePlaylistAsync(playlistId, stop.Token).ConfigureAwait(false);
            if (confirmed.Tracks.Any(track => track.Id == trackId))
                throw new SoundCloudException("SoundCloud не подтвердил удаление трека. Обнови плейлист.");
        }
        finally { playlistEdits.Release(); }
    }

    public async Task<SoundCloudPlaylist> UpdateMobilePlaylistAsync(PlaylistUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsValid()) throw new ArgumentException("Проверь название и описание плейлиста.");
        var account = Account();
        // Freeze caller-owned arrays before waiting on another playlist edit.
        request = request with { TrackIds = request.TrackIds?.ToArray(), ExpectedTrackIds = request.ExpectedTrackIds?.ToArray() };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await playlistEdits.WaitAsync(stop.Token).ConfigureAwait(false);
        try
        {
            var playlist = await EditableMobilePlaylistAsync(account, request.UserId, request.PlaylistId, stop.Token).ConfigureAwait(false);
            var ids = playlist.Tracks.Select(track => track.Id).ToArray();
            var replacingOrder = request.TrackIds != null;
            // Metadata-only PUT must omit every track field. A full replacement
            // is allowed only when all track IDs were enumerated.
            if (!playlist.TrackPagesComplete)
                throw new SoundCloudException("Не завершена загрузка плейлиста. Открой его ещё раз.");
            if (replacingOrder && ids.Length != playlist.TrackCount)
                throw new SoundCloudException("Для изменения порядка нужны ID всех треков. Название, описание и приватность можно менять без замены списка.");
            if (replacingOrder && request.ExpectedTrackIds != null && !ids.SequenceEqual(request.ExpectedTrackIds))
                throw new SoundCloudException("Состав плейлиста изменился. Обнови его перед сохранением.");
            var order = request.TrackIds ?? ids;
            if (!order.Order().SequenceEqual(ids.Order()))
                throw new SoundCloudException("Изменение порядка должно сохранить все треки плейлиста.");
            using var response = await ExecuteAsync(HttpMethod.Put, SoundCloudMobileRoutes.Uri($"/playlists/{SoundCloudMobileRoutes.Urn("playlists", request.PlaylistId)}"), Json(writer =>
            {
                writer.WriteStartObject();
                if (replacingOrder)
                {
                    writer.WriteStartArray("track_urns");
                    foreach (var id in order) writer.WriteStringValue(SoundCloudMobileRoutes.Urn("tracks", id));
                    writer.WriteEndArray();
                    writer.WriteString("genre", playlist.Genre ?? "");
                    writer.WriteString("tag_list", string.Join(" ", playlist.Tags ?? []));
                }
                writer.WriteString("description", request.Description); writer.WriteString("title", request.Title);
                writer.WriteBoolean("public", request.Sharing == "public"); writer.WriteEndObject();
            }), null, stop.Token, expectJson: false, expectedAccount: account).ConfigureAwait(false);
            var confirmed = await GetMobilePlaylistAsync(request.PlaylistId, stop.Token).ConfigureAwait(false);
            if (confirmed.User?.Id != request.UserId || confirmed.Title != request.Title || confirmed.Sharing != request.Sharing ||
                (confirmed.Description ?? "") != request.Description || confirmed.TrackCount != (replacingOrder ? order.Length : playlist.TrackCount) ||
                !confirmed.Tracks.Select(track => track.Id).SequenceEqual(order))
                throw new SoundCloudException("SoundCloud не подтвердил изменение плейлиста. Обнови страницу перед повтором.");
            return confirmed;
        }
        finally { playlistEdits.Release(); }
    }

    public async Task DeleteMobilePlaylistAsync(long userId, long playlistId, CancellationToken cancellationToken = default)
    {
        var account = Account();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await playlistEdits.WaitAsync(stop.Token).ConfigureAwait(false);
        try
        {
            await EditableMobilePlaylistAsync(account, userId, playlistId, stop.Token).ConfigureAwait(false);
            using var removed = await ExecuteAsync(HttpMethod.Delete, SoundCloudMobileRoutes.Uri($"/playlists/{SoundCloudMobileRoutes.Urn("playlists", playlistId)}"),
                null, null, stop.Token, expectJson: false, expectedAccount: account).ConfigureAwait(false);
            try { await GetMobilePlaylistAsync(playlistId, stop.Token).ConfigureAwait(false); }
            catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 404 or 410) { return; }
            // The mobile info service can briefly retain a deleted resource.
            // A complete owner collection independently confirms its removal.
            var owned = await GetMobileOwnedPlaylistsAsync(userId, stop.Token).ConfigureAwait(false);
            if (!owned.Any(item => item.Playlist!.Id == playlistId)) return;
            throw new SoundCloudException("SoundCloud не подтвердил удаление плейлиста. Обнови библиотеку.");
        }
        finally { playlistEdits.Release(); }
    }

    public async Task<SoundCloudPlaylist> SetMobilePlaylistArtworkAsync(long userId, long playlistId, byte[] jpeg, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length is < 4 or > 5 * 1024 * 1024 || jpeg[0] != 0xff || jpeg[1] != 0xd8 || jpeg[2] != 0xff)
            throw new ArgumentException("Нужна обложка JPEG размером до 5 МБ.");
        var data = Convert.ToBase64String(jpeg);
        var account = Account();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await playlistEdits.WaitAsync(stop.Token).ConfigureAwait(false);
        try
        {
            await EditableMobilePlaylistAsync(account, userId, playlistId, stop.Token).ConfigureAwait(false);
            using var response = await ExecuteAsync(HttpMethod.Put, SoundCloudMobileRoutes.Uri($"/playlists/{SoundCloudMobileRoutes.Urn("playlists", playlistId)}/artwork"),
                Json(writer => { writer.WriteStartObject(); writer.WriteString("image_data", data); writer.WriteEndObject(); }), null, stop.Token, expectJson: false, expectedAccount: account).ConfigureAwait(false);
            var confirmed = await GetMobilePlaylistAsync(playlistId, stop.Token).ConfigureAwait(false);
            if (confirmed.User?.Id != userId || string.IsNullOrEmpty(confirmed.ArtworkUrl))
                throw new SoundCloudException("SoundCloud не подтвердил новую обложку. Обнови плейлист.");
            return confirmed;
        }
        finally { playlistEdits.Release(); }
    }
}
