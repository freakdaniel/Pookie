using Aprillz.MewUI;
using Pookie.App.Storage;
using Pookie.SoundCloud;
using System.Text.Json;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyScrollingUiAsync()
    {
        try
        {
            Window.WindowSize = WindowSize.Resizable(1000, 840, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => Window.ClientSize.Width < 1001 && libraryColumns == 4);
            var fixtures = Enumerable.Range(1, 80).Select(id => new SoundCloudTrack
            {
                Id = 90000 + id, Title = "Scrolling fixture " + id, Duration = 120000,
                ArtworkUrl = $"https://i1.sndcdn.com/scroll-fixture-{id}.jpg",
                WaveformUrl = $"https://wave.sndcdn.com/scroll-fixture-{id}.json"
            }).ToArray();
            // Exercise real disk reads, image decoding and waveform parsing while
            // scrolling, without using the account or relying on network timing.
            await Task.Run(async () =>
            {
                var image = ArtworkFixtureBitmap(500);
                var waveform = System.Text.Encoding.UTF8.GetBytes("{\"height\":140,\"samples\":[20,70,140,35,90,55]}");
                var cache = new WaveformDiskCache(dataPaths);
                foreach (var track in fixtures)
                {
                    await imageDiskCache.WriteAsync(track.ArtworkUrl!, image, lifetime.Token);
                    await cache.WriteAsync(track.WaveformUrl!, waveform, lifetime.Token);
                }
            }, lifetime.Token);
            var previousSession = api.Session; var previousTransport = api.BrowserTransport;
            try
            {
                api.Session = new("fixture", "fixture-token", "Test");
                ShowLibraryTracks();
                foreach (var listView in new[] { false, true })
                {
                    var transport = new PaginationBrowser(); api.BrowserTransport = transport;
                    likesAsList.Value = listView;
                    ResetPageScrolling();
                    ReplaceTracks(new(fixtures.Take(30).ToArray(), "https://api-v2.soundcloud.com/users/42/likes?cursor=scroll"));
                    await WaitForLikedLayoutAsync(() => NavigationScroll() is { ViewportHeight: > 100 } scroll &&
                        SmoothScroll.Maximum(scroll) > 1000 && smoothScrolls.ContainsKey(scroll));
                    var viewer = NavigationScroll()!;
                    viewer.SetScrollOffsets(0, SmoothScroll.Maximum(viewer)); CheckPageEnd();
                    await WaitForLikedLayoutAsync(() => transport.Calls == 1 && paginationLoading.Value);
                    viewer.SetScrollOffsets(0, SmoothScroll.Maximum(viewer) - 260);
                    var start = viewer.VerticalOffset; var target = start - 240;
                    var frames = 0;
                    void CountFrame() => frames++;
                    Window.FrameRendered += CountFrame;
                    try
                    {
                        smoothScrolls[viewer].Scroll(-240);
                        await WaitForLikedLayoutAsync(() => viewer.VerticalOffset < start - 1 && viewer.VerticalOffset > target + 1);
                        var response = new { collection = fixtures.Skip(30).Select(track => new { track = new {
                            id = track.Id, title = track.Title, duration = track.Duration,
                            artwork_url = track.ArtworkUrl, waveform_url = track.WaveformUrl } }).ToArray(), next_href = (string?)null };
                        transport.Reply.SetResult(JsonSerializer.Serialize(response));
                        await WaitForLikedLayoutAsync(() => tracks.Count == 80 && !paginationLoading.Value);
                        await WaitForLikedLayoutAsync(() => Math.Abs(viewer.VerticalOffset - target) < 1);
                        if (frames < 3 || transport.Calls != 1)
                            throw new InvalidOperationException("Pagination interrupted scrolling or issued overlapping requests.");
                        if (listView)
                            await WaitForLikedLayoutAsync(() => likedRows.Values.Any(row => row.Track?.Id > 90020 && row.Waveform.HasSamples));
                        await WaitForLikedLayoutAsync(() => libraryCoverCache.Count >= 20);
                        Console.WriteLine($"LIKES_SCROLL_UI_OK: {(listView ? "list" : "grid")}, 50-track append during {frames} rendered frames; cached 500px covers/waveforms, preserved scroll destination");
                    }
                    finally { Window.FrameRendered -= CountFrame; }
                }
            }
            finally { api.Session = previousSession; api.BrowserTransport = previousTransport; }
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("SCROLLING_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }
}
