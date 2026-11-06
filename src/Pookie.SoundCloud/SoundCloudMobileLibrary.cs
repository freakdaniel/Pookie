using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudHttpTransport
{
    private readonly object mobileLibraryGate = new();
    private readonly Dictionary<(long UserId, string Section), (WebSession Account, DateTimeOffset At, LibraryItem[] Items)> mobileLibrarySnapshots = [];
    private long mobileLibraryVersion;

    private void InvalidateMobileCollectionSnapshots()
    {
        lock (mobileLibraryGate) { mobileLibraryVersion++; mobileLibrarySnapshots.Clear(); }
    }
    public async Task<LibraryPage> GetMobileLibraryPageAsync(long userId, string section, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var account = Account();
        if (account.MobileIdentity == null) throw new SoundCloudException("Нужен мобильный профиль.");
        var urn = SoundCloudMobileRoutes.Urn("users", userId);
        var path = section switch
        {
            "likes" or "reposts" => $"/users/{urn}/{section}",
            "albums" => $"/users/{urn}/albums/posted",
            "posted" => $"/users/{urn}/playlists/posted",
            "following" => $"/followings/{urn}/users",
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        var uri = cursor == null ? SoundCloudMobileRoutes.Uri(path + "?limit=30&linked_partitioning=1") : MobileCursor(cursor, path);
        using var json = await GetJsonAsync(uri, cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array)
            throw new SoundCloudException("SoundCloud не подтвердил мобильную библиотеку.");
        var items = new List<LibraryItem>();
        foreach (var entry in collection.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) throw SoundCloudMobilePlaylist.InvalidPlaylist();
            if (section == "following")
            {
                var user = SoundCloudMobilePlaylist.ParseUser(entry);
                items.Add(new("user:" + user.Id, user.Username, "Исполнитель", user.AvatarUrl, User: user));
            }
            else if (entry.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.Object)
                items.Add(LibraryItem.FromTrack(SoundCloudMobilePlaylist.ParseTrack(track)));
            else if (entry.TryGetProperty("playlist", out var playlist) && playlist.ValueKind == JsonValueKind.Object)
                items.Add(LibraryData.FromPlaylist(SoundCloudMobilePlaylist.ParseMetadata(playlist)));
            else throw new SoundCloudException("SoundCloud вернул неизвестный элемент мобильной библиотеки.");
        }
        var next = SoundCloudMobilePlaylist.NextHref(root);
        if (next != null) MobileCursor(next, path);
        if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
        return new(items.DistinctBy(item => item.Key).ToArray(), next);
    }

    private static Uri MobileCursor(string cursor, string path) =>
        Uri.TryCreate(cursor, UriKind.Absolute, out var target) && SoundCloudMobileRoutes.IsRead(target) && target.AbsolutePath == path
            ? target : throw new SoundCloudException("Некорректный курсор мобильной библиотеки.");

    private async Task<LibraryItem[]> AllMobileLibraryItemsAsync(long userId, string section, CancellationToken token)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var account = Account(); long version;
            lock (mobileLibraryGate)
            {
                version = mobileLibraryVersion;
                if (mobileLibrarySnapshots.TryGetValue((userId, section), out var cached) && account.Equals(cached.Account) &&
                    clock.GetUtcNow() - cached.At < TimeSpan.FromSeconds(15)) return cached.Items;
            }
            var items = new List<LibraryItem>(); string? cursor = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (seen.Count >= 1000 || cursor != null && !seen.Add(cursor)) throw new SoundCloudException("Зацикленная мобильная библиотека.");
                var page = await GetMobileLibraryPageAsync(userId, section, cursor, token).ConfigureAwait(false);
                items.AddRange(page.Items); cursor = page.NextHref;
            } while (cursor != null);
            var result = items.DistinctBy(item => item.Key).ToArray();
            lock (mobileLibraryGate)
            {
                if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
                if (mobileLibraryVersion == version)
                {
                    if (mobileLibrarySnapshots.Count >= 8 && !mobileLibrarySnapshots.ContainsKey((userId, section)))
                        mobileLibrarySnapshots.Remove(mobileLibrarySnapshots.MinBy(pair => pair.Value.At).Key);
                    mobileLibrarySnapshots[(userId, section)] = (account, clock.GetUtcNow(), result);
                    return result;
                }
            }
        }
        throw new SoundCloudException("Библиотека изменилась во время загрузки. Открой её ещё раз.");
    }

    public async Task<LibraryItem[]> GetMobileCollectionsAsync(long userId, CancellationToken cancellationToken = default)
    {
        var account = Account();
        // Android assembles the library from posts and likes; public profile
        // collections already include their corresponding mobile metadata.
        var posted = await AllMobileLibraryItemsAsync(userId, "posted", cancellationToken).ConfigureAwait(false);
        var albums = await AllMobileLibraryItemsAsync(userId, "albums", cancellationToken).ConfigureAwait(false);
        var liked = await AllMobileLibraryItemsAsync(userId, "likes", cancellationToken).ConfigureAwait(false);
        var reposted = await AllMobileLibraryItemsAsync(userId, "reposts", cancellationToken).ConfigureAwait(false);
        if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
        var result = posted.Concat(albums).Concat(liked).Concat(reposted).Where(item => item.Playlist != null).DistinctBy(item => item.Key).ToArray();
        await Parallel.ForEachAsync(Enumerable.Range(0, result.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (index, token) =>
        {
            var item = result[index];
            if (!string.IsNullOrEmpty(item.ArtworkUrl) || item.Playlist!.TrackCount == 0) return;
            try
            {
                var full = await GetMobilePlaylistAsync(item.Playlist.Id, token).ConfigureAwait(false);
                result[index] = LibraryData.FromPlaylist(full) with { Key = item.Key };
            }
            catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 404 or 410) { }
        }).ConfigureAwait(false);
        if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
        return result;
    }

    public async Task<HashSet<long>> GetMobileActionIdsAsync(string kind, bool reposts = false, long? userId = null, CancellationToken cancellationToken = default)
    {
        if (kind is not ("tracks" or "playlists" or "users")) throw new ArgumentOutOfRangeException(nameof(kind));
        var account = Account();
        var owner = await UserIdAsync(cancellationToken).ConfigureAwait(false);
        if (account.MobileIdentity == null || userId != null && userId != owner) throw new SoundCloudException("Мобильная библиотека принадлежит другому профилю.");
        var items = await AllMobileLibraryItemsAsync(owner, kind == "users" ? "following" : reposts ? "reposts" : "likes", cancellationToken).ConfigureAwait(false);
        if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
        return items.Select(item => kind == "tracks" ? item.Track?.Id : kind == "playlists" ? item.Playlist?.Id : item.User?.Id)
            .OfType<long>().Where(id => id > 0).ToHashSet();
    }
}
