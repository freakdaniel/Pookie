using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pookie.SoundCloud;

// Android's ApiPlaylistWrapper/ApiPlaylistWithTracks use URNs and embedded
// owners. Normalize only at the mobile boundary, keeping web receipts strict.
internal static class SoundCloudMobilePlaylist
{
    public static SoundCloudPlaylist ParseMetadata(JsonElement root)
    {
        var value = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("playlist", out var wrapped) ? wrapped : root;
        var normalized = NormalizeResource(value, "playlists");
        var related = value.TryGetProperty("_embedded", out var embedded) ? embedded : value;
        if (related.ValueKind != JsonValueKind.Object || !related.TryGetProperty("user", out var user) ||
            !value.TryGetProperty("track_count", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var total) || total < 0)
            throw InvalidPlaylist();
        normalized["user"] = NormalizeResource(user, "users");
        normalized.Remove("tracks");
        var playlist = normalized.Deserialize(SoundCloudJson.Default.SoundCloudPlaylist) ?? throw InvalidPlaylist();
        return playlist with { Tracks = [] };
    }

    public static SoundCloudTrack ParseTrack(JsonElement value) =>
        NormalizeResource(value, "tracks").Deserialize(SoundCloudJson.Default.SoundCloudTrack) ?? throw InvalidPlaylist();

    public static SoundCloudUser ParseUser(JsonElement value) =>
        NormalizeResource(value, "users").Deserialize(SoundCloudJson.Default.SoundCloudUser) ?? throw InvalidPlaylist();

    public static string? NextHref(JsonElement page)
    {
        if (page.ValueKind != JsonValueKind.Object) return null;
        string? mobile = null;
        if (page.TryGetProperty("_links", out var links) && links.ValueKind != JsonValueKind.Null)
        {
            if (links.ValueKind != JsonValueKind.Object) throw InvalidPlaylist();
            if (links.TryGetProperty("next", out var next) && next.ValueKind != JsonValueKind.Null)
            {
                if (next.ValueKind != JsonValueKind.Object || !next.TryGetProperty("href", out var href)) throw InvalidPlaylist();
                mobile = ReadHref(href);
            }
        }
        var legacy = page.TryGetProperty("next_href", out var value) ? ReadHref(value) : null;
        if (mobile != null && legacy != null && mobile != legacy) throw InvalidPlaylist();
        return mobile ?? legacy;
    }

    private static string? ReadHref(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null :
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } href ? href : throw InvalidPlaylist();

