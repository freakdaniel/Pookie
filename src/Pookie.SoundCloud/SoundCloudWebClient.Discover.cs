using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<LibraryItem[]> GetPersonalMixesAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        if (Session == null) throw new SoundCloudException("Для персональных подборок нужен вход в SoundCloud.");
        using var document = await GetJsonAsync(Api("mixed-selections"), token);
        return DiscoverData.PersonalMixes(document.RootElement, userId);
    }
}

public static class DiscoverData
{
    public static LibraryItem[] PersonalMixes(JsonElement root, long userId)
    {
        var result = new List<LibraryItem>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("collection", out var selections) || selections.ValueKind != JsonValueKind.Array) return [];
        foreach (var selection in selections.EnumerateArray())
        {
            if (selection.ValueKind != JsonValueKind.Object || !selection.TryGetProperty("items", out var items)) continue;
            if (items.ValueKind == JsonValueKind.Object && items.TryGetProperty("collection", out var collection)) items = collection;
            if (items.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in items.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || entry.TryGetProperty("ad_urn", out _) || entry.TryGetProperty("promoted_by", out _)) continue;
                if (LibraryData.ParseItem(entry) is not { Playlist: { } playlist } item) continue;
                var urn = playlist.Urn;
                if (!urn.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal)) continue;
                if (playlist.MadeFor != null && playlist.MadeFor.Id != userId) continue;
                if (urn.Contains(":your-moods:", StringComparison.Ordinal) || urn.Contains(":new-for-you:", StringComparison.Ordinal) ||
                    urn.Contains(":weekly:", StringComparison.Ordinal)) result.Add(item);
            }
        }
        return result.DistinctBy(item => item.Key).ToArray();
    }
}
