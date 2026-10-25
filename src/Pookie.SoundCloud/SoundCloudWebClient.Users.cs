using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed record UserWebProfile(string Url, string? Title, string? Service);
public sealed record UserVisual(string? VisualUrl);
public sealed record UserVisuals(UserVisual[]? Visuals);

public sealed partial class SoundCloudWebClient
{
    public async Task<SoundCloudUser> GetUserAsync(long userId, CancellationToken token = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        using var document = await GetJsonAsync(Api($"users/{userId}"), token);
        return document.RootElement.Deserialize<SoundCloudUser>(SoundCloudJson.Default.Options)
            is { Id: > 0 } user && user.Id == userId ? user : throw new SoundCloudException("Профиль недоступен");
    }

    public async Task<UserWebProfile[]> GetUserLinksAsync(long userId, CancellationToken token = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        using var document = await GetJsonAsync(Api($"users/soundcloud:users:{userId}/web-profiles"), token);
        var rows = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement : document.RootElement.GetProperty("collection");
        return rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object && row.TryGetProperty("url", out var value) &&
            value.ValueKind == JsonValueKind.String && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.UserInfo == "")
            .Select(row => new UserWebProfile(row.GetProperty("url").GetString()!,
                row.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null,
                row.TryGetProperty("service", out var service) && service.ValueKind == JsonValueKind.String ? service.GetString() : null)).DistinctBy(link => link.Url).ToArray();
    }

    public async Task<LibraryPage> GetUserSectionAsync(long userId, string section, string? cursor = null, CancellationToken token = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var path = section switch
        {
            "activity" => $"stream/users/{userId}",
            "tracks" => $"users/{userId}/tracks",
            "albums" => $"users/{userId}/albums",
            "playlists" => $"users/{userId}/playlists_without_albums",
            "reposts" => $"stream/users/{userId}/reposts",
            "likes" => $"users/{userId}/likes",
            "followers" => $"users/{userId}/followers",
            "following" => $"users/{userId}/followings",
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        var uri = Api(path + "?limit=30&linked_partitioning=1");
        if (cursor != null && (!BrowserRequestCommand.IsReadUrl(cursor) || !Uri.TryCreate(cursor, UriKind.Absolute, out uri) || uri.AbsolutePath != "/" + path))
            throw new SoundCloudException("Неверная страница профиля");
        using var document = await GetJsonAsync(uri!, token);
        var page = await ParseLibraryAsync(document.RootElement, token);
        if (section is not ("activity" or "reposts")) return page;
        var events = document.RootElement.GetProperty("collection").EnumerateArray()
            .Select(entry => (Item: LibraryData.ParseItem(entry), Entry: entry)).Where(pair => pair.Item != null)
            .GroupBy(pair => pair.Item!.Key).ToDictionary(group => group.Key, group => group.First().Entry);
        return page with { Items = page.Items.Select(item => events.TryGetValue(item.Key, out var entry) ? item with
        {
            ActivityType = entry.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null,
            ActivityAt = entry.TryGetProperty("created_at", out var date) && date.ValueKind == JsonValueKind.String ? date.GetString() : null
        } : item).ToArray() };

    }
}
