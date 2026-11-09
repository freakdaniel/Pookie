using System.Diagnostics;
using Aprillz.MewUI;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task ProbeLiveSessionAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
        var dispatcher = Application.Current.Dispatcher!;
        var phase = "startup";
        var queued = 0;
        var sampling = true;
        var samples = new List<(string Phase, string Kind, double Ms, long Frame, DateTime At)>();
        var heartbeat = Task.Run(async () =>
        {
            while (await timer.WaitForNextTickAsync().ConfigureAwait(false) && !token.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref queued, 1) != 0) continue;
                var posted = Stopwatch.GetTimestamp();
                dispatcher.BeginInvoke(() =>
                {
                    Volatile.Write(ref queued, 0);
                    if (!sampling) return;
                    samples.Add((phase, "dispatcher", Stopwatch.GetElapsedTime(posted).TotalMilliseconds,
                        Window.LastFramePerformanceStats.FrameIndex, DateTime.UtcNow));
                });
            }
        });
        void Frame()
        {
            var frame = Window.LastFramePerformanceStats;
            if (!frame.IsEmpty) samples.Add((phase, "render", frame.FrameMs, frame.FrameIndex, DateTime.UtcNow));
        }
        async Task Visit(string name, Func<Task> navigate, bool scroll = true)
        {
            phase = name;
            Console.WriteLine($"LIVE_SESSION_MARK {name} {DateTime.UtcNow:O}");
            await navigate().WaitAsync(token);
            await WaitForLoginFrameAsync();
            if (scroll) await ScrollLiveSessionAsync(token);
        }

        WindowPresentation.FramePresented(Window) += Frame;
        try
        {
            await Visit("home", () => NavigateHomeAsync());
            await Visit("feed", () => OpenFeedAsync());
            query.Value = "Daft Punk";
            await Visit("search-all", () => SearchAsync(SearchSection.All));
            await Visit("search-playlists", () => SearchAsync(SearchSection.Playlists));
            if (searchResults?.Items.FirstOrDefault(item => item.Playlist != null) is { } playlist)
                await Visit("playlist", () => OpenLibraryItemAsync(playlist));
            await Visit("likes", () => LikesAsync(showAll: true));
            var track = await api.GetTrackAsync(1966465007, token);
            await Visit("track-page", () => OpenTrackPageAsync(track), scroll: false);
            await Visit("drm-play", async () =>
            {
                SetQueue(track);
                var play = PlayAsync(track);
                await ScrollLiveSessionAsync(token);
                await play.WaitAsync(token);
                await WaitForTrackPageLiveAsync(() => audioReady && progress.Value > .1);
            }, scroll: false);
            await Visit("expanded", () =>
            {
                SetExpandedPlayer(true);
                return WaitForTrackPageLiveAsync(() => expandedHost.IsVisible && !expandedAnimation.IsRunning);
            }, scroll: false);
            SetExpandedPlayer(false);
            if (track.User is { } artist) await Visit("artist", () => OpenUserProfileAsync(artist));
            await Visit("chats-list", OpenChatsAsync, scroll: false);
            Console.WriteLine("LIVE_SESSION_OK");
        }
        finally
        {
            WindowPresentation.FramePresented(Window) -= Frame;
            sampling = false;
            timer.Dispose();
            await heartbeat;
            foreach (var group in samples.GroupBy(sample => (sample.Phase, sample.Kind)))
            {
                var ordered = group.OrderByDescending(sample => sample.Ms).ToArray();
                Console.WriteLine($"LIVE_SESSION_STATS {group.Key.Phase} {group.Key.Kind} n={ordered.Length} max={ordered[0].Ms:F1}ms over40={ordered.Count(sample => sample.Ms > 40)}");
                foreach (var sample in ordered.Take(4).Where(sample => sample.Ms >= 32))
                    Console.WriteLine($"LIVE_SESSION_SLOW {sample.Phase} {sample.Kind} ms={sample.Ms:F1} frame={sample.Frame} at={sample.At:O}");
            }
        }
    }

    private async Task ScrollLiveSessionAsync(CancellationToken token)
    {
        var point = Window.ClientToScreen(shellScroll.Bounds.Center);
        var coords = (nint)(((int)Math.Round(point.Y) << 16) | ((int)Math.Round(point.X) & 0xffff));
        var handle = Window.Handle;
        await Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
            for (var i = 0; i < 64; i++)
            {
                var delta = i < 40 ? -60 : 60;
                if (!PostLikedActionMessage(handle, 0x20a, unchecked((nuint)(uint)(delta << 16)), coords))
                    throw new InvalidOperationException("Could not post session wheel input.");
                await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
            }
        }, token);
    }
}
