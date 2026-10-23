using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task ProbeDetailScrollingAsync()
    {
        if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_PLAYING") != "1")
        { await ProbeDetailScrollingCoreAsync(); return; }
        var original = player;
        var fixture = new BufferFixturePlayer();
        var watch = Stopwatch.StartNew();
        void Advance() => fixture.State = fixture.State with { Position = 20 + watch.Elapsed.TotalSeconds };
        timer.Stop(); player = fixture;
        try
        {
            var track = trackDetailState!.Track;
            SetQueue(track); await PlayAsync(track);
            fixture.State = new(20, track.DurationSeconds, true, false, false) { BufferedEnd = track.DurationSeconds };
            Window.FrameRendered += Advance;
            timer.Start(); Poll();
            Console.WriteLine("DETAIL_SCROLL_PLAYING: real Poll/progress animation with advancing fixture position");
            if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_SETTLED") == "1")
            {
                await WaitForLikedLayoutAsync(() => artwork.Source is ImageSource && !playerBackdrop.Running &&
                    !playbackLoading.Value && Math.Abs(playerChrome.ActualHeight - PlayerBarHeight) < 1 / Window.DpiScale);
                Console.WriteLine("DETAIL_SCROLL_SETTLED: initial player height/artwork/palette transitions finished");
            }
            await ProbeDetailScrollingCoreAsync();
        }
        finally { timer.Stop(); Window.FrameRendered -= Advance; player = original; }
    }

    private async Task ProbeDetailScrollingCoreAsync()
    {
        var comments = Enumerable.Range(0, 500).Select(i => new SoundCloudComment
        {
            Id = 10000 + i, User = new() { Id = i + 10, Username = "Слушатель " + i },
            Body = string.Concat(Enumerable.Repeat("Комментарий с переносами строк и длинным текстом. ", 1 + i % 8)),
            Timestamp = i * 100, CreatedAt = "2026-10-20T12:00:00Z",
            Replies = i % 4 == 0 ? [new() { Id = 20000 + i, Body = "Ответ на комментарий", User = new() { Username = "Автор ответа" } }] : []
        }).ToArray();
        trackDetailState = trackDetailState! with { Comments = new(comments, null), CommentsLoading = false };
        Console.WriteLine("DETAIL_SCROLL_SETUP comments=500");
        RenderTrackDetailComments();
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
        if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_CONTINUOUS_ONLY") != "1") await ProbeScrollPathAsync("track-comments", 240);
        if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_TOUCHPAD") == "1") await ProbeNativeTouchpadScrollingAsync("track-500");
        else await ProbeContinuousScrollAsync("track-500");
        // Appending a page must preserve the viewport and allow both directions.
        var offset = shellScroll.VerticalOffset;
        trackDetailState = trackDetailState with { Comments = new(comments.Concat(comments.Take(50).Select(c => c with { Id = c.Id + 40000 })).ToArray(), null) };
        RenderTrackDetailComments();
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
        if (Math.Abs(shellScroll.VerticalOffset - offset) > 1) throw new InvalidOperationException("Appending comments moved the viewport.");
        if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_CONTINUOUS_ONLY") != "1") await ProbeScrollPathAsync("track-appended", 120);

        var transport = new CollectionFixtureTransport();
        api.BrowserTransport = transport; me = new() { Id = 42, Username = "Scroll fixture" }; signedIn.Value = true;
        await OpenLibraryItemAsync(LibraryData.FromPlaylist(transport.Playlist));
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && playlistCollectionVisible.Value);
        var fixture = transport.Playlist.Tracks[0];
        var playlistTracks = Enumerable.Range(0, 1000).Select(i => fixture with { Id = 70000 + i, Title = "Трек " + i }).ToArray();
        activeCollection = new(playlistTracks.Select(LibraryItem.FromTrack).ToArray(), null);
        RefreshCollectionTracks();
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
        if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_CONTINUOUS_ONLY") != "1") await ProbeScrollPathAsync("playlist-1000", 240);
        if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_TOUCHPAD") == "1") await ProbeNativeTouchpadScrollingAsync("playlist-1000");
        else await ProbeContinuousScrollAsync("playlist-1000");
        Console.WriteLine("DETAIL_SCROLL_OK: comments, append, hero reentry and playlist");
    }

    private async Task ProbeNativeTouchpadScrollingAsync(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native touchpad probe requires Windows.");
        LikedActionPoint saved = default;
        var restoreCursor = GetLikedActionCursor(out saved);
        var indicator = new PresentationProbe().Width(20).Height(20).Top().Right().Margin(0, 74, 8, 0).Row(0).RowSpan(3).IsHitTestVisible(false);
        workspace.Add(indicator);
        var frames = 0;
        var times = new List<double>();
        var maximumMovement = 0d;
        try
        {
            ProbeShowWindow(Window.Handle, 5);
            if (!ProbeSetWindowPos(Window.Handle, -1, 0, 0, 0, 0, 0x53)) throw new InvalidOperationException("Cannot expose touchpad probe.");
            Window.Activate();
            await WaitForLikedLayoutAsync(() => Window.IsActive && shellPageScroll.IsReady && indicator.ActualWidth > 10);
            var maximum = SmoothScroll.Maximum(shellScroll);
            var point = new Point(Window.ClientSize.Width * .5, Math.Min(500, shellScroll.Bounds.Bottom - 40));
            MoveLikedActionCursor(point);
            var screen = Window.ClientToScreen(point);
            var coordinates = (nint)(((int)Math.Round(screen.Y) << 16) | ((int)Math.Round(screen.X) & 0xffff));
            async Task PresentedFrame(int wheelDelta)
            {
                var color = frames++ % 2 == 0 ? Color.FromRgb(43, 183, 71) : Color.FromRgb(213, 57, 149);
                var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Frame() => rendered.TrySetResult();
                Window.FrameRendered += Frame;
                var watch = Stopwatch.StartNew();
                try
                {
                    indicator.FrameColor = color; indicator.InvalidateVisual();
                    for (var burst = 0; burst < 4 && wheelDelta != 0; burst++)
                        if (!PostLikedActionMessage(Window.Handle, 0x20A, unchecked((nuint)(uint)(wheelDelta << 16)), coordinates))
                            throw new InvalidOperationException("Cannot post precision wheel input.");
                    await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                }
                finally { Window.FrameRendered -= Frame; }
                var pixel = Window.ClientToScreen(new Point(indicator.Bounds.X + 10, indicator.Bounds.Y + 10));
                uint Read()
                {
                    if (ProbeDwmFlush() < 0) throw new InvalidOperationException("DWM presentation failed during touchpad scrolling.");
                    var dc = ProbeGetDC(0);
                    try { return ProbeGetPixel(dc, (int)Math.Round(pixel.X), (int)Math.Round(pixel.Y)); }
                    finally { ProbeReleaseDC(0, dc); }
                }
                var expected = (uint)(color.R | color.G << 8 | color.B << 16);
                // Desktop color management can change channel values. Opposite
                // red/green states must still alternate on the presented window;
                // a stale frame cannot pass the next state, even after a reset.
                bool Correct(uint value)
                {
                    var red = (int)(value & 255); var green = (int)((value >> 8) & 255);
                    return value != uint.MaxValue && (color.G > color.R ? green > red + 40 : red > green + 40);
                }
                var actual = Read();
                // FrameRendered can precede the top-level present. Keep this
                // state unchanged until it reaches DWM, with a bounded deadline.
                var presenting = Stopwatch.StartNew();
                while (!Correct(actual) && presenting.Elapsed < TimeSpan.FromMilliseconds(500))
                { await WaitForLoginFrameAsync(); actual = Read(); }
                if (!Correct(actual))
                {
                    CaptureUiPreview("touchpad-presentation-failure");
                    var pixelWindow = ProbeWindowFromPoint(new LikedActionPoint { X = (int)pixel.X, Y = (int)pixel.Y });
                    var windowClass = new System.Text.StringBuilder(256);
                    ProbePresentationWindowClass(pixelWindow, windowClass, windowClass.Capacity);
                    ProbePresentationWindowPid(pixelWindow, out var ownerPid);
                    throw new InvalidOperationException($"Presentation stopped or was occluded: page={name}, frame={frames}, actual={actual:X8}, expected={expected:X8}, renders={indicator.RenderCount}, bounds={indicator.Bounds}, pixel={pixel}, window={Window.Handle}, pixelWindow={pixelWindow}, class={windowClass}, pid={ownerPid}, active={Window.IsActive}.");
                }
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            foreach (var start in new[] { 0d, maximum / 2, Math.Max(0, maximum - 2500) })
            {
                var contentFrames = new HashSet<ulong>();
                smoothScrolls[shellScroll].Stop(); shellScroll.SetScrollOffsets(0, start);
                await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
                for (var step = 0; step < 180; step++)
                {
                    await PresentedFrame(step < 90 ? -18 : 18);
                    maximumMovement = Math.Max(maximumMovement, Math.Abs(shellScroll.VerticalOffset - start));
                    // GetPixel on the desktop synchronizes with DWM. Sample
                    // page contents periodically so the probe still generates
                    // frequent wheel bursts instead of spending each frame in
                    // dozens of separate desktop readbacks.
                    if (step % 12 != 0) continue;
                    var dc = ProbeGetDC(0);
                    try
                    {
                        var hash = 14695981039346656037UL;
                        for (var y = 0; y < 8; y++)
                        for (var x = 0; x < 6; x++)
                        {
                            var sample = Window.ClientToScreen(new Point(100 + x * 80,
                                140 + y * Math.Max(20, (shellScroll.Bounds.Bottom - 200) / 8)));
                            hash = unchecked((hash ^ ProbeGetPixel(dc, (int)sample.X, (int)sample.Y)) * 1099511628211UL);
                        }
                        contentFrames.Add(hash);
                    }
                    finally { ProbeReleaseDC(0, dc); }
                }
                if (contentFrames.Count < 5) throw new InvalidOperationException($"The page body did not repaint while scrolling: {name}, region={start}, distinct desktop samples={contentFrames.Count}.");
            }
            if (maximumMovement < 200) throw new InvalidOperationException("Native precision wheel messages did not scroll the page.");
            if (page.Value == Page.Track)
            {
                smoothScrolls[shellScroll].Stop(); shellScroll.SetScrollOffsets(0, 0);
                await WaitForLoginFrameAsync();
                await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && trackCommentMarkers.Count > 0 &&
                    trackCommentMarkers[^1].Button.Bounds.Y > ShellChromeGlass.HeaderHeight);
                // The clustered timed avatars overlap: the last marker is the
                // topmost hit target, so hovering the first can hit a later one.
                var marker = trackCommentMarkers[^1].Button;
                var hover = new Point(marker.Bounds.X + 12, marker.Bounds.Y + 12);
                MoveLikedActionCursor(hover); SendLikedActionMouse(0x200, 0, hover);
                await WaitForLikedLayoutAsync(() => marker.IsMouseOver);
                var tooltipWatch = Stopwatch.StartNew();
                while (tooltipWatch.Elapsed < TimeSpan.FromSeconds(1.2)) await PresentedFrame(0);
            }
            times.Sort();
            Console.WriteLine($"NATIVE_TOUCHPAD_OK {name}: wheel_messages=2160 presented_frames={frames} movement={maximumMovement:F0} p95={times[(int)(times.Count * .95)]:F1}ms max={times[^1]:F1}ms; every frame verified on desktop pixels, including comment hover");
        }
        finally
        {
            smoothScrolls[shellScroll].Stop(); workspace.Remove(indicator);
            ProbeSetWindowPos(Window.Handle, -2, 0, 0, 0, 0, 0x13);
            if (restoreCursor && !SetLikedActionCursor(saved.X, saved.Y)) throw new InvalidOperationException("Cannot restore pointer after touchpad probe.");
        }
    }

    private sealed class PresentationProbe : Control
    {
        internal Color FrameColor;
        internal int RenderCount;
        protected override void OnRender(IGraphicsContext context) { RenderCount++; context.FillRectangle(Bounds, FrameColor); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int ProbePresentationWindowClass(nint window, System.Text.StringBuilder text, int length);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint ProbePresentationWindowPid(nint window, out uint pid);

    private async Task ProbeContinuousScrollAsync(string name)
    {
        var maximum = SmoothScroll.Maximum(shellScroll);
        var times = new List<double>();
        foreach (var start in new[] { 100d, maximum / 2, Math.Max(0, maximum - 1600) })
        {
            shellScroll.SetScrollOffsets(0, start);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
            await WaitForLoginFrameAsync();
            for (var i = 0; i < 180; i++)
            {
                var step = i < 90 ? i : 179 - i;
                var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Frame() => rendered.TrySetResult();
                Window.FrameRendered += Frame;
                var watch = Stopwatch.StartNew();
                try
                {
                    var before = shellScroll.VerticalOffset;
                    shellScroll.SetScrollOffsets(0, start + step * 12);
                    if (shellScroll.VerticalOffset == before) Window.InvalidateVisual();
                    await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                }
                finally { Window.FrameRendered -= Frame; }
                times.Add(watch.Elapsed.TotalMilliseconds);
                if (times[^1] >= 43)
                {
                    var stats = Window.LastFramePerformanceStats;
                    Console.WriteLine($"SCROLL_SLOW {name} start={start:F0} step={i} time={times[^1]:F1} frame={stats.FrameMs:F1} layout={stats.LayoutMs:F1} draw={stats.RenderBodyMs:F1} present={stats.PresentMs:F1} calls={stats.DrawCalls} bytes={stats.AllocatedBytes}");
                }
                if (shellScroll.ViewportHeight < 600) throw new InvalidOperationException($"Scroll probe viewport collapsed: window={Window.ClientSize} workspace={workspace.Bounds} shell={shellScroll.Bounds} player={playerChrome.Bounds} content={contentSurface.Bounds}.");
            }
        }
        times.Sort();
        Console.WriteLine($"CONTINUOUS_SCROLL_RESULT {name} mean={times.Average():F1}ms p95={times[(int)(times.Count * .95)]:F1}ms max={times[^1]:F1}ms frames={times.Count} over43={times.Count(t => t >= 43)}");
    }

    private async Task ProbeScrollPathAsync(string name, int steps)
    {
        var times = new List<double>();
        var maximum = SmoothScroll.Maximum(shellScroll);
        Console.WriteLine($"DETAIL_SCROLL_START {name} max={maximum:F1} viewport={shellScroll.ViewportHeight:F1}");
        for (var i = 0; i < steps; i++)
        {
            // Fine scroll through the hero, deep comments, then reverse to the top.
            var phase = i / (steps / 4); var step = i % (steps / 4);
            var fraction = step / (double)(steps / 4 - 1);
            var offset = phase switch { 0 => fraction * 1100, 1 => 1100 + fraction * (maximum - 1100), 2 => maximum * (1 - fraction), _ => 1100 * (1 - fraction) };
            var trace = Environment.GetEnvironmentVariable("POOKIE_SCROLL_TRACE") == "1";
            if (trace) Console.WriteLine($"SCROLL_BEGIN {name} i={i} offset={offset:F1}");
            var watch = Stopwatch.StartNew();
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Frame() => rendered.TrySetResult();
            Window.FrameRendered += Frame;
            try
            {
                var previous = shellScroll.VerticalOffset;
                shellScroll.SetScrollOffsets(0, Math.Clamp(offset, 0, maximum));
                if (shellScroll.VerticalOffset == previous) Window.InvalidateVisual();
                await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
            }
            finally { Window.FrameRendered -= Frame; }
            times.Add(watch.Elapsed.TotalMilliseconds);
            if (trace || times[^1] > 100) Console.WriteLine($"SCROLL_END {name} i={i} time={times[^1]:F1}ms gc={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} memory={GC.GetTotalMemory(false)}");
            if (shellScroll.ViewportHeight < 600) throw new InvalidOperationException($"Scroll probe viewport collapsed: window={Window.ClientSize} workspace={workspace.Bounds} shell={shellScroll.Bounds} player={playerChrome.Bounds} content={contentSurface.Bounds}.");
            if (i % 30 == 0) Console.WriteLine($"DETAIL_SCROLL_FRAME {name} i={i} time={times[^1]:F1} offset={shellScroll.VerticalOffset:F1}");
        }
        times.Sort();
        Console.WriteLine($"DETAIL_SCROLL_RESULT {name} mean={times.Average():F1}ms p95={times[(int)(times.Count * .95)]:F1}ms max={times[^1]:F1}ms frames={times.Count}");
    }

}
