using System.Net;
using System.Text;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyLyricSearchAsync()
    {
        const string phrase = "и билет на самолет с серебристым крылом";
        using var handler = new LyricSearchFixture(phrase);
        using var http = new HttpClient(handler);
        var search = new LrcRedSearch(http);
        var cover = new SoundCloudTrack
        {
            Id = 42001, Title = "Пачка сигарет", Duration = 266000,
            User = new() { Username = "Другой исполнитель" }
        };
        var correct = cover with { Id = 42002, User = new() { Username = "Кино" } };
        var blocked = correct with { Id = 42003, Access = "blocked", PublisherMetadata = new() { Isrc = "RUB421401328" } };
        var version = correct with { Id = 42004, Title = "Пачка сигарет (Remix)" };
        var wrongDuration = correct with { Id = 42005, Duration = 165000 };
        var duplicate = cover with { Id = 42009, Title = "Виктор Цой - Пачка сигарет" };
        var matched = await search.FindAsync(phrase, (metadata, _) =>
        {
            if (metadata == "Виктор Цой Пачка сигарет")
                return Task.FromResult(new LibraryPage([LibraryItem.FromTrack(duplicate)], null));
            if (metadata != "Kino Пачка сигарет") throw new InvalidOperationException("Invalid metadata lookup.");
            return Task.FromResult(new LibraryPage(new[] { cover, blocked, version, wrongDuration, correct }.Select(LibraryItem.FromTrack).ToArray(), null));
        }, lifetime.Token);
        if (matched.Single().Track?.Id != correct.Id || handler.Calls != 2)
            throw new InvalidOperationException("Lyric lookup did not shorten the phrase or selected the wrong or a duplicate recording.");

        var originalSession = api.Session;
        var originalTransport = api.BrowserTransport;
        var browser = new PaginationBrowser();
        try
        {
            var session = new WebSession("fixture", "fixture-token", "Test");
            api.Session = session; api.BrowserTransport = browser;
            var regular = LibraryItem.FromTrack(new SoundCloudTrack { Id = 42006, Title = "Обычный результат" });
            var exact = LibraryItem.FromTrack(new SoundCloudTrack { Id = 42007, Title = phrase });
            var page = new LibraryPage([regular, exact, LibraryItem.FromTrack(correct)],
                "https://api-v2.soundcloud.com/search/tracks?cursor=lyrics-fixture");
            var confirmedExact = MergeSearchResults(page, [exact], phrase);
            if (confirmedExact.Items[0] != (exact with { MatchedByLyrics = true }) ||
                confirmedExact.Items.Count(item => item.MatchedByLyrics) != 1 || confirmedExact.Items.Length != page.Items.Length)
                throw new InvalidOperationException("A track found by both sources lost its attribution or was duplicated.");
            foreach (var lyricsFirst in new[] { false, true })
            {
                var ordinary = new TaskCompletionSource<LibraryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
                var lyrics = new TaskCompletionSource<LibraryItem[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (lyricsFirst) lyrics.SetResult(matched); else ordinary.SetResult(page);
                var searchLoad = NavigateRouteAsync(new(Page.Search, Search: phrase, Section: SearchSection.Tracks), generation =>
                {
                    searchSection.Value = SearchSection.Tracks;
                    return ShowSearchResultsAsync(ordinary.Task, lyrics.Task, phrase, SearchSection.Tracks, generation, loading!.Token);
                });
                if (searchLoad.IsCompleted || !searchLoadingView.Loading || !searchLoadingView.Skeleton.IsVisible ||
                    searchLyricsCredit.Value || searchResults is not { Items.Length: 0 })
                    throw new InvalidOperationException("Search displayed partial results while one source was still loading.");
                if (lyricsFirst) ordinary.SetResult(page); else lyrics.SetResult(matched);
                await searchLoad;
                if (searchLoadingView.Loading || !searchLyricsCredit.Value ||
                    !searchResults!.Items.Select(item => item.Track!.Id).SequenceEqual([42007L, 42002L, 42006L]))
                    throw new InvalidOperationException("Search did not reveal the ranked and deduplicated results together.");
            }

            var next = LoadMoreSearchAsync();
            browser.Reply.SetResult("""{"collection":[{"id":42002,"title":"Duplicate"},{"id":42008,"title":"Next track"}],"next_href":null}""");
            await next;
            if (!searchResults!.Items.Select(item => item.Track!.Id).SequenceEqual([42007L, 42002L, 42006L, 42008L]) ||
                !searchLyricsCredit.Value || searchResults.NextHref != null)
                throw new InvalidOperationException("Pagination did not preserve the combined search results.");
        }
        finally { api.Session = originalSession; api.BrowserTransport = originalTransport; }
        Console.WriteLine("LYRIC_SEARCH_OK: anonymous query, shorter phrase retry, recording validation, duplicate uploads, combined reveal in both reply orders, attribution and pagination");
    }

    private sealed class LyricSearchFixture(string phrase) : HttpMessageHandler
    {
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var expected = Calls++ == 0 ? phrase : "с серебристым крылом";
            if (uri.Host != "lrc.red" || uri.AbsolutePath != "/search.json" ||
                uri.Query != "?in=lyrics&q=" + Uri.EscapeDataString(expected) ||
                !request.Headers.UserAgent.ToString().StartsWith("Pookie/", StringComparison.Ordinal) ||
                request.Headers.Authorization != null)
                throw new InvalidOperationException("The lyric provider did not receive the expected anonymous query.");
            var json = Calls == 1 ? """{"hits":[]}""" :
                """
                {"hits":[
                  {"title":"Пачка сигарет","artist":"Kino","isrc":"RUB421401328","duration":266.018},
                  {"title":"Пачка сигарет","artist":"Виктор Цой, Кино","isrc":"TCADG1798107","duration":265.32}
                ]}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