    private static JsonObject NormalizeResource(JsonElement value, string kind)
    {
        if (value.ValueKind != JsonValueKind.Object) throw InvalidPlaylist();
        long id = 0;
        if (value.TryGetProperty("id", out var numeric) &&
            (numeric.ValueKind != JsonValueKind.Number || !numeric.TryGetInt64(out id) || id is <= 0 or > 9007199254740991))
            throw InvalidPlaylist();
        if (value.TryGetProperty("urn", out var urn))
        {
            var prefix = "soundcloud:" + kind + ":";
            if (urn.ValueKind != JsonValueKind.String || urn.GetString() is not { } text || !text.StartsWith(prefix, StringComparison.Ordinal) ||
                !long.TryParse(text.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var fromUrn) ||
                fromUrn is <= 0 or > 9007199254740991 || text != SoundCloudMobileRoutes.Urn(kind, fromUrn) || id != 0 && id != fromUrn)
                throw InvalidPlaylist();
            id = fromUrn;
        }
        if (id == 0) throw InvalidPlaylist();
        var result = JsonNode.Parse(value.GetRawText())!.AsObject();
        result["id"] = id;
        if (kind == "playlists" && !value.TryGetProperty("last_modified", out _) && value.TryGetProperty("updated_at", out var updated))
            result["last_modified"] = JsonNode.Parse(updated.GetRawText());
        if (kind == "playlists" && value.TryGetProperty("user_tags", out var tags))
            result["tags"] = JsonNode.Parse(tags.GetRawText());
        // Android's ApiPlaylist/ApiTrack expose a size template instead of the
        // web artwork_url. Resolve it before handing the resource to shared UI.
        var imageField = kind == "users" ? "avatar_url" : "artwork_url";
        if ((!value.TryGetProperty(imageField, out var image) || image.ValueKind == JsonValueKind.Null ||
             image.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(image.GetString())) &&
            value.TryGetProperty(imageField + "_template", out var template) && template.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(template.GetString()))
            result[imageField] = template.GetString()!.Replace("{size}", "t500x500", StringComparison.Ordinal);
        if (kind == "users" && value.TryGetProperty("badges", out var badges) && badges.ValueKind == JsonValueKind.Array)
        {
            // ApiUser uses List<String>; the web model uses boolean flags.
            var flags = new JsonObject();
            foreach (var badge in badges.EnumerateArray())
            {
                if (badge.ValueKind != JsonValueKind.String) throw InvalidPlaylist();
                var name = badge.GetString()!.ToLowerInvariant();
                if (name is "verified" or "pro" or "pro_unlimited" or "creator_mid_tier") flags[name] = true;
            }
            result["badges"] = flags;
        }
        if (kind == "tracks")
        {
            var related = value.TryGetProperty("_embedded", out var embedded) ? embedded : value;
            if (related.ValueKind == JsonValueKind.Object && related.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
                result["user"] = NormalizeResource(user, "users");
            // ApiTrack carries its counters in RelatedResources.stats. The
            // shared model follows V2 names, including singular comment_count.
            if (related.ValueKind == JsonValueKind.Object && related.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
                foreach (var (source, target) in new[] { ("playback_count", "playback_count"), ("likes_count", "likes_count"),
                    ("comments_count", "comment_count"), ("reposts_count", "reposts_count") })
                    if (stats.TryGetProperty(source, out var count)) result[target] = JsonNode.Parse(count.GetRawText());
        }
        return result;
    }

    internal static SoundCloudException InvalidPlaylist() => new("SoundCloud не подтвердил данные мобильного плейлиста.");
}

public sealed partial class SoundCloudHttpTransport
{
    public async Task<LibraryItem[]> GetMobileOwnedPlaylistsAsync(long userId, CancellationToken cancellationToken = default)
    {
        var account = Account();
        if (account.MobileIdentity == null) throw new SoundCloudException("Для мобильных плейлистов нужен Android-профиль.");
        var path = $"/users/{SoundCloudMobileRoutes.Urn("users", userId)}/playlists/posted";
        Uri? uri = SoundCloudMobileRoutes.Uri(path + "?limit=30&linked_partitioning=1");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<LibraryItem>();
        while (uri != null)
        {
            if (seen.Count >= 1000 || !SoundCloudMobileRoutes.IsRead(uri) || uri.AbsolutePath != path || !seen.Add(uri.AbsoluteUri))
                throw new SoundCloudException("Некорректный курсор мобильных плейлистов.");
            if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
            using var document = await GetJsonAsync(uri, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("collection", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw SoundCloudMobilePlaylist.InvalidPlaylist();
            foreach (var entry in entries.EnumerateArray())
            {
                var playlist = SoundCloudMobilePlaylist.ParseMetadata(entry);
                var item = LibraryData.FromPlaylist(playlist);
                if (playlist.User?.Id == userId && !playlist.IsSystemPlaylist && !item.IsAlbum) items.Add(item);
            }
            var next = SoundCloudMobilePlaylist.NextHref(root);
            uri = next == null ? null : Uri.TryCreate(next, UriKind.Absolute, out var target) ? target :
                throw new SoundCloudException("Некорректный курсор мобильных плейлистов.");
        }
        if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
        return items.DistinctBy(item => item.Playlist!.Id).ToArray();
    }

    public async Task<SoundCloudPlaylist> GetMobilePlaylistAsync(long playlistId, CancellationToken cancellationToken = default)
    {
        var account = Account();
        if (account.MobileIdentity == null) throw new SoundCloudException("Для мобильного плейлиста нужен Android-профиль.");
        var path = $"/playlists/{SoundCloudMobileRoutes.Urn("playlists", playlistId)}";
        using var document = await GetJsonAsync(SoundCloudMobileRoutes.Uri(path + "/info"), cancellationToken).ConfigureAwait(false);
        var playlist = SoundCloudMobilePlaylist.ParseMetadata(document.RootElement);
        if (playlist.Id != playlistId || !document.RootElement.TryGetProperty("tracks", out var first))
            throw SoundCloudMobilePlaylist.InvalidPlaylist();
        var page = first.Clone();
        var tracks = new List<SoundCloudTrack>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 20; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
            var collection = page.ValueKind == JsonValueKind.Object && page.TryGetProperty("collection", out var entries) ? entries : page;
            if (collection.ValueKind != JsonValueKind.Array) throw SoundCloudMobilePlaylist.InvalidPlaylist();
            foreach (var track in collection.EnumerateArray()) tracks.Add(SoundCloudMobilePlaylist.ParseTrack(track));
            var next = SoundCloudMobilePlaylist.NextHref(page);
            if (next == null)
            {
                // ApiCollection's explicit terminal links describe the actual
                // enumeration. track_count can still include unavailable tracks.
                var terminalAndroidPage = page.ValueKind == JsonValueKind.Object && page.TryGetProperty("_links", out var links) &&
                    links.ValueKind is JsonValueKind.Object or JsonValueKind.Null;
                if (!terminalAndroidPage && tracks.Count != playlist.TrackCount) throw SoundCloudMobilePlaylist.InvalidPlaylist();
                return playlist with { Tracks = tracks.ToArray(), TrackPagesComplete = true };
            }
            if (!Uri.TryCreate(next, UriKind.Absolute, out var uri) ||
                !SoundCloudMobileRoutes.IsRead(uri) || uri.AbsolutePath != path + "/tracks" || !seen.Add(uri.AbsoluteUri))
                throw new SoundCloudException("Некорректный курсор треков мобильного плейлиста.");
            using var following = await GetJsonAsync(uri, cancellationToken).ConfigureAwait(false);
            page = following.RootElement.Clone();
        }
        throw new SoundCloudException("SoundCloud не завершил список треков плейлиста.");
    }
}
