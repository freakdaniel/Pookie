using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<LibraryItem[]> GetPersonalMixesAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        if (Session == null) throw new SoundCloudException("Для персональных подборок нужен вход в SoundCloud.");
        if (Session.MobileIdentity != null && Transport is ISoundCloudMobileApi mobile)
        {
            try
            {
                using var home = await mobile.GetMobileSduiAsync(cancellationToken: token);
                var mixes = DiscoverData.MobilePersonalMixes(home.RootElement, userId);
                if (mixes.Length > 0) return mixes;
            }
            catch (NotSupportedException) { return await GetRestMixesAsync(userId, token); }
            catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 400 or 404)
            { return await GetRestMixesAsync(userId, token); }
        }
        return await GetRestMixesAsync(userId, token);
    }

    private async Task<LibraryItem[]> GetRestMixesAsync(long userId, CancellationToken token)
    {
        using var document = await GetJsonAsync(Api("mixed-selections"), token);
        return DiscoverData.PersonalMixes(document.RootElement, userId);
    }
}

public static class DiscoverData
{
    public static LibraryItem[] MobilePersonalMixes(JsonElement root, long userId)
    {
        var items = new Dictionary<string, LibraryItem>(StringComparer.Ordinal); var remaining = 20000;
        Visit(root, 0);
        return items.Values.ToArray();
        void Visit(JsonElement value, int depth)
        {
            if (--remaining < 0 || depth > 24) return;
            if (value.ValueKind == JsonValueKind.Array) { foreach (var child in value.EnumerateArray()) Visit(child, depth + 1); return; }
            if (value.ValueKind != JsonValueKind.Object) return;
            if (value.TryGetProperty("urn", out var urn) && urn.ValueKind == JsonValueKind.String &&
                urn.GetString()?.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal) == true &&
                value.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                var playlist = LibraryData.ParsePlaylist(value);
                if (playlist.MadeFor == null || playlist.MadeFor.Id == userId)
                { var item = LibraryData.FromPlaylist(playlist); items.TryAdd(item.Key, item); }
            }
            foreach (var child in value.EnumerateObject()) Visit(child.Value, depth + 1);
        }
    }

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
