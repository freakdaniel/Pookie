namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    private ISoundCloudMobileApi MobileApi => Session?.MobileIdentity != null && Transport is ISoundCloudMobileApi mobile ? mobile :
        throw new SoundCloudException("Мобильное соединение SoundCloud ещё не подключено.");

    private async Task<LibraryPage> ReadMobileLibraryCursorAsync(Uri uri, CancellationToken token)
    {
        var account = Session;
        var me = await GetMeAsync(token);
        if (Session != account) throw new SoundCloudException("Сессия SoundCloud изменилась.");
        var section = uri.AbsolutePath == $"/followings/{SoundCloudMobileRoutes.Urn("users", me.Id)}/users" ? "following" :
            new[] { "likes", "reposts" }.FirstOrDefault(name => uri.AbsolutePath == $"/users/{SoundCloudMobileRoutes.Urn("users", me.Id)}/{name}") ??
            throw new SoundCloudException("Курсор не относится к мобильной библиотеке профиля.");
        return await MobileApi.GetMobileLibraryPageAsync(me.Id, section, uri.AbsoluteUri, token);
    }

    private void InvalidatePlaylistMetadata(long playlistId)
    {
        lock (libraryMetadataGate)
        {
            libraryMetadata.Remove($"playlists/{playlistId}");
            libraryMetadata.Remove($"playlists/{playlistId}?representation=full");
        }
    }

    public async Task RemovePlaylistTrackAsync(long userId, long playlistId, long trackId, CancellationToken token = default)
    {
        try { await MobileApi.RemoveMobilePlaylistTrackAsync(userId, playlistId, trackId, token); }
        finally { InvalidatePlaylistMetadata(playlistId); }
    }

    public async Task<SoundCloudPlaylist> UpdatePlaylistAsync(PlaylistUpdateRequest request, CancellationToken token = default)
    {
        try { return await MobileApi.UpdateMobilePlaylistAsync(request, token); }
        finally { InvalidatePlaylistMetadata(request.PlaylistId); }
    }

    public async Task DeletePlaylistAsync(long userId, long playlistId, CancellationToken token = default)
    {
        try { await MobileApi.DeleteMobilePlaylistAsync(userId, playlistId, token); }
        finally { InvalidatePlaylistMetadata(playlistId); }
    }

    public async Task<SoundCloudPlaylist> SetPlaylistArtworkAsync(long userId, long playlistId, byte[] jpeg, CancellationToken token = default)
    {
        try { return await MobileApi.SetMobilePlaylistArtworkAsync(userId, playlistId, jpeg, token); }
        finally { InvalidatePlaylistMetadata(playlistId); }
    }
}
