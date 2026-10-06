using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.SoundCloud;

public sealed record SoundCloudPlaylist
{
    public long Id { get; init; }
    public string Urn { get; init; } = "";
    public string Title { get; init; } = "";
    public string? ArtworkUrl { get; init; }
    public string? CalculatedArtworkUrl { get; init; }
    public string? ShortTitle { get; init; }
    public string? ShortDescription { get; init; }
    public SoundCloudUser? MadeFor { get; init; }
    public string? PermalinkUrl { get; init; }
    public string? PlaylistType { get; init; }
    public bool IsAlbum { get; init; }
    public int TrackCount { get; init; }
    public SoundCloudUser? User { get; init; }
    public SoundCloudTrack[] Tracks { get; init; } = [];
}

public sealed record LibraryItem(string Key, string Title, string Subtitle, string? ArtworkUrl,
    SoundCloudTrack? Track = null, SoundCloudPlaylist? Playlist = null, SoundCloudUser? User = null)
{
    public bool IsAlbum => Playlist is { IsAlbum: true } || Playlist?.PlaylistType?.ToLowerInvariant() is "album" or "ep" or "single" or "compilation";
    public static LibraryItem FromTrack(SoundCloudTrack track) => new("track:" + track.Id, track.Title, track.Author, track.ArtworkUrl ?? track.User?.AvatarUrl, Track: track);
}
public sealed record LibraryPage(LibraryItem[] Items, string? NextHref);

public static class LibraryData
{
    public static LibraryPage Parse(JsonElement root)
    {
        var collection = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
        var items = new List<LibraryItem>();
        foreach (var entry in collection.EnumerateArray())
            if (ParseItem(entry) is { } item) items.Add(item);
        var next = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_href", out var href) && href.ValueKind == JsonValueKind.String ? href.GetString() : null;
        return new(items.DistinctBy(item => item.Key).ToArray(), next);
    }
    public static LibraryItem? ParseItem(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        var item = entry;
        var kind = Text(entry, "kind");
        foreach (var name in new[] { "context", "origin", "system_playlist", "playlist", "track", "user" })
            if (item.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Object &&
                (name != "user" || !item.TryGetProperty("title", out _)))
            {
                item = nested;
                if (name is "system_playlist" or "playlist" or "track" or "user") kind = name.Replace('_', '-');
                break;
            }
        foreach (var name in new[] { "system_playlist", "playlist", "track", "user" })
            if (item.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Object &&
                !item.TryGetProperty("title", out _) && !item.TryGetProperty("username", out _))
            { item = nested; kind = name.Replace('_', '-'); break; }
        kind ??= Text(item, "kind");
        if (item.TryGetProperty("username", out _))
        {
            var user = item.Deserialize(SoundCloudJson.Default.SoundCloudUser);
            return user is { Id: > 0 } ? new("user:" + user.Id, user.Username,
                user.FollowersCount is { } followers ? $"{followers:N0} подписчиков" : "Исполнитель", user.AvatarUrl, User: user) : null;
        }
        if (kind is "playlist" or "system-playlist" || item.TryGetProperty("tracks", out _) || item.TryGetProperty("playlist_type", out _))
        {
            var playlist = ParsePlaylist(item, Text(entry, "context_urn"));
            if (playlist.Id <= 0 && string.IsNullOrEmpty(playlist.Urn)) return null;
            return FromPlaylist(playlist);
        }
        if (item.TryGetProperty("title", out _))
        {
            var track = item.Deserialize(SoundCloudJson.Default.SoundCloudTrack);
            if (track is { Id: > 0 }) return LibraryItem.FromTrack(track);
        }
        return null;
    }

    public static SoundCloudPlaylist ParsePlaylist(JsonElement item, string? contextUrn = null)
    {
        // System playlists put a URN in `id`, unlike numeric ordinary playlists.
        var id = Text(item, "id");
        var urn = Text(item, "urn") ?? (id?.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal) == true ? id : contextUrn);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            foreach (var property in item.EnumerateObject())
            {
                if (property.Name == "id" && property.Value.ValueKind == JsonValueKind.String)
                    writer.WriteNumber("id", long.TryParse(id, out var numeric) ? numeric : 0);
                else property.WriteTo(writer);
            }
            if (!item.TryGetProperty("urn", out _) && urn != null) writer.WriteString("urn", urn);
            writer.WriteEndObject();
        }
        var playlist = JsonSerializer.Deserialize(output.ToArray(), SoundCloudJson.Default.SoundCloudPlaylist)
            ?? throw new JsonException("Invalid playlist");
        return playlist with { Title = playlist.ShortTitle ?? playlist.Title ?? "Подборка", Urn = playlist.Urn ?? "" };
    }

    public static LibraryItem FromPlaylist(SoundCloudPlaylist playlist) => new(
        "playlist:" + (!string.IsNullOrEmpty(playlist.Urn) ? playlist.Urn : playlist.Id.ToString()), playlist.Title,
        playlist.ShortDescription ?? playlist.User?.Username ?? (playlist.MadeFor != null ? "Для " + playlist.MadeFor.Username :
            playlist.PlaylistType?.Contains("STATION", StringComparison.OrdinalIgnoreCase) == true ? "Станция" : "Подборка"),
        playlist.CalculatedArtworkUrl ?? playlist.ArtworkUrl ?? playlist.Tracks?.FirstOrDefault(t => !string.IsNullOrEmpty(t.ArtworkUrl))?.ArtworkUrl,
        Playlist: playlist);

    private static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

}
