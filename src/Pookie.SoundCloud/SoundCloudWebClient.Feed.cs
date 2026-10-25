using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed record FeedEntry(string Key, LibraryItem Item, SoundCloudUser? Actor, string Type, string? CreatedAt)
{
    public bool IsRepost => Type.Contains("repost", StringComparison.OrdinalIgnoreCase);
}
public sealed record FeedPage(FeedEntry[] Entries, string? NextHref);

public sealed partial class SoundCloudWebClient
{
    public async Task<FeedPage> GetFeedActivitiesAsync(string? cursor = null, CancellationToken token = default)
    {
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы открыть ленту подписок");
        var uri = Api("stream?limit=30&linked_partitioning=1");
        if (cursor != null && (!BrowserRequestCommand.IsReadUrl(cursor) || !Uri.TryCreate(cursor, UriKind.Absolute, out uri) || uri.AbsolutePath != "/stream"))
            throw new SoundCloudException("Неверная страница ленты");
        using var document = await GetJsonAsync(uri!, token);
        var enriched = await ParseLibraryAsync(document.RootElement, token);
        var items = enriched.Items.ToDictionary(item => item.Key);
        var entries = new List<FeedEntry>();
        foreach (var entry in document.RootElement.GetProperty("collection").EnumerateArray())
        {
            if (LibraryData.ParseItem(entry) is not { } parsed || !items.TryGetValue(parsed.Key, out var item) ||
                item.Track == null && item.Playlist == null ||
                item.Playlist is { } playlist && playlist.TrackCount <= 0 && (playlist.Tracks?.Length ?? 0) == 0) continue;
            string? Text(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var type = Text("type") ?? (item.Track != null ? "track" : "playlist");
            var at = Text("created_at");
            var actor = entry.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object
                ? user.Deserialize(SoundCloudJson.Default.SoundCloudUser) : item.Track?.User ?? item.Playlist?.User;
            var key = Text("uuid") ?? $"{type}:{item.Key}:{actor?.Id}:{at}";
            entries.Add(new(key, item with { ActivityType = type, ActivityAt = at }, actor, type, at));
        }
        return new(entries.DistinctBy(entry => entry.Key).ToArray(), enriched.NextHref);
    }
}
