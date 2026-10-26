using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyFeedAsync()
    {
        try
        {
            timer.Stop();
            var fixture = new FeedFixture(); api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = fixture;
            me = new() { Id = 42, Username = "Мой профиль" }; signedIn.Value = true;
            var art = TrackArtFixture();
            collectionImages["https://i1.sndcdn.com/feed-playlist.jpg"] = ImageSource.FromBytes(ArtworkFixtureBitmap(184));
            foreach (var track in fixture.Tracks)
            {
                libraryCoverCache[track.Id] = ImageSource.FromBytes(ArtworkFixtureBitmap(184));
                if (track.WaveformUrl != null) waveformRequests[track.WaveformUrl] = Task.FromResult<float[]?>(Enumerable.Range(0, 240).Select(i => (float)(.3 + .6 * Math.Abs(Math.Sin(i * .3)))).ToArray());
                if (track.TrackArtUrl != null) trackArtRequests[track.TrackArtUrl] = Task.FromResult<ImageSource?>(art);
            }
            localRecent.Clear(); localRecent.Add(LibraryItem.FromTrack(fixture.Tracks[1]));
            Window.WindowSize = WindowSize.Resizable(1440, 900, minWidth: 1000, minHeight: 680);
            await ShowWorkspaceAsync();
            await OpenFeedAsync();
            await WaitForLikedLayoutAsync(() => feedState?.Entries.Length == 30 && feedViews.Values.Any(view => view.Track?.Cover.ActualWidth > 100) && shellPageScroll.IsReady);
            if (feedViews.Count > 15 || !feedSidebar.IsVisible || !feedViews.Values.Any(view => view.Entry.IsRepost) || feedSidebarRows.Count != 1)
                throw new InvalidOperationException("Feed did not virtualize publications or show the responsive sidebar.");
            var recentSection = feedSidebar.Children.OfType<StackPanel>().Last();
            var recentHeader = recentSection.Children.OfType<Button>().First();
            var recentRow = feedSidebarRows.Single();
            if (Math.Abs(recentHeader.Bounds.X - recentRow.Cover.Bounds.X) > 1 || recentRow.Root.Bounds.X >= recentHeader.Bounds.X ||
                recentHeader.FindVisualChild<Image>() is not Image { Opacity: < .01 } || feedFilters.Bounds.Y < ShellChromeGlass.HeaderHeight)
                throw new InvalidOperationException("Feed history alignment/hover heading diverged from the shared section style");
            var first = feedViews.Values.First(view => view.Track?.Track?.TrackArtUrl != null).Track!;
            TextBlock? counter = null;
            VisualTree.Visit(first.Root, element => { if (element is TextBlock { Text: "944" } text) counter = text; });
            await WaitForLikedLayoutAsync(() => counter is { IsVisible: true, ActualWidth: > 0 });
            await WaitForLoginFrameAsync(); CaptureUiPreview("feed-wide");
            if (Math.Abs(first.Cover.ActualWidth - TrackRowLayout.CoverSize) > 1 / Window.DpiScale ||
                Math.Abs(first.Root.ActualHeight - TrackRowLayout.Height) > 1 / Window.DpiScale || first.TrackArt.Source != art)
                throw new InvalidOperationException("Feed did not reuse the shared artwork/waveform geometry or custom track art.");
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) > 800);
            shellScroll.SetScrollOffsets(0, 800);
            await WaitForLikedLayoutAsync(() => shellScroll.VerticalOffset > 799 && shellPageScroll.IsReady && feedViews.Values.Any(view => view.Entry.Item.Playlist != null && view.Preview.Count == 3));
            var playlistView = feedViews.Values.First(view => view.Entry.Item.Playlist != null);
            if (playlistView.Preview.Any(row => row.Root.Height != 24) ||
                Math.Abs(playlistView.Covers[0].ActualHeight - TrackRowLayout.CoverSize) > 1 / Window.DpiScale)
                throw new InvalidOperationException("Playlist preview did not fit the shared 184px publication geometry.");
            await WaitForLoginFrameAsync();
            var pinnedTop = shellScroll.Bounds.Y + ShellChromeGlass.HeaderHeight + 16;
            try { await WaitForLikedLayoutAsync(() => Math.Abs(feedSidebar.Bounds.Y - pinnedTop) <= 1); }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Feed sidebar did not pin below the chrome: side={feedSidebar.Bounds}, headerBottom={pinnedTop}, columns={feedColumns.Bounds}, shell={shellPageScroll.Bounds}, content={contentFrame.Bounds}, inner={PageContentScroll()?.Bounds}, scroll={shellScroll.VerticalOffset}/{SmoothScroll.Maximum(shellScroll)}.", error);
            }
            CaptureUiPreview("feed-playlist");
            shellScroll.SetScrollOffsets(0, 0);
            SetFeedReposts(false);
            await WaitForLikedLayoutAsync(() => feedViews.Count > 0 && feedViews.Values.All(view => !view.Entry.IsRepost));
            if (VisibleFeedEntries.Length != 15) throw new InvalidOperationException("Repost filter removed originals or retained reposts.");
            await PlayFeedTrackAsync(fixture.Tracks[1]);
            if (playbackQueue.Context?.Key != "feed:originals" || playbackQueue.Snapshot.Source.Any(entry => entry.Track.Id % 2 == 0))
                throw new InvalidOperationException("Feed playback included filtered reposts.");
            await queueLoader.EnsureAheadAsync(force: true);
            if (playbackQueue.Context?.NextHref != null || playbackQueue.Snapshot.Source.Count != 19 ||
                playbackQueue.Snapshot.Source.Any(entry => entry.Track.Id % 2 == 0))
                throw new InvalidOperationException("Queue pagination lost the feed's repost filter.");
            SetFeedReposts(true);
            await LoadNextVisiblePageAsync();
            if (feedState!.Entries.Length != 40 || feedState.Cursor != null) throw new InvalidOperationException("Feed pagination lost order, duplicated events or retained the final cursor.");
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
            shellScroll.SetScrollOffsets(0, 600);
            await WaitForLikedLayoutAsync(() => shellScroll.VerticalOffset > 500 && !feedList.IsArrangeDirty);
            var saved = shellScroll.VerticalOffset;
            await NavigateRouteAsync(new(Page.Settings), _ => Task.CompletedTask);
            await MoveNavigationAsync(-1);
            await WaitForLikedLayoutAsync(() => pendingNavigationScroll == null && Math.Abs(shellScroll.VerticalOffset - saved) < 2);
            Window.WindowSize = WindowSize.Resizable(1000, 760, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => !feedSidebar.IsVisible && feedList.ActualWidth > 850 && shellPageScroll.IsReady);
            shellScroll.SetScrollOffsets(0, 0);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && shellScroll.VerticalOffset < .5 && feedFilters.Bounds.Y > ShellChromeGlass.HeaderHeight);
            await WaitForLoginFrameAsync(); CaptureUiPreview("feed-narrow");
            // No new page can overwrite a route selected while a refresh is pending.
            fixture.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var retained = feedState;
            var refresh = OpenFeedAsync(true);
            await WaitForLikedLayoutAsync(() => feedLoading);
            await NavigateRouteAsync(new(Page.Settings), _ => Task.CompletedTask);
            fixture.Hold.SetResult(); await refresh;
            if (page.Value != Page.Settings || feedState != retained) throw new InvalidOperationException("A late feed response replaced a newer route.");
            fixture.Hold = null; await OpenFeedAsync();
            fixture.Fail = true;
            try { await OpenFeedAsync(true); throw new InvalidOperationException("Refresh error was not reported"); }
            catch (SoundCloudException) { }
            if (feedState != retained || !feedNotice.IsVisible || !feedRetry.IsVisible) throw new InvalidOperationException("Failed refresh discarded the previous feed or omitted retry.");
            fixture.Fail = false; await OpenFeedAsync(true);
            SetFeedReposts(false); feedState = feedState! with { Entries = feedState.Entries.Where(entry => entry.IsRepost).ToArray(), Cursor = null }; RenderFeed();
            if (!feedEmpty.IsVisible) throw new InvalidOperationException("Filtered empty feed has no explanation.");
            Console.WriteLine("FEED_UI_OK: virtualized shared track/playlist publications, actor/repost/date, custom art, sidebar, responsive widths, filtering, pagination/dedup, history scroll and stale response isolation");
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; CaptureUiPreview("feed-failure"); Console.Error.WriteLine("FEED_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private sealed class FeedFixture : ISoundCloudBrowserTransport
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        internal TaskCompletionSource? Hold;
        internal bool Fail;
        internal SoundCloudTrack[] Tracks = Enumerable.Range(0, 40).Select(i => new SoundCloudTrack
        {
            Id = 64000 + i, Title = i % 2 == 0 ? "Всё ещё звучит — " + (i + 1) : "Где-то между ночью и утром", Duration = 174000,
            User = new() { Id = 100 + i % 4, Username = new[] { "whitek3d", "Cold Carti", "T-Fest", "kurokovvv" }[i % 4] },
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-i - 1).ToString("O"), WaveformUrl = $"https://wave.sndcdn.com/feed-fixture-{i}.json",
            Visuals = i == 0 ? new(true, [new("https://i1.sndcdn.com/feed-fixture-art.jpg")]) : null,
            LikesCount = 944 + i, RepostsCount = 7, PlaybackCount = 13520, CommentCount = 31, PermalinkUrl = "https://soundcloud.com/artist/feed-" + i
        }).ToArray();
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken token = default)
        {
            if (uri.AbsolutePath == "/stream")
            {
                if (Fail) throw new SoundCloudException("Не удалось обновить ленту");
                if (Hold != null) await Hold.Task;
                var next = uri.Query.Contains("cursor=next");
                return JsonSerializer.SerializeToDocument(new { collection = Tracks.Skip(next ? 29 : 0).Take(next ? 11 : 30).Select((track, i) => new
                { type = track.Id % 2 == 0 ? "track-repost" : track.Id == 64003 ? "playlist" : "track", created_at = track.CreatedAt,
                    user = track.Id % 2 == 0 ? new SoundCloudUser { Id = 99, Username = "ПОШЛАЯ МОЛЛИ" } : track.User,
                    origin = track.Id == 64003 ? (object)new SoundCloudPlaylist { Id = 900, Title = "Ночной маршрут", TrackCount = 3,
                        ArtworkUrl = "https://i1.sndcdn.com/feed-playlist.jpg", User = track.User, Tracks = Tracks.Take(3).ToArray() } : track }),
                    next_href = next ? null : "https://api-v2.soundcloud.com/stream?cursor=next" }, Json);
            }
            if (uri.AbsolutePath is "/me/playlist_likes/ids" or "/me/playlist_reposts/ids") return JsonDocument.Parse("[]");
            throw new InvalidOperationException("Unexpected feed fixture route: " + uri.AbsolutePath);
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken token = default) => Task.CompletedTask;
    }
}
