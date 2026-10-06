using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class SearchTests
{
    private const string Mixed = """
        {"collection":[
          {"kind":"track","id":1,"title":"Песня","user":{"id":10,"username":"Автор"}},
          {"kind":"user","id":10,"username":"Автор","followers_count":42},
          {"kind":"playlist","id":2,"title":"Альбом","is_album":true,"user":{"id":10,"username":"Автор"},"tracks":[]},
          {"kind":"track","id":1,"title":"Песня","user":{"id":10,"username":"Автор"}}],
          "next_href":"https://api-v2.soundcloud.com/search?cursor=next"}
        """;

    [Fact]
    public void DirectSearchResultsKeepTheirTypeAndDoNotUnwrapTheAuthor()
    {
        using var document = JsonDocument.Parse(Mixed);
        var result = LibraryData.Parse(document.RootElement);
        Assert.Equal(new[] { "track:1", "user:10", "playlist:2" }, result.Items.Select(item => item.Key));
        Assert.Equal("Автор", result.Items[0].Track!.Author);
        Assert.NotNull(result.Items[1].User);
        Assert.True(result.Items[2].IsAlbum);
        Assert.Equal("Альбом", result.Items[2].Title);
    }

    [Theory]
    [InlineData(SearchSection.All, "/search")]
    [InlineData(SearchSection.Tracks, "/search/tracks")]
    [InlineData(SearchSection.People, "/search/users")]
    [InlineData(SearchSection.Albums, "/search/albums")]
    [InlineData(SearchSection.Playlists, "/search/playlists_without_albums")]
    public async Task SectionsUseAuthenticatedBrowserReadsAndPreserveUnicode(SearchSection section, string path)
    {
        var browser = new Browser();
        using var http = new HttpClient(new NoNetwork());
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "fixture-token", "Test"), BrowserTransport = browser, RequireBrowserTransport = true };
        var result = await api.SearchResultsAsync("  Кис & 猫  ", section);
        Assert.Equal(path, Assert.Single(browser.Calls).AbsolutePath);
        Assert.Contains("q=" + Uri.EscapeDataString("Кис & 猫"), browser.Calls[0].Query);
        Assert.Equal(3, result.Items.Length);
        await api.GetSearchNextAsync(result.NextHref!);
        Assert.Equal(2, browser.Calls.Count);
        Assert.Contains("cursor=next", browser.Calls[1].Query);
    }

    [Theory]
    [InlineData("https://evil.test/search?cursor=2")]
    [InlineData("https://user@api-v2.soundcloud.com/search")]
    [InlineData("https://api-v2.soundcloud.com/search#secret")]
    [InlineData("https://api-v2.soundcloud.com/me")]
    [InlineData("https://api-v2.soundcloud.com/search/delete")]
    public async Task PaginationCannotReadOutsideSearch(string url)
    {
        var browser = new Browser();
        using var http = new HttpClient(new NoNetwork());
        var api = new SoundCloudWebClient(http) { BrowserTransport = browser };
        await Assert.ThrowsAsync<SoundCloudException>(() => api.GetSearchNextAsync(url));
        Assert.Empty(browser.Calls);
    }

    private sealed class Browser : ISoundCloudBrowserTransport
    {
        public List<Uri> Calls { get; } = [];
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        { Calls.Add(uri); return Task.FromResult(JsonDocument.Parse(Mixed)); }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected write");
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Search bypassed the browser session");
    }
}
