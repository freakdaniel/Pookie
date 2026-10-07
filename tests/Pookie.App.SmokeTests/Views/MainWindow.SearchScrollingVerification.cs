using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySearchScrollingAsync()
    {
        await NavigateAsync(Page.Library);
        await WaitForLikedLayoutAsync(() => overviewScroll.ActualHeight > 100 && SmoothScroll.Maximum(overviewScroll) > 400 && smoothScrolls.ContainsKey(overviewScroll));
        overviewScroll.SetScrollOffsets(0, 0);
        var preview = overviewSections["recent"].Grid;
        await WaitForLikedLayoutAsync(() => preview.ActualWidth > 100);
        var point = new Point(preview.Bounds.X + 30, preview.Bounds.Y + 30);
        RouteSearchTestWheel(point);
        await WaitForLikedLayoutAsync(() => overviewScroll.VerticalOffset > 20);
        var wheelOffset = overviewScroll.VerticalOffset;
        RouteSearchTestWheel(new Point(preview.Bounds.X + 30, overviewScroll.Bounds.Y + 40));
        await WaitForLikedLayoutAsync(() => overviewScroll.VerticalOffset > wheelOffset + 20);
        if (preview.FindVisualChild<ScrollViewer>() is not ScrollViewer { VerticalOffset: 0 })
            throw new InvalidOperationException("Overview wheel scrolled a nested card grid instead of the page");
        var artGate = new TaskCompletionSource<ImageSource?>();
        const string url = "https://i1.sndcdn.com/search-fixture-large.jpg";
        artworkRequests[url] = artGate.Task;
        var pixels = new byte[32 * 32 * 4];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
        {
            var i = (y * 32 + x) * 4;
            pixels[i] = (byte)(x < 16 ? 220 : 40);
            pixels[i + 1] = (byte)(y < 16 ? 120 : 40);
            pixels[i + 2] = (byte)(x < 16 ? 40 : 220);
            pixels[i + 3] = 255;
        }
        var art = ImageSource.FromBgraPixels(32, 32, pixels);
        var track = new SoundCloudTrack { Id = 70001, Title = "Кис — поиск", Duration = 126000, ArtworkUrl = url, User = new() { Id = 70, Username = "Исполнитель" } };
        var prepared = PrepareTrackArtworkAsync([track], lifetime.Token);
        var cover = new Image(); _ = ArtworkLayer(cover);
        SetCardArtwork(cover, null, url);
        if (prepared.IsCompleted || cover.Source != null || !artworkLoading[cover].IsVisible || !ReferenceEquals(SharedArtworkAsync(url), artGate.Task))
            throw new InvalidOperationException("Pending artwork became a placeholder or did not share its request");
        artGate.SetResult(art); await prepared;
        SetCardArtwork(cover, await GetLibraryArtworkAsync(track), url, finished: true);
        if (!ReferenceEquals(cover.Source, art) || artworkLoading[cover].IsVisible)
            throw new InvalidOperationException("Artwork shimmer did not resolve directly to the cover");

        var user = new SoundCloudUser { Id = 70, Username = "Исполнитель", FollowersCount = 1234, AvatarUrl = url };
        var playlist = new SoundCloudPlaylist { Id = 80, Title = "Альбом", IsAlbum = true, ArtworkUrl = url, TrackCount = 8, User = user };
        LibraryItem[] results = [LibraryItem.FromTrack(track), new("user:70", user.Username, "1 234 подписчика", url, User: user), LibraryData.FromPlaylist(playlist),
            LibraryItem.FromTrack(track with { Id = 70002, Title = "Кис-Кис — ЛБТД" }),
            LibraryItem.FromTrack(track with { Id = 70003, Title = "Кис — любимая песня" }),
            LibraryItem.FromTrack(track with { Id = 70004, Title = "Кис — ночной плейлист" }),
            LibraryData.FromPlaylist(playlist with { Id = 81, IsAlbum = false, Title = "Кис — подборка" })];
        async Task SearchFixture(SearchSection section, LibraryItem[] items) => await NavigateRouteAsync(new(Page.Search, Search: "Кис", Section: section), async _ =>
        {
            query.Value = "Кис"; searchSection.Value = section; searchHeading.Value = "Результаты для «Кис»";
            await PrepareCollectionArtworkAsync(items, lifetime.Token, rows: true);
            SetSearchResults(new(items, null));
        });
        await SearchFixture(SearchSection.All, results);
        await WaitForLikedLayoutAsync(() => searchViews.Values.Any(view => view.Block?.Kind == SearchBlockKind.Hero && view.Hero.Card.ActualWidth > 100));
        var hero = searchViews.Values.Single(view => view.Block?.Kind == SearchBlockKind.Hero).Hero;
        await WaitForLikedLayoutAsync(() => hero.Backdrop.Source != null && hero.Backdrop.Opacity > .999);
        if (hero.Item?.Key != "track:70001" || hero.Tracks.Count(row => row.Track != null) != 4 || hero.Tracks[0].Root.ActualHeight != 64 ||
            !SearchBlocks().Any(block => block.Title == "Исполнители") || !SearchBlocks().Any(block => block.Title == "Альбомы") || !SearchBlocks().Any(block => block.Title == "Плейлисты"))
            throw new InvalidOperationException("Search did not show a best result, compact songs and grouped entity cards");
        var songsHeading = hero.SongsSection.Children.OfType<TextBlock>().Single();
        if (Math.Abs(hero.Tracks[0].Cover.Bounds.X - songsHeading.Bounds.X) > 1 ||
            hero.Tracks[0].HoverFill.Bounds.X >= songsHeading.Bounds.X ||
            !hero.SongsSection.Bounds.Contains(hero.Tracks[0].HoverFill.Bounds))
            throw new InvalidOperationException("Search compact song hover background clips or no longer aligns its cover with the heading.");
        var searchTitle = (TextBlock)VisualTree.Find(Window.Content, element => element is TextBlock title && title.Text == searchHeading.Value)!;
        if (searchTitle.Bounds.Y - contentFrame.Bounds.Y > contentFrame.Padding.Top + 1 ||
            ((DockPanel)searchLoadingView.Root.Parent!.Parent!).Bounds.Bottom - searchList.Bounds.Bottom > 1)
            throw new InvalidOperationException("Search retained the empty page header or pagination footer spacing");
        CaptureUiPreview("search-all");
        var savedTrack = current; var savedPlaying = isPlaying.Value;
        timer.Stop();
        try
        {
            current = track; isPlaying.Value = true; likedIds.Add(track.Id); RefreshSearchPlayback();
            if (!ReferenceEquals(hero.Play.Source, Icons.Source("pause-solid", Surface)) ||
                !ReferenceEquals(hero.Tracks[0].Play.Source, Icons.Source("pause-solid")) ||
                !ReferenceEquals(hero.Tracks[0].Heart.Source, Icons.Source("heart-filled", LikedHeart)))
                throw new InvalidOperationException("Search playback and like icons did not react immediately");
            isPlaying.Value = false; RefreshSearchPlayback();
            if (!ReferenceEquals(hero.Tracks[0].Play.Source, Icons.Source("play-solid")))
                throw new InvalidOperationException("Search pause did not restore its play icon");
        }
        finally { current = savedTrack; isPlaying.Value = savedPlaying; likedIds.Remove(track.Id); RefreshSearchPlayback(); timer.Start(); }
        Window.WindowSize = WindowSize.Resizable(720, 840, minWidth: 640, minHeight: 680);
        await WaitForLikedLayoutAsync(() => Window.ClientSize.Width < 760 && searchViews.Values.Any(view => view.Block?.Kind == SearchBlockKind.Hero &&
            view.Hero.SongsSection.Bounds.Y > view.Hero.Card.Bounds.Bottom));
        hero = searchViews.Values.Single(view => view.Block?.Kind == SearchBlockKind.Hero).Hero;
        CaptureUiPreview("search-narrow");
        Window.WindowSize = WindowSize.Resizable(1000, 840, minWidth: 1000, minHeight: 680);
        await WaitForLikedLayoutAsync(() => Window.ClientSize.Width >= 999 && searchViews.Values.Any(view => view.Block?.Kind == SearchBlockKind.Hero &&
            view.Hero.SongsSection.Bounds.X > view.Hero.Card.Bounds.Right));
        await SearchFixture(SearchSection.People, [results[1]]);
        await MoveNavigationAsync(-1);
        if (page.Value != Page.Search || searchSection.Value != SearchSection.All || searchResults?.Items.Length != results.Length || query.Value != "Кис")
            throw new InvalidOperationException("Search back history lost the category, query or mixed results");
        await MoveNavigationAsync(1);
        if (searchSection.Value != SearchSection.People || searchResults?.Items.Single().User?.Id != user.Id)
            throw new InvalidOperationException("Search forward history lost people results");
        var playlistFixtures = Enumerable.Range(0, 36).Select(index => LibraryData.FromPlaylist(
            playlist with { Id = 72000 + index, IsAlbum = false, Title = "Подборка " + index })).ToArray();
        await SearchFixture(SearchSection.Playlists, playlistFixtures);
        await WaitForLikedLayoutAsync(() => SearchBlocks().First().Items?.Length == 4 && renderedSearchColumns == 4);
        Window.WindowSize = WindowSize.Resizable(1440, 840, minWidth: 640, minHeight: 680);
        await WaitForLikedLayoutAsync(() => Window.ClientSize.Width >= 1439 && renderedSearchColumns == 6 &&
            searchViews.Values.Any(view => view.Block?.Kind == SearchBlockKind.Cards && view.Block.Items?.Length == 6 &&
                view.CardGrid.ItemsSource.Count == 6 && view.CardGrid.ActualWidth > 1000));
        if (SearchBlocks().Where(block => block.Kind == SearchBlockKind.Cards).SelectMany(block => block.Items!).Count() != 36)
            throw new InvalidOperationException("Expanding a search grid lost cards while regrouping rows");
        await Task.Delay(350, lifetime.Token);
        var visiblePlaylistCards = collectionCards.Values.Where(card => card.Item.Playlist is { Id: >= 72000 and < 72036 } && card.Cover.Bounds.Y >= searchList.Bounds.Y && card.Cover.Bounds.Y < searchList.Bounds.Bottom).ToArray();
        if (visiblePlaylistCards.GroupBy(card => Math.Round(card.Cover.Bounds.Y)).First().Count() != 6)
            throw new InvalidOperationException("Expanded search bound six cards but did not arrange them across the visible row");
        CaptureUiPreview("search-playlists-wide");
        Window.WindowSize = WindowSize.Resizable(1000, 840, minWidth: 1000, minHeight: 680);
        await WaitForLikedLayoutAsync(() => Window.ClientSize.Width < 1001 && renderedSearchColumns == 4 &&
            searchViews.Values.Any(view => view.Block?.Kind == SearchBlockKind.Cards && view.Block.Items?.Length == 4));
        await SearchFixture(SearchSection.Tracks, Enumerable.Range(0, 100).Select(index => LibraryItem.FromTrack(track with { Id = 71000 + index })).ToArray());
        await WaitForLikedLayoutAsync(() => searchViews.Values.Any(view => view.LibraryTrack is { Track.Id: 71000 } row &&
            row.Root.ActualHeight == 160 && row.Cover.ActualWidth == 160 && row.Waveform.ActualHeight == 68));
        var librarySearchRows = searchViews.Values.Where(view => view.Block?.Kind == SearchBlockKind.LibraryTrack &&
            view.LibraryTrack?.Track != null).Select(view => view.LibraryTrack!).OrderBy(row => row.Root.Bounds.Y).ToArray();
        var firstSearchRow = librarySearchRows.First(row => row.Track!.Id == 71000);
        var nextSearchRow = librarySearchRows.First(row => row.Track!.Id == 71001);
        if (searchList.ItemsSource.Count != 100 || librarySearchRows.Length > 20 ||
            Math.Abs(nextSearchRow.Root.Bounds.Y - firstSearchRow.Root.Bounds.Y - 196) > 1 ||
            firstSearchRow.Author.Bounds.Y >= firstSearchRow.Title.Bounds.Y ||
            !ReferenceEquals(likedRows[firstSearchRow.Root], firstSearchRow) ||
            searchLoadingView.Skeleton.CompactList || pageItems[searchList].Style != LoadingRowStyle.Waveform)
            throw new InvalidOperationException("Track search did not reuse the virtualized library rows, spacing and waveform skeletons");
        timer.Stop();
        var savedPosition = likedPlaybackPosition;
        try
        {
            current = firstSearchRow.Track; isPlaying.Value = true; RefreshLikedRows(30);
            if (!firstSearchRow.ShowsPause || firstSearchRow.Waveform.Progress < .2 || nextSearchRow.ShowsPause)
                throw new InvalidOperationException("Library-style search rows did not react to playback and progress");
            isPlaying.Value = false; RefreshLikedRows();
            if (firstSearchRow.ShowsPause) throw new InvalidOperationException("Search waveform row did not restore play after pausing");
        }
        finally { current = savedTrack; isPlaying.Value = savedPlaying; RefreshLikedRows(savedPosition); timer.Start(); }
        await Task.Delay(350, lifetime.Token);
        CaptureUiPreview("search-tracks");

        var previousSession = api.Session; var previousTransport = api.BrowserTransport;
        var browser = new PaginationBrowser();
        try
        {
            api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = browser;
            // A mixed response can insert another artist row above the tracks being
            // read. Preserve the visible track, not an obsolete absolute pixel offset.
            var groupedTracks = Enumerable.Range(0, 60).Select(index => LibraryItem.FromTrack(track with { Id = 73000 + index })).ToArray();
            await SearchFixture(SearchSection.All, results.Where(item => item.User != null || item.Playlist != null).Concat(groupedTracks).ToArray());
            searchResults = searchResults! with { NextHref = "https://api-v2.soundcloud.com/search?cursor=grouped" };
            await WaitForLikedLayoutAsync(() => NavigationScroll() is { } scroll && SmoothScroll.Maximum(scroll) > 1000);
            var groupedScroll = NavigationScroll()!;
            await Task.Delay(200, lifetime.Token);
            searchList.ScrollIntoView(searchList.ItemsSource.Count - 1); CheckPageEnd();
            try { await WaitForLikedLayoutAsync(() => browser.Calls == 1 && paginationLoading.Value); }
            catch (Exception error) { throw new InvalidOperationException($"Grouped pagination did not start: calls={browser.Calls}, loading={paginationLoading.Value}, cursor={PageCursor()}, failed={failedPageCursor}, status={status.Value}, offset={groupedScroll.VerticalOffset}, max={SmoothScroll.Maximum(groupedScroll)}", error); }
            await Task.Delay(150, lifetime.Token);
            searchList.ScrollIntoView(40);
            await Task.Delay(150, lifetime.Token);
            var anchor = compactTrackRows.Where(row => row.Track?.Id is >= 73000 and < 73060 &&
                row.Root.Bounds.Y >= groupedScroll.Bounds.Y && row.Root.Bounds.Y < groupedScroll.Bounds.Bottom).OrderBy(row => row.Root.Bounds.Y).First();
            var anchorId = anchor.Track!.Id; var anchorY = anchor.Root.Bounds.Y;
            var newArtists = Enumerable.Range(71, 4).Select(id => (object)new { kind = "user", id, username = "New artist " + id });
            var newTracks = Enumerable.Range(73060, 10).Select(id => (object)new { kind = "track", id, title = "Next track " + id, duration = 120000 });
            browser.Reply.SetResult(JsonSerializer.Serialize(new { collection = newArtists.Concat(newTracks).ToArray(), next_href = (string?)null }));
            await WaitForLikedLayoutAsync(() => !paginationLoading.Value && searchResults?.NextHref == null);
            await Task.Delay(200, lifetime.Token);
            var retained = compactTrackRows.FirstOrDefault(row => row.Track?.Id == anchorId && Math.Abs(row.Root.Bounds.Y - anchorY) < 2);
            if (retained == null) throw new InvalidOperationException($"Grouped pagination moved track {anchorId} from Y={anchorY}: now={string.Join(';', compactTrackRows.Where(row => row.Track?.Id == anchorId).Select(row => row.Root.Bounds.Y))}, offset={groupedScroll.VerticalOffset}");
            browser = new PaginationBrowser(); api.BrowserTransport = browser;
            likedFilter.Text = ""; likesAsList.Value = true;
            var fixtures = Enumerable.Range(1, 30).Select(id => new SoundCloudTrack { Id = 80000 + id, Title = "Трек " + id, Duration = 120000 }).ToArray();
            ShowLibraryTracks(); ReplaceTracks(new(fixtures, "https://api-v2.soundcloud.com/users/42/likes?cursor=next"));
            await WaitForLikedLayoutAsync(() => NavigationScroll() is { } scroll && SmoothScroll.Maximum(scroll) > 1000);
            var viewer = NavigationScroll()!;
            await WaitForLikedLayoutAsync(() => smoothScrolls.ContainsKey(viewer));
            if (((DockPanel)likesLoadingView.Root.Parent!.Parent!).Bounds.Bottom - likedList.Bounds.Bottom > 15)
                throw new InvalidOperationException("Likes retained the empty footer and its dock spacing");
            var motion = smoothScrolls[viewer];
            viewer.SetScrollOffsets(0, 0);
            motion.Scroll(240);
            if (viewer.VerticalOffset > 1) throw new InvalidOperationException("Smooth scrolling jumped to its destination immediately");
            await WaitForLikedLayoutAsync(() => viewer.VerticalOffset is > 1 and < 239);
            motion.Scroll(240);
            await WaitForLikedLayoutAsync(() => Math.Abs(viewer.VerticalOffset - 480) < 1);
            var previousOffset = viewer.VerticalOffset;
            for (var index = 0; index < 20; index++)
            {
                motion.Scroll(48);
                await Task.Delay(16, lifetime.Token);
                if (viewer.VerticalOffset < previousOffset - .5)
                    throw new InvalidOperationException("Continuous wheel input reversed the scroll movement");
                previousOffset = viewer.VerticalOffset;
            }
            await WaitForLikedLayoutAsync(() => Math.Abs(viewer.VerticalOffset - 1440) < 1);
            motion.Scroll(240); viewer.SetScrollOffsets(0, 120);
            await Task.Delay(280, lifetime.Token);
            if (Math.Abs(viewer.VerticalOffset - 120) > 1) throw new InvalidOperationException("Scrolling animation overrode an external scroll change");
            viewer.SetScrollOffsets(0, SmoothScroll.Maximum(viewer));
            await WaitForLikedLayoutAsync(() => browser.Calls == 1 && paginationLoading.Value);
            if (pageItems[likedList].LoadingCount != 3 || likedList.ItemsSource.GetItem(30) is not LoadingSlot)
                throw new InvalidOperationException("Pagination did not append skeletons inside the list");
            await Task.Delay(150, lifetime.Token);
            viewer.SetScrollOffsets(0, SmoothScroll.Maximum(viewer));
            await Task.Delay(150, lifetime.Token);
            CaptureUiPreview("likes-pagination-skeleton");
            likesAsList.Value = false;
            if (pageItems[likedList].LoadingCount != 0 || pageItems[likedGrid].LoadingCount != libraryColumns)
                throw new InvalidOperationException("Pagination skeletons did not follow a list/grid view change");
            likesAsList.Value = true;
            // The user keeps scrolling while the network reply is pending. The arriving
            // page must preserve this position, rather than the request-start offset.
            viewer.SetScrollOffsets(0, viewer.VerticalOffset - 260);
            var offset = viewer.VerticalOffset;
            for (var i = 0; i < 10; i++) CheckPageEnd();
            if (browser.Calls != 1) throw new InvalidOperationException("Infinite scrolling issued overlapping page requests");
            browser.Reply.SetResult("{\"collection\":[{\"track\":{\"id\":80030,\"title\":\"Duplicate\"}},{\"track\":{\"id\":80031,\"title\":\"Next\"}}],\"next_href\":null}");
            await WaitForLikedLayoutAsync(() => tracks.Count == 31 && !paginationLoading.Value && pendingNavigationScroll == null);
            if (Math.Abs(viewer.VerticalOffset - offset) > 1 || nextHref != null || pageItems[likedList].LoadingCount != 0 || likedList.ItemsSource.Count != 31)
                throw new InvalidOperationException("Appending a page moved the viewport or retained a finished cursor");
            browser.Reply = new();
            ReplaceTracks(new(fixtures, "https://api-v2.soundcloud.com/users/42/likes?cursor=failed"));
            viewer.SetScrollOffsets(0, SmoothScroll.Maximum(viewer)); CheckPageEnd();
            await WaitForLikedLayoutAsync(() => browser.Calls == 2);
            browser.Reply.SetException(new HttpRequestException("Fixture network failure"));
            await WaitForLikedLayoutAsync(() => !paginationLoading.Value && failedPageCursor != null);
            for (var i = 0; i < 10; i++) CheckPageEnd();
            if (browser.Calls != 2 || pageItems[likedList].LoadingCount != 0)
                throw new InvalidOperationException("Failed pagination silently retried in a loop");
            browser.Reply = new();
            ReplaceTracks(new(fixtures, "https://api-v2.soundcloud.com/users/42/likes?cursor=late"));
            viewer.SetScrollOffsets(0, SmoothScroll.Maximum(viewer)); CheckPageEnd();
            await WaitForLikedLayoutAsync(() => browser.Calls == 3);
            await NavigateAsync(Page.Feed);
            var feedIds = tracks.Select(item => item.Id).ToArray();
            browser.Reply.TrySetResult("{\"collection\":[{\"track\":{\"id\":89999,\"title\":\"Late\"}}]}");
            await Task.Delay(100, lifetime.Token);
            if (!feedIds.SequenceEqual(tracks.Select(item => item.Id)) || paginationLoading.Value)
                throw new InvalidOperationException("A stale pagination response changed the new page");
        }
        finally { api.Session = previousSession; api.BrowserTransport = previousTransport; StopCardArtwork(cover); }
        Console.WriteLine("UI_SEARCH_SCROLL_OK: overview wheel routing through cards, removed pagination indicators, search header spacing, responsive search regrouping from 4 to 6 columns and back, shared virtualized library track rows and waveform skeletons, compact overview songs, in-page pagination skeletons, immediate playback/like icons, category history, cover readiness, continuous smooth wheel input, single pagination requests, stable viewport while appending and inserting grouped results, cancellation");
    }

    private sealed class PaginationBrowser : ISoundCloudBrowserTransport
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<string> Reply { get; set; } = new();
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        { Calls++; return JsonDocument.Parse(await Reply.Task.WaitAsync(cancellationToken)); }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected write");
    }

    private void RouteSearchTestWheel(Point point)
    {
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", throwOnError: true)!;
        var method = router.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "MouseWheel" && method.GetParameters().Length == 8);
        method.Invoke(null, [Window, point, new Point(0, 0), new Vector(0, -3), false, false, false, ModifierKeys.None]);
    }
}
