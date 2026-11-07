using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    private readonly object libraryMetadataGate = new();
    private WebSession? metadataSession;
    private readonly Dictionary<string, (DateTimeOffset At, Task<JsonElement> Request)> libraryMetadata = [];

    private Task<JsonElement> GetLibraryMetadataAsync(string path, CancellationToken cancellationToken)
    {
        lock (libraryMetadataGate)
        {
            if (!ReferenceEquals(metadataSession, Session)) { libraryMetadata.Clear(); metadataSession = Session; }
            if (libraryMetadata.TryGetValue(path, out var cached) && !cached.Request.IsCanceled && !cached.Request.IsFaulted &&
                DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(5)) return cached.Request.WaitAsync(cancellationToken);
            if (libraryMetadata.Count >= 256) libraryMetadata.Remove(libraryMetadata.MinBy(pair => pair.Value.At).Key);
            var request = ReadLibraryMetadataAsync(path, cancellationToken);
            libraryMetadata[path] = (DateTimeOffset.UtcNow, request);
            return request;
        }
    }

    private async Task<JsonElement> ReadLibraryMetadataAsync(string path, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(Api(path), cancellationToken);
        return document.RootElement.Clone();
    }

    private async Task<LibraryPage> EnrichLibraryAsync(LibraryPage page, CancellationToken cancellationToken)
    {
        var result = new List<LibraryItem>();
        foreach (var original in page.Items)
        {
            var item = original;
            if (item.Playlist is { } playlist && string.IsNullOrEmpty(item.ArtworkUrl))
            {
                var path = playlist.Urn.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal)
                    ? "system-playlists/" + Uri.EscapeDataString(playlist.Urn)
                    : playlist.Id > 0 ? $"playlists/{playlist.Id}?representation=full" : null;
                if (path != null)
                {
                    try
                    {
                        var full = LibraryData.ParsePlaylist(await GetLibraryMetadataAsync(path, cancellationToken));
                        item = LibraryData.FromPlaylist(full) with { Key = original.Key };
                        if (string.IsNullOrEmpty(item.ArtworkUrl) && full.Tracks?.FirstOrDefault(t => t.Id > 0) is { } first)
                        {
                            var coverTrack = first;
                            if (string.IsNullOrEmpty(first.ArtworkUrl))
                            {
                                var json = await GetLibraryMetadataAsync($"tracks/{first.Id}", cancellationToken);
                                coverTrack = json.Deserialize(SoundCloudJson.Default.SoundCloudTrack) ?? first;
                            }
                            item = item with { ArtworkUrl = coverTrack.ArtworkUrl ?? coverTrack.User?.AvatarUrl };
                        }
                        else if (string.IsNullOrEmpty(item.ArtworkUrl)) item = item with { ArtworkUrl = full.User?.AvatarUrl };
                    }
                    catch (SoundCloudException error) when (error.StatusCode is 404 or 410) { }
                }
            }
            result.Add(item);
        }
        return new(result.ToArray(), page.NextHref);
    }
}
