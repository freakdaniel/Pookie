using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task ProbeLibraryScrollPerformanceAsync(SoundCloudTrack[] fixtures)
    {
        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive);
        // Eliminate network/decode timing while exercising real GPU uploads,
        // virtualization, waveform appearance/hover and artwork glass.
        foreach (var track in fixtures)
        {
            libraryCoverCache[track.Id] = await Task.Run(() => PrepareImage(ArtworkFixtureBitmap(500), 500), lifetime.Token);
            waveformRequests[track.WaveformUrl!] = Task.FromResult<float[]?>(Enumerable.Range(0, 1000)
                .Select(i => (float)(.2 + .7 * Math.Abs(Math.Sin(i * .077)))).ToArray());
        }
        const string artUrl = "https://i1.sndcdn.com/scroll-art-original.jpg";
        trackArtRequests[artUrl] = Task.FromResult<ImageSource?>(TrackArtFixture());
        var withArt = fixtures.Select(t => t with { Visuals = new(true, [new(artUrl)]) }).ToArray();
        foreach (var mode in new[] { TrackViewMode.Cards, TrackViewMode.Waveform })
        {
            likesMode.Value = mode;
            ReplaceTracks(new(fixtures, null));
            await MeasureScrollCaseAsync("library-" + mode);
        }
        likesMode.Value = TrackViewMode.Waveform;
        ReplaceTracks(new(withArt, null));
        await MeasureScrollCaseAsync("library-Waveform-art");
        var transport = new CollectionFixtureTransport();
        api.BrowserTransport = transport; me = new() { Id = 42, Username = "Scroll fixture" }; signedIn.Value = true;
        await OpenLibraryItemAsync(LibraryData.FromPlaylist(transport.Playlist));
        activeCollection = new(withArt.Select(LibraryItem.FromTrack).ToArray(), null);
        collectionMode.Value = TrackViewMode.Waveform; RefreshCollectionTracks();
        await MeasureScrollCaseAsync("playlist-Waveform-art");
        Console.WriteLine("LIBRARY_SCROLL_PERF_OK: cached covers/waves, cold/warm row traversal and wheel bursts");
    }

    private async Task MeasureScrollCaseAsync(string name)
    {
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) > 1000);
        var viewer = shellScroll;
        var motion = smoothScrolls[viewer];
        motion.Stop();
        var start = 220d;
        var span = Math.Min(1600, SmoothScroll.Maximum(viewer) - start - 1);
        viewer.SetScrollOffsets(0, start);
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
        MoveActionPointer(new(Window.ClientSize.Width * .55, shellScroll.Bounds.Center.Y));
        for (var pass = 0; pass < 2; pass++)
        {
            var times = new List<double>(); var draws = new List<double>(); var layouts = new List<double>();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 160; i++)
            {
                var offset = start + span * (i < 80 ? i : 159 - i) / 79;
                var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Presented() => frame.TrySetResult();
                Window.FrameRendered += Presented;
                var watch = Stopwatch.StartNew();
                try
                {
                    viewer.SetScrollOffsets(0, offset); Window.InvalidateVisual();
                    await frame.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                }
                finally { Window.FrameRendered -= Presented; }
                var stats = Window.LastFramePerformanceStats;
                times.Add(watch.Elapsed.TotalMilliseconds); draws.Add(stats.RenderBodyMs); layouts.Add(stats.LayoutMs);
            }
            times.Sort(); draws.Sort(); layouts.Sort();
            Console.WriteLine($"SCROLL_PERF {name} pass={pass} p50={times[80]:F2} p95={times[152]:F2} max={times[^1]:F2} over20={times.Count(t => t > 20)} draw95={draws[152]:F2} layout95={layouts[152]:F2} allocMB={(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576d:F2} rows={likedRows.Count}");
        }
        var gaps = new List<double>(); var renders = new List<double>(); long previous = 0;
        void Sample()
        {
            var now = Stopwatch.GetTimestamp();
            if (previous != 0) gaps.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
            previous = now; renders.Add(Window.LastFramePerformanceStats.FrameMs);
        }
        Window.FrameRendered += Sample;
        try
        {
            for (var i = 0; i < 16; i++)
            { motion.ScrollWheel(i < 8 ? -2 : 2); await Task.Delay(90, lifetime.Token); }
            await Task.Delay(300, lifetime.Token);
        }
        finally { Window.FrameRendered -= Sample; motion.Stop(); }
        gaps.Sort(); renders.Sort();
        Console.WriteLine($"WHEEL_PERF {name} frames={renders.Count} gap95={gaps[(int)(gaps.Count * .95)]:F2} gapMax={gaps[^1]:F2} render95={renders[(int)(renders.Count * .95)]:F2} renderMax={renders[^1]:F2}");
        if (name.Contains("Waveform")) await VerifyScrollRasterReuseAsync(viewer, start, span);
    }

    private async Task VerifyScrollRasterReuseAsync(ScrollViewer viewer, double start, double span)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var field = typeof(TrackWaveform).GetField("raster", flags)!;
        var buffers = new Dictionary<TrackWaveform, WriteableBitmap>();
        var tracksSeen = new HashSet<long>();
        var rowsBefore = likedRows.Count;
        for (var i = 0; i < 80; i++)
        {
            viewer.SetScrollOffsets(0, start + span * (i < 40 ? i : 79 - i) / 39);
            await WaitForLoginFrameAsync();
            foreach (var row in likedRows.Values.Where(r => r.Track != null && r.Root.FindVisualRoot() == Window))
            {
                tracksSeen.Add(row.Track!.Id);
                if (field.GetValue(row.Waveform) is not WriteableBitmap bitmap) continue;
                if (buffers.TryGetValue(row.Waveform, out var previous) && previous.PixelWidth == bitmap.PixelWidth &&
                    previous.PixelHeight == bitmap.PixelHeight && !ReferenceEquals(previous, bitmap))
                    throw new InvalidOperationException("Scrolling rebuilt a same-size pooled waveform bitmap.");
                buffers[row.Waveform] = bitmap;
            }
        }
        if (buffers.Count < 2 || tracksSeen.Count < 8 || likedRows.Count > rowsBefore + 1)
            throw new InvalidOperationException($"Scroll reuse did not traverse tracks in a bounded pool: buffers={buffers.Count}, tracks={tracksSeen.Count}, rows={rowsBefore}->{likedRows.Count}.");
        Console.WriteLine($"SCROLL_RASTER_REUSE_OK: {tracksSeen.Count} tracks, {buffers.Count} reusable buffers, bounded row pool");
    }
}
