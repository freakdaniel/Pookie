using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<LibraryItem[]> GetOwnedPlaylistsAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы добавить трек в плейлист.");
        var items = new List<LibraryItem>();
        var path = $"/users/{userId}/playlists";
        Uri? target = Api(path.TrimStart('/') + "?limit=50&linked_partitioning=1&show_tracks=false");
        var seen = new HashSet<string>();
        while (target != null)
        {
            if (seen.Count >= 1000 || !BrowserRequestCommand.IsReadUrl(target.AbsoluteUri) || target.AbsolutePath != path || !seen.Add(target.AbsoluteUri))
                throw new SoundCloudException("Неверный адрес страницы плейлистов.");
            using var document = await GetJsonAsync(target, token);
            var page = LibraryData.Parse(document.RootElement);
            items.AddRange(page.Items.Where(item => item.Playlist is { Id: > 0 } playlist && playlist.User?.Id == userId && !item.IsAlbum));
            target = page.NextHref == null ? null : Uri.TryCreate(page.NextHref, UriKind.Absolute, out var next) ? next :
                throw new SoundCloudException("Неверный адрес страницы плейлистов.");
        }
        return items.DistinctBy(item => item.Playlist!.Id).ToArray();
    }

    public async Task<HashSet<long>> GetFollowingIdsAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        if (Session == null) throw new SoundCloudException("Для подписок нужен вход в SoundCloud.");
        var ids = new HashSet<long>(); var seen = new HashSet<string>();
        var path = $"/users/{userId}/followings/ids";
        Uri? target = Api(path.TrimStart('/') + "?limit=200&linked_partitioning=1");
        while (target != null)
        {
            if (seen.Count >= 1000 || !BrowserRequestCommand.IsReadUrl(target.AbsoluteUri) || target.AbsolutePath != path || !seen.Add(target.AbsoluteUri))
                throw new SoundCloudException("Неверный адрес страницы подписок.");
            using var document = await GetJsonAsync(target, token);
            var root = document.RootElement;
            var rows = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
            foreach (var row in rows.EnumerateArray())
            {
                long id = 0;
                if (row.ValueKind == JsonValueKind.Number) row.TryGetInt64(out id);
                else if (row.ValueKind == JsonValueKind.String) long.TryParse(row.GetString(), out id);
                if (id is > 0 and <= 9007199254740991) ids.Add(id);
            }
            target = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_href", out var href) && href.ValueKind == JsonValueKind.String && href.GetString() is { } url
                ? Uri.TryCreate(url, UriKind.Absolute, out var next) ? next : throw new SoundCloudException("Неверный адрес страницы подписок.") : null;
        }
        return ids;
    }

    public async Task SetFollowingAsync(long userId, long artistId, bool following, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId); ValidateDetailTrackId(artistId);
        if (userId == artistId) throw new ArgumentException("Нельзя подписаться на свой профиль.");
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы подписаться.");
        if (BrowserTransport is not { } browser) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        await browser.SetFollowingAsync(userId, artistId, following, token);
    }

    public async Task AddToPlaylistAsync(long userId, long playlistId, long trackId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId); ValidateDetailTrackId(playlistId); ValidateDetailTrackId(trackId);
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы добавить трек в плейлист.");
        if (BrowserTransport is not { } browser) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        await browser.AddToPlaylistAsync(userId, playlistId, trackId, token);
        lock (libraryMetadataGate) libraryMetadata.Remove($"playlists/{playlistId}?representation=full");
    }
}
