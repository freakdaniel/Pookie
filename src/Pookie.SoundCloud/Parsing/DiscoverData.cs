using System.Text.Json;

namespace Pookie.SoundCloud;

public static class DiscoverData
{


    public static LibraryItem[] PersonalMixes(JsonElement root, long userId) =>
        SelectionPlaylists(root, userId).Where(item => Kind(item) is Shelf.Mixes or Shelf.MadeForYou).ToArray();

    public static SoundCloudDiscoverShelves Shelves(JsonElement root, long userId) =>
        GroupShelves(SelectionPlaylists(root, userId));

    internal static SoundCloudDiscoverShelves GroupShelves(IEnumerable<LibraryItem> items)
    {
        var groups = items.ToLookup(Kind);
        return new(groups[Shelf.Mixes].ToArray(), groups[Shelf.MadeForYou].ToArray(), groups[Shelf.Trending].ToArray());
    }

    private enum Shelf { Other, Mixes, MadeForYou, Trending }

    private static Shelf Kind(LibraryItem item)
    {
        var urn = item.Playlist?.Urn ?? "";
        const string prefix = "soundcloud:system-playlists:";
        if (urn.StartsWith(prefix + "your-moods:", StringComparison.Ordinal)) return Shelf.Mixes;
        if (urn.StartsWith(prefix + "new-for-you:", StringComparison.Ordinal) ||
            urn.StartsWith(prefix + "weekly:", StringComparison.Ordinal)) return Shelf.MadeForYou;
        return urn.StartsWith(prefix + "trending-by-genre:", StringComparison.Ordinal) ? Shelf.Trending : Shelf.Other;
    }

    private static IEnumerable<LibraryItem> SelectionPlaylists(JsonElement root, long userId)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("collection", out var selections) || selections.ValueKind != JsonValueKind.Array)
            yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in selections.EnumerateArray())
        {
            if (selection.ValueKind != JsonValueKind.Object || !selection.TryGetProperty("items", out var items)) continue;
            if (items.ValueKind == JsonValueKind.Object && items.TryGetProperty("collection", out var collection)) items = collection;
            if (items.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in items.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || entry.TryGetProperty("ad_urn", out _) || entry.TryGetProperty("promoted_by", out _)) continue;
                if (LibraryData.ParseItem(entry) is not { Playlist: { } playlist } item ||
                    playlist.MadeFor != null && playlist.MadeFor.Id != userId) continue;
                if (seen.Add(item.Key)) yield return item;
            }
        }
    }
}
