using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPooledPageViewsAsync()
    {
        timer.Stop(); await ShowWorkspaceAsync();
        Window.WindowSize = WindowSize.Resizable(1160, 1000, minWidth: 1000, minHeight: 680);
        var user = new SoundCloudUser { Id = 77, Username = "Pooled publication fixture", TrackCount = 1000 };
        const string wave = "https://wave.sndcdn.com/pooled-page-fixture.json";
        var samples = Enumerable.Range(0, 400).Select(i => (float)(.2 + .7 * Math.Abs(Math.Sin(i * .27)))).ToArray();
        waveformRequests[wave] = Task.FromResult<float[]?>(samples);
        var tracks = Enumerable.Range(1, 1100).Select(id => new SoundCloudTrack
        { Id = id, Title = "Publication " + id, Duration = 120000, User = user, WaveformUrl = wave, CreatedAt = "2022-01-01T00:00:00Z" }).ToArray();
        var items = tracks.Take(1000).Select((track, i) => i % 5 != 4 ? LibraryItem.FromTrack(track) :
            LibraryData.FromPlaylist(new SoundCloudPlaylist { Id = 90000 + i, Title = "Playlist " + i, User = user,
                Tracks = tracks.Skip(i).Take(3).ToArray(), TrackCount = 3 })).ToArray();

        BeginNavigation(new(Page.Search, Search: "pool", Section: SearchSection.Tracks)); searchSection.Value = SearchSection.Tracks;
        SetSearchResults(new(tracks.Take(1000).Select(LibraryItem.FromTrack).ToArray(), null));
        await Probe("search", () => searchViews.Values.Select(view => view.LibraryTrack).OfType<LikedTrackRow>(), () => searchViews.Count);

        BeginNavigation(new(Page.UserProfile, Item: UserItem(user), UserSection: UserSection.Tracks));
        userSection.Value = UserSection.Tracks;
        userProfileState = new(user, new() { [UserSection.Tracks] = new(tracks.Take(1000).Select(LibraryItem.FromTrack).ToArray(), null) }, [], [], [], false);
        RenderUserProfile();
        await Probe("profile-tracks", () => likedRows.Values.Where(row => row.Root.FindVisualRoot() == Window && userTrackList.IsAncestorOf(row.Root)), () => likedRows.Count);

        BeginNavigation(new(Page.UserProfile, Item: UserItem(user)));
        userSection.Value = UserSection.Overview;
        userProfileState = new(user, new() { [UserSection.Overview] = new(items, null) }, [], [], [], false);
        RenderUserProfile();
        await Probe("profile-publications", () => userPublicationRows, () => userPublicationViews.Count);
        if (userPublicationViews.Count > 30 || userPublicationRows.Count > 20 || userPublicationList.ItemsSource.Count != 1000)
            throw new InvalidOperationException("Profile activity materialized its complete data source.");
        foreach (var view in userPublicationViews.Values.Where(view => view.Item?.Playlist != null))
            if (view.Preview.Count != 3 || view.Preview.Count(row => row.Root.IsVisible) != 3 || view.Cover.ActualWidth < 183)
                throw new InvalidOperationException("Virtualized profile playlist changed its cover or preview geometry.");
        shellScroll.SetScrollOffsets(0, 5000); await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
        var anchor = userPublications.Where(entry => entry.Root.Bounds.IntersectsWith(userOverviewScroll.Bounds)).OrderBy(entry => entry.Root.Bounds.Y).First();
        var y = anchor.Root.Bounds.Y;
        userProfileState = userProfileState with { Sections = new() { [UserSection.Overview] = new([.. items, .. tracks.Skip(1000).Select(LibraryItem.FromTrack)], null) } };
        RenderUserProfile(); await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
        var after = userPublications.Single(entry => entry.Item.Key == anchor.Item.Key);
        if (!ReferenceEquals(anchor.Root, after.Root) || Math.Abs(after.Root.Bounds.Y - y) > 2 / Window.DpiScale)
            throw new InvalidOperationException("Appending profile publications replaced or moved the visible anchor.");

        var previousSession = api.Session; var previousTransport = api.BrowserTransport;
        var delayed = new TaskCompletionSource<SoundCloudPlaylist>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FeedFixture();
        var missing = new SoundCloudPlaylist { Id = 90701, Title = "Delayed preview", User = user, TrackCount = 3, Tracks = [] };
        transport.Details[missing.Id] = delayed.Task;
        api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = transport;
        try
        {
            shellScroll.SetScrollOffsets(0, 0);
            userProfileState = userProfileState with { Sections = new() { [UserSection.Overview] = new([LibraryData.FromPlaylist(missing), .. items], null) } };
            RenderUserProfile();
            try { await WaitForLikedLayoutAsync(() => transport.DetailReads > 0 && userPublicationViews.Values.Any(view => view.Item?.Playlist?.Id == missing.Id)); }
            catch (Exception error)
            { throw new InvalidOperationException($"Delayed playlist did not bind: requests={transport.DetailReads}, outer={shellScroll.VerticalOffset}, inner={userOverviewScroll.VerticalOffset}, hostReady={userPublicationHost.IsReady}, bound={string.Join(',', userPublicationViews.Values.Select(view => view.Item?.Key))}.", error); }
            var slot = userPublicationViews.Values.First(view => view.Item?.Playlist?.Id == missing.Id);
            shellScroll.SetScrollOffsets(0, 80000); await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
            delayed.SetResult(missing with { Tracks = [tracks[0] with { Id = 999999, Title = "Stale preview" }] });
            await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
            if (slot.Item?.Playlist?.Id == missing.Id || slot.Preview.Any(row => row.Track?.Id == 999999))
                throw new InvalidOperationException("A recycled publication accepted its previous playlist's delayed preview.");
        }
        finally { api.Session = previousSession; api.BrowserTransport = previousTransport; }

        BeginNavigation(new(Page.Track, Item: LibraryItem.FromTrack(tracks[0])));
        trackDetailState = new(tracks[0], new([], null), new([], null), false, false)
        { MetadataLoading = false, SidebarLoading = false, Sections = new Dictionary<TrackSection, LibraryPage>
            { [TrackSection.Related] = new(tracks.Take(1000).Select(LibraryItem.FromTrack).ToArray(), null),
                [TrackSection.Playlists] = new(items.Where(item => item.Playlist != null).ToArray(), null) } };
        trackDetailSection.Value = TrackSection.Related; RenderTrackSection();
        await Probe("track-related", () => trackSectionRows, () => trackSectionRowPool.Count);
        if (trackSectionRowPool.Count > 20) throw new InvalidOperationException("Related tracks lost bounded virtualization.");
        trackDetailSection.Value = TrackSection.Playlists; RenderTrackSection();
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && BoundTrackSectionCards.Any(card => card.Root.ActualWidth > 100));
        foreach (var width in new[] { 1000, 1440, 1160 })
        {
            Window.WindowSize = WindowSize.Resizable(width, 1000, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && Math.Abs(Window.ClientSize.Width - width) < 1);
            foreach (var offset in new[] { 0d, 5000d, 12000d, 0d })
            {
                shellScroll.SetScrollOffsets(0, offset); await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
                var cards = BoundTrackSectionCards.ToArray();
                if (cards.Length == 0 || cards.Length > 48 || cards.Any(card => Math.Abs(card.Cover.ActualWidth - trackSectionCardWidth) > 1 / Window.DpiScale))
                    throw new InvalidOperationException("Track collection virtualization changed the responsive grid or realized all cards.");
            }
        }
        if (Environment.GetEnvironmentVariable("POOKIE_UI_POOL_PERF") == "1")
            await ProbePooledPagePerformanceAsync(user, tracks, items);
        Console.WriteLine("UI_PAGE_POOLS_OK: 1000-result search/profile tracks, mixed profile publications, stable append anchor, retained waveforms, bounded related rows and responsive playlist grids");

        async Task Probe(string name, Func<IEnumerable<LikedTrackRow>> rows, Func<int> poolSize)
        {
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) > 20000);
            var raster = typeof(TrackWaveform).GetField("raster", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var buffers = new Dictionary<TrackWaveform, WriteableBitmap>(); var ids = new HashSet<long>();
            foreach (var offset in new[] { 0d, 5000d, 50000d, 120000d, 5000d, 0d })
            {
                shellScroll.SetScrollOffsets(0, offset); await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
                foreach (var row in rows().Where(row => row.Track != null && row.Root.FindVisualRoot() == Window))
                {
                    ids.Add(row.Track!.Id);
                    if (!row.Waveform.RetainRasterOnDetach) throw new InvalidOperationException(name + " did not opt into pooled render storage.");
                    if (Math.Abs(row.Cover.ActualWidth - TrackRowLayout.CoverSize) > 1 / Window.DpiScale ||
                        Math.Abs(row.Root.ActualHeight - TrackRowLayout.Height) > 1 / Window.DpiScale)
                        throw new InvalidOperationException(name + " changed shared cover/row dimensions.");
                    if (raster.GetValue(row.Waveform) is not WriteableBitmap bitmap) continue;
                    if (buffers.TryGetValue(row.Waveform, out var old) && old.PixelWidth == bitmap.PixelWidth && old.PixelHeight == bitmap.PixelHeight && !ReferenceEquals(old, bitmap))
                        throw new InvalidOperationException(name + " recreated a same-size pooled waveform bitmap.");
                    buffers[row.Waveform] = bitmap;
                }
            }
            if (ids.Count < 12 || poolSize() > 100) throw new InvalidOperationException(name + " did not traverse a bounded control pool.");
            Console.WriteLine($"UI_PAGE_POOL_OK {name}: tracks={ids.Count}, buffers={buffers.Count}, slots={poolSize()}");
        }
    }
}
