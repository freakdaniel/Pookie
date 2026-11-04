using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<SoundCloudDiscoverShelves> GetDiscoverShelvesAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        using var document = await GetJsonAsync(Api("mixed-selections"), token);
        var shelves = DiscoverData.Shelves(document.RootElement, userId);
        if (shelves.Mixes.Length > 0 && shelves.MadeForYou.Length > 0 ||
            Session?.MobileIdentity == null || Transport is not ISoundCloudMobileApi mobile) return shelves;

        // The web selection supplies the reference order. Some mobile sessions
        // receive only public web selections, so fill missing personal shelves from SDUI.
        try
        {
            using var home = await mobile.GetMobileSduiAsync(cancellationToken: token);
            var personal = DiscoverData.GroupShelves(DiscoverData.MobilePersonalMixes(home.RootElement, userId));
            return shelves with
            {
                Mixes = shelves.Mixes.Length == 0 ? personal.Mixes : shelves.Mixes,
                MadeForYou = shelves.MadeForYou.Length == 0 ? personal.MadeForYou : shelves.MadeForYou
            };
        }
        catch (Exception error) when (!token.IsCancellationRequested &&
            error is NotSupportedException or SoundCloudException or HttpRequestException or JsonException or OperationCanceledException)
        { return shelves; }
    }

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

public sealed record SoundCloudDiscoverShelves(LibraryItem[] Mixes, LibraryItem[] MadeForYou, LibraryItem[] Trending);

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
