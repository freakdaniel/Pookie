using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySearchLinksUiAsync()
    {
        var previousSession = api.Session; var previousTransport = api.BrowserTransport;
        var previousMe = me;
        var sawSearchPage = false;
        void ObservePage() { if (page.Value == Page.Search) sawSearchPage = true; }
        try
        {
            var fixture = new SearchLinksFixture();
            api.Session = new("searchfixture", "fixture-token", "Test"); api.BrowserTransport = fixture;
            me = new() { Id = 42, Username = "Search fixture" }; signedIn.Value = true;
            await ShowWorkspaceAsync();
            Window.Activate();
            await WaitForLikedLayoutAsync(() => Window.IsActive && !startupSplash.IsVisible && workspace.IsVisible);
            var playback = current?.Id; var playbackGeneration = playGeneration;
            page.Changed += ObservePage;
            var historyCount = navigationHistory.Count;

            // Exercise the same routed Enter event as the real header search.
            OpenTopSearch();
            await WaitForLikedLayoutAsync(() => topSearchInput.ActualWidth > 100);
            topSearchInput.Focus();
            ((ITextInputClient)topSearchInput).HandleTextInput(new("https://soundcloud.com/ar"));
            ((ITextInputClient)topSearchInput).HandleTextInput(new("tist/song"));
            if (query.Value != "https://soundcloud.com/artist/song")
                throw new InvalidOperationException("Pending link presentation interrupted a manually typed URL");
            query.Value = "";
            ((ITextInputClient)topSearchInput).HandleTextInput(new("  https://soundcloud.com/artist/song?secret_token=s-fixture  "));
            if (!topSearchInput.IsFocused || query.Value != topSearchInput.Text)
                throw new InvalidOperationException($"Header search did not acquire focus or bind its input: focused={topSearchInput.IsFocused}, query={query.Value}, text={topSearchInput.Text}");
            if (!topSearchBar.ShowsLink || topSearchInput.Opacity != 0 || topSearchBar.LinkLabel != "Ищу...")
                throw new InvalidOperationException("Pasted URL was displayed as raw text before recognition");
            await WaitForLikedLayoutAsync(() => topSearchBar.LinkLabel == SearchLinksFixture.TrackTitle && !topSearchBar.LinkTransitioning);
            if (page.Value != Page.Home || fixture.Resolutions != 1)
                throw new InvalidOperationException("URL recognition navigated before Enter or made duplicate requests");
            await WaitForLoginFrameAsync(); CaptureUiPreview("search-link-recognized", topSearchBar);
            RouteSettingsKey(Key.Enter);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState?.Track.Id == 91 &&
                !navigationHistory[navigationIndex].Pending);
            if (current?.Id != playback || playGeneration != playbackGeneration)
                throw new InvalidOperationException("Opening a track URL started playback");
            if (sawSearchPage || fixture.Resolutions != 1 || navigationHistory.Count != historyCount + 1)
                throw new InvalidOperationException("Enter repeated URL recognition or inserted an intermediate search page");

            OpenTopSearch(); query.Value = "https://soundcloud.com/artist/slow-preview";
            var oldPreview = searchLinkTask!;
            await fixture.PreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            query.Value = "Новый запрос";
            fixture.PreviewReady.SetResult();
            try { await oldPreview; }
            catch (OperationCanceledException)
            {
                if (query.Value != "Новый запрос") throw;
            }
            if (topSearchBar.ShowsLink || topSearchInput.Opacity != 1 || query.Value != "Новый запрос")
                throw new InvalidOperationException("A stale metadata reply replaced an edited search draft");
            CloseTopSearch(clear: false);

            foreach (var (url, expectedPage, expectedKey) in new[]
            {
                ("https://www.soundcloud.com/artist", Page.UserProfile, "user:7"),
                ("https://soundcloud.com/artist/sets/list", Page.LibraryCollection, "playlist:94"),
                ("https://soundcloud.com/artist/sets/album", Page.LibraryCollection, "playlist:95"),
                ("https://soundcloud.com/discover/sets/mix", Page.LibraryCollection, "playlist:soundcloud:system-playlists:mix")
            })
            {
                query.Value = url; await SearchAsync(SearchSection.Tracks);
                if (page.Value != expectedPage || navigationHistory[navigationIndex].Route.Item?.Key != expectedKey)
                    throw new InvalidOperationException($"Search URL opened the wrong page: {url}");
                if (url.EndsWith("/album", StringComparison.Ordinal) && !navigationHistory[navigationIndex].Route.Item!.IsAlbum)
                    throw new InvalidOperationException("Album URL lost its album type");
            }

            await MoveNavigationAsync(-1);
            if (sawSearchPage || page.Value != Page.LibraryCollection || navigationHistory[navigationIndex].Route.Item?.Key != "playlist:95")
                throw new InvalidOperationException("Link navigation inserted search results into history");
            page.Changed -= ObservePage;

            OpenTopSearch();
            query.Value = "https://www.soundcloud.com/artist";
            await WaitForLikedLayoutAsync(() => topSearchBar.LinkLabel == "Artist");
            topSearchInput.Focus(); RouteSettingsKey(Key.Backspace);
            if (query.Value.Length != 0 || topSearchBar.ShowsLink || topSearchInput.Opacity != 1)
                throw new InvalidOperationException("Backspace did not remove the entire URL chip");
            RouteSelectableTextKey(Key.Z, ModifierKeys.Control);
            await WaitForLikedLayoutAsync(() => topSearchBar.LinkLabel == "Artist");
            ((ITextInputClient)topSearchInput).HandleTextInput(new("Обычный поиск"));
            if (topSearchBar.ShowsLink || query.Value != "Обычный поиск" || topSearchInput.Opacity != 1)
                throw new InvalidOperationException("Typing did not replace the URL chip with ordinary text");
            CloseTopSearch(clear: false);

            query.Value = "Обычный поиск"; await SearchAsync(SearchSection.People);
            if (page.Value != Page.Search || fixture.Searches != 1 || fixture.LastSearchPath != "/search/users")
                throw new InvalidOperationException("Text search no longer respects its selected section");

            query.Value = "https://soundcloud.com/artist/slow";
            var delayed = SearchAsync();
            await fixture.ResolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ShowSettingsAsync(SettingsSection.General);
            fixture.ResolveReady.SetResult(); await delayed;
            if (page.Value != Page.Settings) throw new InvalidOperationException("A stale URL response replaced the current page");

            query.Value = "https://soundcloud.com/artist/missing";
            try { await SearchAsync(); throw new InvalidOperationException("Missing URL succeeded"); }
            catch (SoundCloudException error) when (error.StatusCode == 404)
            {
                if (searchLoading.Value || navigationHistory[navigationIndex].Pending)
                    throw new InvalidOperationException("A failed URL left search loading");
            }
            query.Value = "https://soundcloud.com/artist/song"; await SearchAsync();
            if (page.Value != Page.Track) throw new InvalidOperationException("Search did not recover after an unavailable link");
            Console.WriteLine("SEARCH_LINKS_UI_OK: paste displays a chip, metadata resolves before Enter, direct navigation without a search frame or duplicate request, native editing/undo, profiles/playlists/albums/mixes, text search, history, stale isolation and error recovery");
        }
        catch (Exception error)
        {
            VerificationFailure = error; Environment.ExitCode = 1;
            Console.Error.WriteLine($"SEARCH_LINKS_UI_FAILED: {error}");
        }
        finally
        {
            page.Changed -= ObservePage;
            api.Session = previousSession; api.BrowserTransport = previousTransport; me = previousMe;
            Window.Close();
        }
    }

    private sealed class SearchLinksFixture : ISoundCloudBrowserTransport
    {
        internal const string TrackTitle = "Общество — песня с очень длинным названием, которое должно обрезаться внутри строки поиска";
        private static readonly string Track = JsonSerializer.Serialize(new { kind = "track", id = 91, title = TrackTitle, duration = 120000, user = new { id = 7, username = "Artist" } });
        private const string User = """{"kind":"user","id":7,"username":"Artist"}""";
        private const string Playlist = """{"kind":"playlist","id":94,"title":"List","tracks":[],"user":{"id":7,"username":"Artist"}}""";
        private const string Album = """{"kind":"playlist","id":95,"title":"Album","is_album":true,"tracks":[],"user":{"id":7,"username":"Artist"}}""";
        private const string Mix = """{"kind":"system-playlist","id":"soundcloud:system-playlists:mix","title":"Mix","tracks":[]}""";
        internal int Searches { get; private set; }
        internal int Resolutions { get; private set; }
        internal string? LastSearchPath { get; private set; }
        internal TaskCompletionSource ResolveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ResolveReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PreviewStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PreviewReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var path = uri.AbsolutePath;
            if (path == "/resolve")
            {
                Resolutions++;
                var url = Uri.UnescapeDataString(uri.Query[5..]);
                if (url.EndsWith("/missing", StringComparison.Ordinal)) throw new SoundCloudException("Не найдено", 404);
                if (url.EndsWith("/slow-preview", StringComparison.Ordinal))
                { PreviewStarted.SetResult(); await PreviewReady.Task; }
                if (url.EndsWith("/slow", StringComparison.Ordinal))
                {
                    ResolveStarted.SetResult();
                    // Deliberately emulate a transport that ignores cancellation.
                    await ResolveReady.Task;
                }
                return JsonDocument.Parse(url switch
                {
                    "https://www.soundcloud.com/artist" => User,
                    "https://soundcloud.com/artist/sets/list" => Playlist,
                    "https://soundcloud.com/artist/sets/album" => Album,
                    "https://soundcloud.com/discover/sets/mix" => Mix,
                    _ => Track
                });
            }
            if (path.StartsWith("/search", StringComparison.Ordinal)) { Searches++; LastSearchPath = path; }
            return JsonDocument.Parse(path switch
            {
                "/tracks/91" => Track,
                "/users/7" => User,
                "/playlists/94" => Playlist,
                "/playlists/95" => Album,
                _ when path.StartsWith("/system-playlists/", StringComparison.Ordinal) => Mix,
                _ when path.EndsWith("/web-profiles", StringComparison.Ordinal) => "[]",
                _ => """{"collection":[],"next_href":null}"""
            });
        }

        public Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(JsonDocument.Parse(request.Kind == "sidebar" ? """{"data":{"playlists":[],"albums":[]}}"""
                : """{"data":{"trackComments":{"comments":[],"pageInfo":{"hasNextPage":false}}}}"""));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Unexpected write");
    }
}
