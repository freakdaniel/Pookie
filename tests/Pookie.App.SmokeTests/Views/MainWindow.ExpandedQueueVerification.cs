using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyExpandedQueueDesignAsync()
    {
        await WaitForLikedLayoutAsync(() => expandedQueueScroll.VerticalOffset < 1 &&
            expandedQueueBlocks.Values.Any(block => block?.Key == "source"));
        var source = expandedQueueBlocks.Single(pair => pair.Value?.Key == "source");
        var playing = expandedQueueRows.Values.Single(row => row.Track?.Id == current?.Id);
        var heading = ((StackPanel)((Grid)source.Key.Content!).Children[1]).Children.OfType<TextBlock>().Last();
        if (Math.Abs(playing.Cover.Bounds.X - heading.Bounds.X) > 1 ||
            playing.HoverFill.Bounds.X >= heading.Bounds.X || playing.HoverFill.Bounds.Right <= playing.Duration.Bounds.Right)
            throw new InvalidOperationException("Queue row background does not extend beyond the heading alignment.");
        if (!expandedQueueScroll.Bounds.Contains(playing.HoverFill.Bounds) || !playing.Root.Bounds.Contains(playing.HoverFill.Bounds) ||
            !expandedPanel.Parent!.Bounds.Contains(playing.HoverFill.Bounds))
            throw new InvalidOperationException("Queue hover background escapes its row or scroll viewport and clips its rounded edges.");
        VerifyCompactTrackCorners(playing);
        ((IVisualTreeHost)expandedQueueScroll).VisitChildren(element =>
        {
            if (element is ScrollBar bar && (bar.Opacity != 0 || bar.IsHitTestVisible))
                throw new InvalidOperationException("Fullscreen queue scrollbar is visible or interactive.");
            return true;
        });
        var origin = queueOriginTitle;
        var originalPage = page.Value;
        page.Value = Page.Search; RefreshExpandedQueue();
        if (expandedQueueData.Single(block => block.Kind == QueueBlockKind.Source).Title != origin)
            throw new InvalidOperationException("Playback origin followed page navigation instead of the queue.");
        page.Value = originalPage;

        var before = source.Key.Bounds.Y;
        var positions = new List<double>();
        var intervals = new List<double>();
        long previousMotionFrame = 0;
        void Sample()
        {
            var previous = expandedQueueBlocks.FirstOrDefault(pair => pair.Value?.Track?.Id == tracks[0].Id).Key;
            if (previous != null) positions.Add(previous.VisualY);
            if (expandedQueueBlocks.Keys.Any(row => row.Animating))
            {
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (previousMotionFrame != 0) intervals.Add(System.Diagnostics.Stopwatch.GetElapsedTime(previousMotionFrame, now).TotalMilliseconds);
                previousMotionFrame = now;
            }
            else previousMotionFrame = 0;
        }
        Window.FrameRendered += Sample;
        try
        {
            await PlayAsync(tracks[1]);
            await WaitForLikedLayoutAsync(() => positions.Count >= 3 && expandedQueueBlocks.Keys.Any(row => row.Animating));
            var moving = expandedQueueBlocks.Single(pair => pair.Value?.Track?.Id == tracks[0].Id).Key;
            if (!moving.Animating) throw new InvalidOperationException("Previous queue row did not start moving.");
            typeof(ItemsControl).GetMethod("InvalidateItemBindings", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.Invoke(expandedQueue, [true]);
            await WaitForLoginFrameAsync();
            if (!moving.Animating) throw new InvalidOperationException("Rebinding the same queue row interrupted its movement.");
            var motionProgress = expandedQueueAnimation.Progress;
            var entry = playbackQueue.Current!;
            var resolvedTrack = entry.Track with { Title = entry.Track.Title + " (resolved)" };
            playbackQueue.SetPreparation(entry.EntryId, PlaybackPreparation.Ready, resolvedTrack);
            RefreshQueue();
            if (expandedQueueAnimation.Progress != motionProgress || !moving.Animating)
                throw new InvalidOperationException("Resolved track metadata restarted the queue animation.");
            await WaitForLoginFrameAsync();
            var retained = expandedQueueBlocks.Single(pair => pair.Value?.Track?.Id == tracks[0].Id).Key;
            if (!ReferenceEquals(retained, moving) || expandedQueueAnimation.Progress < motionProgress || !moving.Animating)
                throw new InvalidOperationException("Metadata refresh recycled an animated row or reset its shared timeline.");
            if (!expandedQueueRows.Values.Any(row => row.Track?.Title == resolvedTrack.Title))
                throw new InvalidOperationException("Retained queue rows did not receive resolved metadata.");
            await WaitForExpandedQueueSettledAsync();
            var after = expandedQueueBlocks.Single(pair => pair.Value?.Key == "source").Key.Bounds.Y;
            if (Math.Abs(after - before) > 2 || positions.Select(y => Math.Round(y, 1)).Distinct().Count() < 4)
                throw new InvalidOperationException($"Queue advance jumped its source or failed to animate the previous row: before={before}, after={after}, frames={string.Join(',', positions)}");
        }
        finally { Window.FrameRendered -= Sample; }
        intervals.Sort();
        if (intervals.Count > 0)
            Console.WriteLine($"EXPANDED_QUEUE_ADVANCE_TIMING: p95_ms={intervals[(int)((intervals.Count - 1) * .95)]:F1}; max_ms={intervals[^1]:F1}; frames={intervals.Count}");
        CaptureExpandedPanelPreview("expanded-queue-advanced");

        var offsets = new List<double>();
        void SampleScroll() => offsets.Add(expandedQueueScroll.VerticalOffset);
        Window.FrameRendered += SampleScroll;
        var offset = expandedQueueScroll.VerticalOffset;
        try
        {
            RouteSearchTestWheel(new Point(expandedQueue.Bounds.X + expandedQueue.ActualWidth * .5,
                expandedQueue.Bounds.Y + expandedQueue.ActualHeight * .5));
            if (Math.Abs(expandedQueueScroll.VerticalOffset - offset) > 1)
                throw new InvalidOperationException("Queue wheel input skipped directly to its target.");
            await WaitForLikedLayoutAsync(() => offsets.Select(y => Math.Round(y, 1)).Distinct().Count() >= 4 && expandedQueueScroll.VerticalOffset > offset + 20);
            if (expandedQueueScroll.VerticalOffset <= offset + 20 || offsets.Select(y => Math.Round(y, 1)).Distinct().Count() < 4)
                throw new InvalidOperationException("Fullscreen queue did not render smooth wheel scrolling.");
        }
        finally { Window.FrameRendered -= SampleScroll; }
        smoothScrolls[expandedQueueScroll].Stop();
        expandedQueueScroll.SetScrollOffsets(0, 1200);
        await WaitForLoginFrameAsync();
        await PlayAsync(tracks[2]);
        await WaitForExpandedQueueSettledAsync();
        VerifyNowPlayingAnchor();
        // A distant selection must realize the source heading and keep it at the
        // same viewport position even with hundreds of preceding virtualized rows.
        await PlayAsync(tracks[500]);
        await WaitForExpandedQueueSettledAsync();
        VerifyNowPlayingAnchor();
        await PlayAsync(tracks[501]);
        await WaitForExpandedQueueSettledAsync();
        VerifyNowPlayingAnchor();
        CaptureExpandedPanelPreview("expanded-queue-scrolled");
        await VerifyQueuePaletteTransitionAsync();
        await VerifyExpandedManualQueueAsync();
        await VerifyNativeQueueAdvanceAsync();
        SetQueue(tracks[0]);
        await PlayAsync(tracks[0]);
        await WaitForExpandedQueueSettledAsync();
        expandedQueueAnchor = null;
        expandedQueueScroll.SetScrollOffsets(0, 0); expandedQueue.ScrollIntoView(0);
        await WaitForLikedLayoutAsync(() => expandedQueueScroll.VerticalOffset < 1);
        Console.WriteLine("EXPANDED_QUEUE_DESIGN_OK: heading outsets, hidden scrollbar, retained playback origin, animated previous rows, smooth wheel scrolling and stable now-playing source anchor after scrolling and distant selections");
    }

    private async Task VerifyNativeQueueAdvanceAsync()
    {
        var previous = player;
        await using var native = new Pookie.Audio.SoundFlowPlayer(silent: true);
        var intervals = new List<double>();
        long lastFrame = 0;
        void Sample()
        {
            if (!expandedQueueBlocks.Keys.Any(row => row.Animating)) { lastFrame = 0; return; }
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (lastFrame != 0) intervals.Add(System.Diagnostics.Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds);
            lastFrame = now;
        }
        player = native;
        Window.FrameRendered += Sample;
        try
        {
            SetQueue(tracks[0]);
            await PlayAsync(tracks[0]);
            await WaitForExpandedQueueSettledAsync();
            for (var index = 1; index <= 3; index++)
            {
                var transport = (Grid)((Grid)expandedCover.Child!).Children[1];
                var next = ((StackPanel)transport.Children[1]).Children[2];
                RouteExpandedPointer(new Point(expandedCover.Bounds.X + expandedCover.Bounds.Width / 2,
                    expandedCover.Bounds.Y + expandedCover.Bounds.Height / 2));
                await WaitForLikedLayoutAsync(() => transport.Opacity > .999);
                RouteWaveformClick(new Point(next.Bounds.X + next.Bounds.Width / 2, next.Bounds.Y + next.Bounds.Height / 2));
                await WaitForLikedLayoutAsync(() => current?.Id == tracks[index].Id && audioReady);
                await WaitForExpandedQueueSettledAsync();
                if (!native.Poll().Playing || expandedCurrentEntry != playbackQueue.Current?.EntryId)
                    throw new InvalidOperationException("Native audio and fullscreen queue diverged after Next on the cover.");
            }
            if (intervals.Count < 12) throw new InvalidOperationException("Native track changes did not render intermediate queue frames.");
            intervals.Sort();
            Console.WriteLine($"EXPANDED_NATIVE_QUEUE_OK: three cover Next clicks with real native decoder/device lifecycle on a silent backend; p95_ms={intervals[(int)((intervals.Count - 1) * .95)]:F1}; max_ms={intervals[^1]:F1}; frames={intervals.Count}");
        }
        finally
        {
            Window.FrameRendered -= Sample;
            await native.StopAsync();
            player = previous;
        }
    }

    private async Task WaitForExpandedQueueSettledAsync()
    {
        await WaitForLoginFrameAsync();
        await WaitForLikedLayoutAsync(() => expandedQueueAnchor == null &&
            !expandedQueueBlocks.Keys.Any(row => row.Animating) &&
            !expandedQueue.IsMeasureDirty && !expandedQueue.IsArrangeDirty);
        await WaitForLoginFrameAsync();
    }

    private void VerifyNowPlayingAnchor()
    {
        var source = expandedQueueBlocks.Single(pair => pair.Value?.Key == "source").Key;
        var playing = expandedQueueBlocks.Single(pair => pair.Value?.Track?.Id == current?.Id).Key;
        if (Math.Abs(source.Bounds.Y - expandedQueueScroll.Bounds.Y) > 2 ||
            playing.Bounds.Y < source.Bounds.Bottom || playing.Bounds.Bottom > expandedQueueScroll.Bounds.Bottom)
            throw new InvalidOperationException($"Now-playing heading/current track escaped the viewport: source={source.Bounds}, current={playing.Bounds}, viewport={expandedQueueScroll.Bounds}");
    }

    private async Task VerifyQueuePaletteTransitionAsync()
    {
        var original = expandedBackdrop.Target;
        var rendered = new HashSet<PlayerPalette>();
        var stale = 0;
        var backdropTextures = new HashSet<object>();
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var baseField = typeof(PlayerBackdrop).GetField("rasterImage", fields)!;
        var fadeField = typeof(QueueEdgeFade).GetField("image", fields)!;
        var fadeTextures = new HashSet<object>();
        object? lastRaster = null;
        var pixelMismatch = 0;
        var nativeStages = new HashSet<int>();
        string? nativeMismatch = null;
        void Sample()
        {
            if (!expandedBackdrop.Running) return;
            rendered.Add(expandedQueueFade.RenderedPalette);
            if (expandedQueueFade.RenderedGradient != expandedBackdrop.CurrentGradient) stale++;
            lastRaster = baseField.GetValue(expandedBackdrop);
            if (lastRaster is { } texture) backdropTextures.Add(texture);
            if (fadeField.GetValue(expandedQueueFade) is { } fade) fadeTextures.Add(fade);
            var stage = (int)(expandedBackdrop.BlendAmount * 8);
            if (nativeStages.Add(stage))
            {
                var native = SamplePresentedBackdropPixels();
                for (var i = 0; i < native.Length; i++)
                {
                    var expected = expandedBackdrop.CurrentGradient.Sample((i + .5) / native.Length);
                    if (Math.Abs(native[i].R - expected.X) > 2 || Math.Abs(native[i].G - expected.Y) > 2 || Math.Abs(native[i].B - expected.Z) > 2)
                        nativeMismatch ??= $"blend={expandedBackdrop.BlendAmount:F3}, expected={expected}, presented={native[i]}, sample={i}";
                }
            }
            var pixels = SampleExpandedBackdropPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                var expected = expandedBackdrop.CurrentGradient.Sample((i + .5) / pixels.Length);
                if (Math.Abs(pixels[i].R - expected.X) > 2 || Math.Abs(pixels[i].G - expected.Y) > 2 || Math.Abs(pixels[i].B - expected.Z) > 2)
                    pixelMismatch++;
            }
            var edges = SampleExpandedBackdropPixels(includeQueueFade: true);
            for (var i = 0; i < edges.Length; i++)
            {
                var x = expandedQueueFade.Bounds.X - expandedBackdrop.Bounds.X + (i + .5) / edges.Length * expandedQueueFade.Bounds.Width;
                var expected = expandedBackdrop.CurrentGradient.Sample(x / expandedBackdrop.ActualWidth);
                if (Math.Abs(edges[i].R - expected.X) > 2 || Math.Abs(edges[i].G - expected.Y) > 2 || Math.Abs(edges[i].B - expected.Z) > 2)
                    pixelMismatch++;
            }
        }
        Window.FrameRendered += Sample;
        try
        {
            expandedBackdrop.SetPalette(new(Color.FromRgb(65, 22, 30), Color.FromRgb(50, 18, 35), Color.FromRgb(32, 18, 28)));
            await WaitForLikedLayoutAsync(() => rendered.Count >= 4 && expandedBackdrop.CurrentGradient != PlayerGradient.FromPalette(original));
            expandedBackdrop.SetPalette(new(Color.FromRgb(20, 35, 65), Color.FromRgb(20, 28, 50), Color.FromRgb(18, 25, 36)));
            nativeStages.Clear();
            await WaitForLikedLayoutAsync(() => !expandedBackdrop.Running);
            await WaitForLoginFrameAsync();
            if (rendered.Count < 4 || stale != 0 || expandedQueueFade.RenderedPalette != expandedBackdrop.Target)
                throw new InvalidOperationException($"Queue edge fades did not follow the live background palette: intermediate={rendered.Count}, stale={stale}");
            if (backdropTextures.Count != 1)
                throw new InvalidOperationException($"Player replaced its gradient image {backdropTextures.Count} times during animation.");
            if (fadeTextures.Count != 1)
                throw new InvalidOperationException($"Queue edge fades replaced their texture {fadeTextures.Count} times during color animation.");
            if (lastRaster == null || !ReferenceEquals(lastRaster, baseField.GetValue(expandedBackdrop)))
                throw new InvalidOperationException("Completing the color transition replaced the displayed raster.");
            var settled = SamplePresentedBackdropPixels();
            for (var i = 0; i < settled.Length; i++)
            {
                var expected = PlayerGradient.FromPalette(expandedBackdrop.Target).Sample((i + .5) / settled.Length);
                if (Math.Abs(settled[i].R - expected.X) > 2 || Math.Abs(settled[i].G - expected.Y) > 2 || Math.Abs(settled[i].B - expected.Z) > 2)
                    throw new InvalidOperationException("Settled backdrop pixels differ from the transition target.");
            }
            if (pixelMismatch != 0)
                throw new InvalidOperationException($"Rendered backdrop pixels disagreed with the displayed transition in {pixelMismatch} samples.");
            if (nativeMismatch != null)
                throw new InvalidOperationException("Presented background differed from the animation: " + nativeMismatch);
        }
        finally
        {
            Window.FrameRendered -= Sample;
            expandedBackdrop.SetPalette(original);
            await WaitForLikedLayoutAsync(() => !expandedBackdrop.Running);
            await WaitForLoginFrameAsync();
        }
        Console.WriteLine($"EXPANDED_QUEUE_PALETTE_OK: presented and GPU-rendered backdrop/edge pixels match intermediate/settled colors and an interrupted transition; retained images: backdrop={backdropTextures.Count}, edges={fadeTextures.Count}, color frames={rendered.Count}");
    }

    private Color[] SamplePresentedBackdropPixels()
    {
        var surface = typeof(Window).GetField("_retainedFrameSurface", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(Window) as IRenderSurface
            ?? throw new InvalidOperationException("The fullscreen window has no retained frame surface.");
        var pixels = new byte[surface.PixelWidth * surface.PixelHeight * 4];
        if (!Window.GraphicsFactory.TryReadPixels(surface, pixels, surface.PixelWidth * 4))
            throw new InvalidOperationException("Could not read the presented fullscreen frame.");
        var result = new Color[5];
        for (var i = 0; i < result.Length; i++)
        {
            var x = Math.Min(surface.PixelWidth - 1, (int)((i + .5) / result.Length * surface.PixelWidth));
            var offset = ((surface.PixelHeight - 8) * surface.PixelWidth + x) * 4;
            result[i] = Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
        }
        return result;
    }

    private Color[] SampleExpandedBackdropPixels(bool includeQueueFade = false)
    {
        var bounds = includeQueueFade ? expandedQueueFade.Bounds : expandedBackdrop.Bounds;
        var width = (int)Math.Ceiling(bounds.Width * Window.DpiScale);
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.Offscreen(width, 1, Window.DpiScale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Translate(-bounds.X, -bounds.Y);
        expandedBackdrop.Render(context);
        if (includeQueueFade) expandedQueueFade.Render(context);
        context.EndFrame();
        var pixels = new byte[width * 4];
        if (!Window.GraphicsFactory.TryReadPixels(surface, pixels, width * 4))
            throw new InvalidOperationException("Could not read the GPU-rendered backdrop probe.");
        var result = new Color[5];
        for (var i = 0; i < result.Length; i++)
        {
            var offset = Math.Min(width - 1, (int)((i + .5) / 5 * width)) * 4;
            result[i] = Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
        }
        return result;
    }

    private async Task VerifyExpandedManualQueueAsync()
    {
        var first = playbackQueue.Enqueue(tracks[1]);
        var second = playbackQueue.Enqueue(tracks[1]);
        RefreshQueue();
        await WaitForLikedLayoutAsync(() => expandedQueueRows.Values.Any(row => row.EntryId == first.EntryId && row.Root.ActualHeight > 0) &&
            expandedQueueRows.Values.Any(row => row.EntryId == second.EntryId && row.Root.ActualHeight > 0));
        if (expandedQueueData.Select(block => block.Key).Distinct().Count() != expandedQueueData.Length)
            throw new InvalidOperationException("Manual duplicate rows shared keys.");
        var offset = expandedQueueScroll.VerticalOffset;
        var selected = playbackQueue.Current!.EntryId;
        await WaitForExpandedQueueSettledAsync();
        CaptureExpandedPanelPreview("expanded-manual-queue");
        ChangeManualQueue(playbackQueue.ClearManual);
        await WaitForLikedLayoutAsync(() => playbackQueue.Snapshot.ManualUpcoming.Count == 0);
        if (playbackQueue.Current.EntryId != selected || Math.Abs(expandedQueueScroll.VerticalOffset - offset) > 2)
            throw new InvalidOperationException("Clearing manual entries changed the current track or viewport.");

        var origin = playbackQueue.Context!;
        StartQueueContext(origin with { NextHref = "fixture-page" }, tracks.ToArray(), current!);
        await PlayAsync(current!);
        await WaitForLikedLayoutAsync(() => expandedQueueScroll.VerticalOffset < 1);
        smoothScrolls[expandedQueueScroll].Stop();
        expandedQueueScroll.SetScrollOffsets(0, 400);
        await WaitForLoginFrameAsync();
        offset = expandedQueueScroll.VerticalOffset;
        playbackQueue.Append(playbackQueue.SourceVersion, "fixture-page", new([tracks[0]], null));
        RefreshQueue();
        await WaitForLoginFrameAsync();
        if (Math.Abs(expandedQueueScroll.VerticalOffset - offset) > 2)
            throw new InvalidOperationException("Appending a source page pulled the expanded queue back to now-playing.");
        Console.WriteLine("EXPANDED_MANUAL_QUEUE_OK: duplicate occurrence keys, edits preserve current/viewport, and source pagination preserves a scrolled viewport");
    }

    private void VerifyCompactTrackCorners(CompactTrackRow row)
    {
        var bounds = row.HoverFill.Bounds;
        var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        context.FillRectangle(new Rect(0, 0, bounds.Width, bounds.Height), Color.FromRgb(24, 24, 24));
        context.Translate(-bounds.X, -bounds.Y);
        // Render the real clipping ancestor, not just the border: an individually
        // rounded fill can still lose its corners at the panel's viewport edge.
        var cachedRows = expandedQueueBlocks.Keys.Select(motion => (UIElement)motion.Content!).ToArray();
        var caches = cachedRows.Select(element => element.CacheMode).ToArray();
        try
        {
            // CPU reference surfaces cannot read the native GPU row caches.
            foreach (var element in cachedRows) element.CacheMode = null;
            ((UIElement)expandedPanel.Parent!).Render(context);
        }
        finally
        {
            for (var i = 0; i < cachedRows.Length; i++) cachedRows[i].CacheMode = caches[i];
        }
        context.EndFrame();
        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var x = (int)(.5 * scale); var top = (int)(.5 * scale); var middle = (int)(bounds.Height / 2 * scale);
        if (pixels[middle * cpu.StrideBytes + x * 4 + 2] < 35 || pixels[top * cpu.StrideBytes + x * 4 + 2] > 27)
            throw new InvalidOperationException("Compact track hover background lost its rounded corners or visible horizontal inset.");
    }
}
