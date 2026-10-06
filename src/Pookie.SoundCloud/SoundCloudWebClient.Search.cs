namespace Pookie.SoundCloud;

public enum SearchSection { All, Tracks, People, Albums, Playlists }

public sealed partial class SoundCloudWebClient
{
    public async Task<LibraryPage> SearchResultsAsync(string query, SearchSection section = SearchSection.All,
        CancellationToken cancellationToken = default)
    {
        var path = section switch
        {
            SearchSection.All => "search", SearchSection.Tracks => "search/tracks",
            SearchSection.People => "search/users", SearchSection.Albums => "search/albums",
            SearchSection.Playlists => "search/playlists_without_albums", _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        using var document = await GetJsonAsync(Api(path + "?q=" + Uri.EscapeDataString(query.Trim()) +
            "&limit=30&linked_partitioning=1"), cancellationToken);
        return LibraryData.Parse(document.RootElement);
    }

    public async Task<LibraryPage> GetSearchNextAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!BrowserRequestCommand.IsReadUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.AbsolutePath is not ("/search" or "/search/tracks" or "/search/users" or "/search/albums" or "/search/playlists" or "/search/playlists_without_albums"))
            throw new SoundCloudException("Неверный адрес следующей страницы поиска.");
        using var document = await GetJsonAsync(uri, cancellationToken);
        return LibraryData.Parse(document.RootElement);
    }
}
