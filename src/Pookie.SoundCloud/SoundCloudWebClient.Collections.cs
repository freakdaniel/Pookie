using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<SoundCloudPlaylist> CreatePlaylistAsync(long userId, string title, string sharing, long trackId, CancellationToken token = default)
    {
        var request = new PlaylistCreateRequest(userId, trackId, title.Trim(), sharing);
        if (!request.IsValid()) throw new ArgumentException("Укажи название плейлиста длиной до 255 символов.");
        if (Session?.IsValid() != true || BrowserTransport == null) throw new SoundCloudException("Войди в SoundCloud, чтобы создать плейлист.");
        using var document = await BrowserTransport.CreatePlaylistAsync(request, token);
        if (!request.Confirms(document.RootElement)) throw new SoundCloudException("SoundCloud не подтвердил создание плейлиста.");
        return document.RootElement.Deserialize(SoundCloudJson.Default.SoundCloudPlaylist)!;
    }

    public async Task<LibraryItem[]> GetOwnedPlaylistsAsync(long userId, CancellationToken token = default, bool includeTracks = false)
    {
        ValidateDetailTrackId(userId);
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы добавить трек в плейлист.");
        if (Session.MobileIdentity != null)
        {
            if (Transport is not ISoundCloudMobileApi mobile)
                throw new SoundCloudException("Мобильное соединение SoundCloud ещё не подключено.");
            return await mobile.GetMobileOwnedPlaylistsAsync(userId, token);
        }
        var items = new List<LibraryItem>();
        var path = $"/users/{userId}/playlists";
        Uri? target = Api(path.TrimStart('/') + "?limit=50&linked_partitioning=1&show_tracks=" + (includeTracks ? "true" : "false"));
        var seen = new HashSet<string>();
        while (target != null)
        {
            if (seen.Count >= 1000 || !BrowserRequestCommand.IsReadUrl(target.AbsoluteUri) || target.AbsolutePath != path || !seen.Add(target.AbsoluteUri))
                throw new SoundCloudException("Неверный адрес страницы плейлистов.");
            var read = target;
            if (includeTracks)
            {
                var query = target.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Where(part => !part.StartsWith("show_tracks=", StringComparison.OrdinalIgnoreCase));
                read = new UriBuilder(target) { Query = string.Join('&', query.Append("show_tracks=true")) }.Uri;
            }
            using var document = await GetJsonAsync(read, token);
            var page = LibraryData.Parse(document.RootElement);
            items.AddRange(page.Items.Where(item => item.Playlist is { Id: > 0 } playlist && playlist.User?.Id == userId && !item.IsAlbum));
            target = page.NextHref == null ? null : Uri.TryCreate(page.NextHref, UriKind.Absolute, out var next) ? next :
                throw new SoundCloudException("Неверный адрес страницы плейлистов.");
        }
        return items.DistinctBy(item => item.Playlist!.Id).ToArray();
    }

    public async Task<LibraryItem[]> GetPlaylistChoicesForTrackAsync(long userId, long trackId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ValidateDetailTrackId(trackId);
        var items = await GetOwnedPlaylistsAsync(userId, token, includeTracks: true);
        // A match in a partial preview is conclusive. Absence is conclusive only
        // in a complete ID list; never fetch/decode track artwork or audio here.
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (index, stop) =>
        {
            var playlist = items[index].Playlist!;
            if (playlist.Tracks.Any(track => track.Id == trackId) ||
                playlist.TrackCount >= 0 && playlist.Tracks.Length == playlist.TrackCount && playlist.Tracks.All(track => track.Id > 0)) return;
            SoundCloudPlaylist full;
            if (Session?.MobileIdentity != null)
            {
                if (Transport is not ISoundCloudMobileApi mobile)
                    throw new SoundCloudException("Мобильное соединение SoundCloud ещё не подключено.");
                full = await mobile.GetMobilePlaylistAsync(playlist.Id, stop);
            }
            else
            {
                using var document = await GetJsonAsync(Api($"playlists/{playlist.Id}?representation=full"), stop);
                full = LibraryData.ParsePlaylist(document.RootElement);
            }
            if (full.Id != playlist.Id || full.User?.Id != userId || full.IsAlbum || full.IsSystemPlaylist || full.TrackCount < 0 ||
                (!full.TrackPagesComplete && full.Tracks.Length != full.TrackCount) || full.Tracks.Any(track => track.Id <= 0))
                throw new SoundCloudException("SoundCloud не подтвердил состав плейлиста. Открой список ещё раз.");
            items[index] = LibraryData.FromPlaylist(full);
        });
        return items;
    }

    public async Task<HashSet<long>> GetFollowingIdsAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        if (Session == null) throw new SoundCloudException("Для подписок нужен вход в SoundCloud.");
        if (Session.MobileIdentity != null) return await MobileApi.GetMobileActionIdsAsync("users", userId: userId, cancellationToken: token);
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
    public async Task<HashSet<long>> GetPlaylistActionIdsAsync(bool reposts, CancellationToken token = default)
    {
        if (Session == null) throw new SoundCloudException("Для этого нужен вход в SoundCloud.");
        if (Session.MobileIdentity != null) return await MobileApi.GetMobileActionIdsAsync("playlists", reposts, cancellationToken: token);
        var path = reposts ? "/me/playlist_reposts/ids" : "/me/playlist_likes/ids";
        Uri? target = Api(path.TrimStart('/') + "?limit=200&linked_partitioning=1");
        var ids = new HashSet<long>(); var seen = new HashSet<string>();
        while (target != null)
        {
            if (seen.Count >= 1000 || !BrowserRequestCommand.IsReadUrl(target.AbsoluteUri) || target.AbsolutePath != path || !seen.Add(target.AbsoluteUri))
                throw new SoundCloudException("Неверный адрес страницы плейлистов.");
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
            target = null;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_href", out var href) && href.ValueKind == JsonValueKind.String && href.GetString() is { } url)
                target = Uri.TryCreate(url, UriKind.Absolute, out var next) ? next : throw new SoundCloudException("Неверный адрес страницы плейлистов.");
        }
        return ids;
    }

    public async Task SetPlaylistLikedAsync(long userId, long playlistId, bool liked, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId); ValidateDetailTrackId(playlistId);
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы поставить лайк.");
        if (BrowserTransport is not { } browser) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        await browser.SetPlaylistLikedAsync(userId, playlistId, liked, token);
    }

    public async Task SetPlaylistRepostedAsync(long playlistId, bool reposted, CancellationToken token = default)
    {
        ValidateDetailTrackId(playlistId);
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы сделать репост.");
        if (BrowserTransport is not { } browser) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        await browser.SetPlaylistRepostedAsync(playlistId, reposted, token);
    }

}
