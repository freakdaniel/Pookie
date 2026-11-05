using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.App.Home;
using Pookie.App.Playback;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyHomeUiAsync()
    {
        var originalPlayer = player;
        try
        {
            if (OperatingSystem.IsWindows() && !ProbeSetWindowPos(Window.Handle, -1, 0, 0, 0, 0, 0x53))
                throw new InvalidOperationException("Could not expose the native Home fixture.");
            if (Environment.GetEnvironmentVariable("POOKIE_MIX_PROBE") is { Length: > 0 } mixProbe)
            {
                await ProbeHomeMixCarouselAsync(mixProbe == "live");
                return;
            }
            Window.WindowSize = WindowSize.Resizable(1400, 980, minWidth: 1000, minHeight: 680);
            var transport = new HomeFixtureTransport();
            api.Session = new("homefixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Home fixture" }; signedIn.Value = true;
            await imageDiskCache.WriteAsync(HomeFixtureTransport.Artwork, ArtworkFixtureBitmap(500), lifetime.Token);
            await ShowWorkspaceAsync();
            Window.Activate();
            var load = NavigateHomeAsync();
            await WaitForLikedLayoutAsync(() => HomeItems("recent").Length == 6 && homePending.Contains("recommendations"));
            if (!homeSections.Single(section => section.Key == "recommendations").Loading.Skeleton.IsVisible)
                throw new InvalidOperationException("Slow recommendation request hid the loaded recent section or lost its skeleton.");
            transport.RelatedReady.SetResult(); await load;
            await WaitForLikedLayoutAsync(() => homeSections.All(section => section.Root.IsVisible) && libraryColumns == 6 &&
                collectionCards.Keys.Any(root => homeSections[0].Cards!.IsAncestorOf(root) && root.ActualWidth > 100));
            if (HomeItems("recommendations").Length != 12 || HomeItems("releases").Length != 6 || HomeItems("mixes").Length != 7 ||
                !HomeItems("made-for-you").Select(item => item.Title).SequenceEqual(new[] { "Daily Drops", "Weekly Wave" }) ||
                !HomeItems("trends").Select(item => item.Title).Take(3).SequenceEqual(new[] { "Hip Hop", "All Genres", "Electronic" }) || homePending.Count != 0)
                throw new InvalidOperationException("Personal Home did not bind the expected independent sections.");
            if (homeSections.Any(section => section.Key == "recent"))
                throw new InvalidOperationException("Removed Continue section is still on Home.");
            await WaitForLikedLayoutAsync(() => homeSections.All(section => !section.Loading.Skeleton.IsVisible));
            if (homeLikesCard.CornerRadius != 16 || homeLikesCard.Height != 88 || homeHistoryCard.CornerRadius != 16 ||
                homeLikesSubtitle.Text != "6 треков" || homeHistorySubtitle.Text.Length == 0)
                throw new InvalidOperationException("Home shortcuts lost their card shape or personal captions.");
            await WaitForLikedLayoutAsync(() => homeMixCarousel.ActualHeight > 290 && homeMixCarousel.MotionActive && homeChartRows.Count == 10);
            await WaitForLoginFrameAsync(); CaptureUiPreview("home-wide");
            player = null;
            var mixPoint = FindHomeMixPoint(homeMixCarousel.FrontIndex);
            MoveActionPointer(mixPoint);
            await WaitForLikedLayoutAsync(() => !homeMixCarousel.MotionActive);
            homeMixCarousel.Focus();
            var still = homeMixCarousel.FrontIndex;
            MoveActionPointer(new Point(homeMixCarousel.Bounds.X + 8, mixPoint.Y));
            await WaitForLikedLayoutAsync(() => homeMixCarousel.FrontIndex != still && homeMixCarousel.MotionActive);
            MoveActionPointer(FindHomeMixPoint(homeMixCarousel.FrontIndex));
            await WaitForLikedLayoutAsync(() => !homeMixCarousel.MotionActive);

            var neighbor = (homeMixCarousel.FrontIndex + 1) % 7;
            var selectedMix = HomeItems("mixes")[neighbor];
            RouteWaveformClick(FindHomeMixPoint(neighbor));
            await WaitForLikedLayoutAsync(() => page.Value == Page.LibraryCollection && displayedCollection?.Key == selectedMix.Key);
            if (playbackQueue.Context != null) throw new InvalidOperationException("Opening a front-row mix launched playback.");
            await NavigateHomeAsync();
            shellScroll.SetScrollOffsets(0, 0);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && homeMixCarousel.ActualHeight > 290);
            MoveActionPointer(FindHomeMixPoint(homeMixCarousel.FrontIndex));
            await WaitForLikedLayoutAsync(() => !homeMixCarousel.MotionActive);
            var rear = (homeMixCarousel.FrontIndex + 3) % 7;
            RouteWaveformClick(FindHomeMixPoint(rear));
            await WaitForLikedLayoutAsync(() => homeMixCarousel.FrontIndex == rear);
            if (page.Value != Page.Home || playbackQueue.Context != null)
                throw new InvalidOperationException("Selecting a rear cover opened a page or started playback.");

            MoveActionPointer(FindHomeMixPoint(rear));
            await WaitForLikedLayoutAsync(() => !homeMixCarousel.MotionActive);
            var playIndex = (rear + 1) % 7;
            var sidePlay = FindHomeMixPoint(playIndex, play: true);
            MoveActionPointer(sidePlay);
            RouteWaveformClick(sidePlay);
            await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == HomeItems("mixes")[playIndex].Key && homeMixCarousel.FrontIndex == playIndex);
            current = playbackQueue.Current!.Track;
            isPlaying.Value = true;
            RefreshHomeMixPlayback();
            await WaitForLikedLayoutAsync(() => homeMixCarousel.PlayingEmphasis > .99 && !homeMixCarousel.MotionActive);
            homeMixCarousel.Focus();
            RouteSettingsKey(Key.Right);
            var manualIndex = (playIndex + 1) % 7;
            await WaitForLikedLayoutAsync(() => homeMixCarousel.FrontIndex == manualIndex && homeMixCarousel.FrontCoverEmphasis > .99 && !homeMixCarousel.MotionActive);
            var rest = System.Diagnostics.Stopwatch.StartNew();
            using (var idleObservation = new PeriodicTimer(TimeSpan.FromMilliseconds(25)))
                while (rest.Elapsed < TimeSpan.FromMilliseconds(1200)) await idleObservation.WaitForNextTickAsync(lifetime.Token);
            if (homeMixCarousel.FrontIndex != manualIndex || homeMixCarousel.MotionActive)
                throw new InvalidOperationException("Orbit returned early or redrew during its waiting period.");
            await WaitForLikedLayoutAsync(() => homeMixCarousel.FrontIndex == playIndex && !homeMixCarousel.MotionActive);
            if (rest.Elapsed < TimeSpan.FromSeconds(3))
                throw new InvalidOperationException("Orbit did not wait three seconds after manual scrolling settled.");
            current = null;
            isPlaying.Value = false;
            playbackQueue.Clear();
            RefreshHomeMixPlayback();
            mixPoint = FindHomeMixPoint(homeMixCarousel.FrontIndex);
            MoveActionPointer(mixPoint);
            RouteHomeCarouselDrag(mixPoint, new Point(mixPoint.X - 160, mixPoint.Y));
            await WaitForLikedLayoutAsync(() => homeMixCarousel.FrontIndex != playIndex);
            if (playbackQueue.Context != null)
                throw new InvalidOperationException("Dragging a mix launched playback.");
            CaptureUiPreview("home-mixes");
            shellScroll.SetScrollOffsets(0, shellScroll.VerticalOffset + homeChart.Bounds.Y - startupHeader.Bounds.Bottom - 20);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && !homeMixCarousel.MotionActive && homeChartOpen.Bounds.Y > startupHeader.Bounds.Bottom &&
                homeChartOpen.Bounds.Bottom < homeScroll.Bounds.Bottom);
            var picker = homeChartGenreSlot.Children.OfType<MaterialSelect>().Single();
            if (picker.SelectedText != "Все жанры" || !homeChartRows.Select(row => row.Track!.Id).SequenceEqual(transport.Chart.Take(10).Select(track => track.Id)) ||
                homeChartLeft.Children.Count != 5 || homeChartRight.Children.Count != 5)
                throw new InvalidOperationException("Home did not default to the general chart in SoundCloud order.");
            CaptureUiPreview("home-trending");
            await VerifyTrendingActionsAsync(homeChartRows[0]);
            var chartScroll = shellScroll.VerticalOffset;
            picker.SelectedIndex = 0;
            await WaitForLikedLayoutAsync(() => homeChartRows.Count == 10 && homeChartRows[0].Track?.Id == transport.Chart[^1].Id && shellPageScroll.IsReady);
            if (Math.Abs(shellScroll.VerticalOffset - chartScroll) > 1)
                throw new InvalidOperationException("Switching genres moved the page vertically.");
            ClickHomeElement(homeChartOpen);
            await WaitForLikedLayoutAsync(() => page.Value == Page.LibraryCollection &&
                displayedCollection?.Key == "playlist:soundcloud:system-playlists:trending-by-genre:genre-0" &&
                activeCollection?.Items.Length == transport.Chart.Length);
            await NavigateHomeAsync();
            shellScroll.SetScrollOffsets(0, 0);
            await WaitForLoginFrameAsync();
            homeScroll.SetScrollOffsets(0, 400); await WaitForLoginFrameAsync(); CaptureUiPreview("home-recommendations");
            if (homeScroll.VerticalOffset < 350) throw new InvalidOperationException("Home sections are not vertically scrollable.");
            shellScroll.SetScrollOffsets(0, 0);
            Window.WindowSize = WindowSize.Resizable(1000, 980, minWidth: 1000, minHeight: 680);
            var recommendations = homeSections.Single(section => section.Key == "recommendations");
            // A 1000-DIP native window rounds to physical pixels at fractional display DPI.
            await WaitForLikedLayoutAsync(() => libraryColumns == 4 && Math.Abs(homeMixCarousel.ActualHeight - 258) < 1 &&
                recommendations.Cards!.ItemsSource.Count == 4);
            await WaitForLoginFrameAsync(); CaptureUiPreview("home-narrow");
            Window.WindowSize = WindowSize.Resizable(1400, 980, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => libraryColumns == 6 && recommendations.Cards!.ItemsSource.Count == 6);
            shellScroll.SetScrollOffsets(0, 0);
            await WaitForLoginFrameAsync();
            if (homeChart.Bounds.Bottom > recommendations.Root.Bounds.Top)
                throw new InvalidOperationException("The chart must appear above the discovery shelf.");
            shellScroll.SetScrollOffsets(0, shellScroll.VerticalOffset + recommendations.Root.Bounds.Y - startupHeader.Bounds.Bottom - 20);
            await WaitForLoginFrameAsync();
            await WaitForLikedLayoutAsync(() => collectionCards.Keys.Any(root => recommendations.Cards!.IsAncestorOf(root) && root.ActualWidth > 100));
            var root = collectionCards.Keys.First(root => recommendations.Cards!.IsAncestorOf(root) && root.ActualWidth > 100);
            var card = CollectionCard(root)!; var id = card.Item!.Track!.Id;
            CaptureUiPreview("home-card-before-hover");
            MoveActionPointer(new(card.Frame.Bounds.X + 12, card.Frame.Bounds.Y + 12));
            await WaitForLikedLayoutAsync(() => card.Playback.Opacity > .999);
            ClickHomeElement(card.Playback.PlaybackButton!);
            await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == "home:recommendations");
            if (page.Value != Page.Home || playbackQueue.Current?.Track.Id != id || playbackQueue.Snapshot.Source.Count != 12)
                throw new InvalidOperationException("Home play button navigated or copied the wrong queue.");
            var queueIds = playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).ToArray();
            var reads = transport.HomeReads;
            ClickHomeElement(card.Title);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState?.Track.Id == id && !trackDetailState.MetadataLoading);
            if (current != null || !queueIds.SequenceEqual(playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id)))
                throw new InvalidOperationException("Home card navigation started playback or replaced its queue.");
            localRecent.Insert(0, LibraryItem.FromTrack(transport.Recent[5]));
            await NavigateHomeAsync();
            if (HomeItems("recent")[0].Track?.Id != 106) throw new InvalidOperationException("Cached Home did not include the latest local playback.");
            if (transport.HomeReads != reads) throw new InvalidOperationException("Cached Home repeated section requests on return.");
            transport.FailMixes = true;
            await NavigateHomeAsync(force: true);
            if (!homeErrors.Contains("mixes") || HomeItems("recent").Length != 6 || HomeItems("recommendations").Length != 12 ||
                homeSections.Any(section => section.Shelf == null && !section.Root.IsVisible))
                throw new InvalidOperationException("One failed section cleared other Home content or has no retry indication.");
            transport.FailMixes = false;
            await NavigateHomeAsync(force: true);
            if (homeErrors.Count != 0 || HomeItems("mixes").Length != 7) throw new InvalidOperationException("Home refresh did not recover a failed section.");

            // Compact rows now live in the feed's recent sidebar; the former
            // standalone list is no longer attached to the feed page.
            localRecent.Clear();localRecent.AddRange(transport.Recent.Select(track=>LibraryItem.FromTrack(track)));
            await OpenFeedAsync(force:true);
            Window.Activate();
            shellScroll.SetScrollOffsets(0, 0);
            await WaitForLoginFrameAsync();
            await WaitForLikedLayoutAsync(() => Window.IsActive && shellPageScroll.IsReady && feedSidebarRows.Any(row => row.Track?.Id == 101 && row.Root.ActualWidth > 100));
            var row = feedSidebarRows.First(row => row.Track?.Id == 101);
            if (row.Title.ActualWidth > row.Root.ActualWidth / 2) throw new InvalidOperationException("Short compact title stretches across the row.");
            MoveActionPointer(new(row.Title.Bounds.Right + 60, row.Title.Bounds.Y + 8));
            if (row.Title.Opacity != 1) throw new InvalidOperationException("Empty row space triggers the title link hover.");
            var restingTitle = row.Title.Foreground;
            var titlePoint = SettingsCenter(row.Title);
            MoveActionPointer(titlePoint);
            var hoveredTitle = Color.FromArgb((byte)Math.Round(restingTitle.A*.72),restingTitle.R,restingTitle.G,restingTitle.B);
            await WaitForLikedLayoutAsync(() => row.Title.Foreground == hoveredTitle);
            if (row.Title.Opacity != 1)
                throw new InvalidOperationException("Actual title lost its foreground hover or allocated an opacity layer.");
            playbackQueue.Clear(); MoveActionPointer(new(row.Cover.Bounds.X + 10, row.Cover.Bounds.Y + 10));
            await WaitForLikedLayoutAsync(() => row.Overlay.Strength > .999);
            ClickHomeElement(row.PlaybackButton);
            await WaitForLikedLayoutAsync(() => playbackQueue.Current?.Track.Id == 101);
            if (page.Value != Page.Feed) throw new InvalidOperationException("Compact play button opened the track page.");
            ClickHomeElement(row.Title);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState?.Track.Id == 101);
            libraryLikes = new(transport.Recent, null); ShowLibraryTracks(); likesMode.Value = TrackViewMode.Cards; RefreshLikedViews();
            // Recycled tiles still have their old bounds until the new page is arranged.
            await WaitForLoginFrameAsync();
            await WaitForLikedLayoutAsync(() => likedTiles.Values.Any(tile => tile.Track?.Id == 101 && likedGrid.IsAncestorOf(tile.Root) && tile.Root.ActualWidth > 100));
            var tile = likedTiles.Values.First(tile => tile.Track?.Id == 101 && likedGrid.IsAncestorOf(tile.Root));
            playbackQueue.Clear(); MoveActionPointer(new(tile.Cover.Bounds.X + 10, tile.Cover.Bounds.Y + 10));
            await WaitForLikedLayoutAsync(() => tile.OverlayOpacity > .999);
            ClickHomeElement(tile.PlaybackButton);
            await WaitForLikedLayoutAsync(() => playbackQueue.Current?.Track.Id == 101);
            if (page.Value != Page.LibraryTracks) throw new InvalidOperationException("Liked tile play button opened the track page.");
            ClickHomeElement(tile.Title);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState?.Track.Id == 101);
            transport.RelatedReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var late = NavigateHomeAsync(force: true);
            await WaitForLikedLayoutAsync(() => homePending.Contains("recommendations"));
            api.Session = new("otherfixture", "fixture-token", "Test"); me = new() { Id = 43 }; ClearLibraryData();
            transport.RelatedReady.SetResult(); await late;
            if (homeContent != null || homeSession != null) throw new InvalidOperationException("Cleared account retained personal Home data.");
            Console.WriteLine("HOME_UI_OK: responsive orbit, hover drift, front-row navigation, cover playback, ranked genre charts, independent loading, responsive grids, shortcuts, cache/retry, account isolation and track links");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("HOME_UI_FAILED: " + error); }
        finally
        {
            if (OperatingSystem.IsWindows()) ProbeSetWindowPos(Window.Handle, -2, 0, 0, 0, 0, 0x13);
            player = originalPlayer; Window.Close();
        }
    }

    private Point FindHomeMixPoint(int index, bool play = false)
    {
        var bounds = homeMixCarousel.Bounds;
        bool Hits(double x, double y) => homeMixCarousel.CardAt(new Point(x, y), out var badge) == index && badge == play;
        var inset = play ? 4 : 10;
        for (var y = bounds.Y + inset; y < bounds.Bottom - inset; y += 4)
            for (var x = bounds.X + inset; x < bounds.Right - inset; x += 4)
                if (Hits(x, y) && Hits(x - inset, y - inset) && Hits(x + inset, y + inset))
                    return new Point(x, y);
        throw new InvalidOperationException($"Mix {index} has no visible {(play ? "Play" : "body")} hit area.");
    }

    private void RouteHomeCarouselDrag(Point from, Point to)
    {
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", throwOnError: true)!;
        var methods = router.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var button = methods.Single(method => method.Name == "MouseButton" && method.GetParameters().Length == 10);
        var move = methods.Single(method => method.Name == "MouseMove" && method.GetParameters().Length == 7);
        button.Invoke(null, [Window, from, new Point(0, 0), MouseButton.Left, true, true, false, false, 1, ModifierKeys.None]);
        move.Invoke(null, [Window, to, new Point(0, 0), true, false, false, ModifierKeys.None]);
        button.Invoke(null, [Window, to, new Point(0, 0), MouseButton.Left, false, false, false, false, 1, ModifierKeys.None]);
    }

    private void ClickHomeElement(FrameworkElement element) =>
        RouteWaveformClick(new(element.Bounds.X + element.ActualWidth / 2, element.Bounds.Y + element.ActualHeight / 2));

    private sealed class HomeFixtureTransport : ISoundCloudBrowserTransport
    {
        internal const string Artwork = "https://i1.sndcdn.com/home-fixture-t500x500.jpg";
        internal bool FailMixes;
        internal int HomeReads;
        internal TaskCompletionSource RelatedReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SoundCloudTrack[] Recent { get; } = Enumerable.Range(101, 6).Select(id => Track(id, id - 100,
            new[] { "Тихая гавань", "Там, где свет", "Волны", "После полуночи", "Новый день", "До весны" }[id - 101])).ToArray();
        internal SoundCloudTrack[] Chart { get; } = Enumerable.Range(1, 12)
            .Select(index => Track(600 + index, 30 + index, "Трек из чарта " + index)).ToArray();
        private static SoundCloudTrack Track(long id, long artist, string title) => new()
        { Id = id, Title = title, Duration = 180000, ArtworkUrl = Artwork, CreatedAt = DateTimeOffset.UtcNow.AddDays(-2).ToString("O"),
          User = new() { Id = artist, Username = "Исполнитель " + artist }, PermalinkUrl = "https://soundcloud.com/fixture/track-" + id };
        private static JsonDocument Tracks(IEnumerable<SoundCloudTrack> tracks) => JsonDocument.Parse("{\"collection\":[" +
            string.Join(',', tracks.Select(track => JsonSerializer.Serialize(track, SoundCloudJson.Default.SoundCloudTrack))) + "]}");
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var path = uri.AbsolutePath;
            if (path is "/me/play-history/contexts" or "/me/play-history/tracks" or "/users/42/likes" or "/me/track_likes/ids" or "/stream" or "/mixed-selections") HomeReads++;
            if (path is "/me/play-history/contexts" or "/me/play-history/tracks" or "/users/42/likes") return Tracks(Recent);
            if (path == "/me/track_likes/ids") return JsonDocument.Parse("[101,102,103,104,105,106]");
            if (path == "/users/42/followings/ids") return JsonDocument.Parse("[7,8,9]");
            if (path == "/stream") return Tracks(Enumerable.Range(301, 6).Select(id => Track(id, 7 + id % 3, "Новый релиз " + (id - 300))));
            if (path == "/mixed-selections")
            {
                if (FailMixes) throw new HttpRequestException("Fixture: mixes unavailable");
                static object Playlist(string urn, string title, string description) => new
                {
                    kind = "system-playlist", id = "soundcloud:system-playlists:" + urn, title,
                    short_description = description, artwork_url = Artwork, track_count = 30
                };
                var genres = new[] { "Hip Hop", "All Genres", "Electronic", "Trap", "Indie", "Dance", "Pop", "Rock", "Ambient", "Techno", "Jazz", "Metal" };
                return JsonSerializer.SerializeToDocument(new
                {
                    collection = new[]
                    {
                        new { items = Enumerable.Range(1, 7).Select(id => Playlist($"your-moods:42:{id}", $"Your Mix {id}", "Fixture artists")).ToArray() },
                        new { items = new[] { Playlist("new-for-you::test", "Daily Drops", "New releases"), Playlist("weekly::test", "Weekly Wave", "Weekly discoveries") } },
                        new { items = genres.Select((genre, index) => Playlist("trending-by-genre:" + (index == 1 ? "all-genres" : "genre-" + index), genre, "Trending")).ToArray() }
                    }
                });
            }
            if (path.EndsWith("/related"))
            {
                await RelatedReady.Task.WaitAsync(cancellationToken);
                var seed = long.Parse(path.Split('/')[2]);
                var start = seed < 200 ? 201 : 401 + (int)(seed % 3) * 100;
                return Tracks(Enumerable.Range(start, 18).Select(id => Track(id, 20 + (id - start) / 2, "Открытие " + (id - 200))));
            }
            if (path.StartsWith("/system-playlists/"))
            {
                var urn = Uri.UnescapeDataString(path["/system-playlists/".Length..]);
                var hipHop = urn.EndsWith(":genre-0");
                var general = urn.EndsWith(":all-genres");
                var entries = hipHop ? Chart.Reverse().ToArray() : general ? Chart : Recent;
                var mix = new SoundCloudPlaylist { Urn = urn, Title = hipHop ? "Hip Hop" : general ? "All Genres" : "Your Mix " + urn.Split(':')[^1],
                    Tracks = entries, TrackCount = entries.Length, ArtworkUrl = Artwork };
                return JsonDocument.Parse(JsonSerializer.Serialize(mix, SoundCloudJson.Default.SoundCloudPlaylist));
            }
            if (path.StartsWith("/tracks/"))
            {
                var id = long.Parse(path.Split('/')[2]);
                var track = Recent.FirstOrDefault(track => track.Id == id) ?? Track(id, 20 + (id - 201) / 2, "Открытие " + (id - 200));
                return JsonDocument.Parse(JsonSerializer.Serialize(track, SoundCloudJson.Default.SoundCloudTrack));
            }
            throw new InvalidOperationException("Unexpected Home fixture request: " + path);
        }
        public Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default) => Task.FromResult(JsonDocument.Parse(
            request.Kind == "sidebar" ? "{\"data\":{\"playlists\":[],\"albums\":[]}}" :
            "{\"data\":{\"trackComments\":{\"comments\":[],\"pageInfo\":{\"hasNextPage\":false}}}}"));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
