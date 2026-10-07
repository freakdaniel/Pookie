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
        void Sample()
        {
            var previous = expandedQueueBlocks.FirstOrDefault(pair => pair.Value?.Track?.Id == tracks[0].Id).Key;
            if (previous != null) positions.Add(previous.VisualY);
        }
        Window.FrameRendered += Sample;
        try
        {
            await PlayAsync(tracks[1]);
            await Task.Delay(100, lifetime.Token);
            var moving = expandedQueueBlocks.Single(pair => pair.Value?.Track?.Id == tracks[0].Id).Key;
            if (!moving.Animating) throw new InvalidOperationException("Previous queue row did not start moving.");
            typeof(ItemsControl).GetMethod("InvalidateItemBindings", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.Invoke(expandedQueue, [true]);
            await Task.Delay(70, lifetime.Token);
            if (!moving.Animating) throw new InvalidOperationException("Rebinding the same queue row interrupted its movement.");
            await Task.Delay(380, lifetime.Token);
            var after = expandedQueueBlocks.Single(pair => pair.Value?.Key == "source").Key.Bounds.Y;
            if (Math.Abs(after - before) > 2 || positions.Select(y => Math.Round(y, 1)).Distinct().Count() < 4)
                throw new InvalidOperationException($"Queue advance jumped its source or failed to animate the previous row: before={before}, after={after}, frames={string.Join(',', positions)}");
        }
        finally { Window.FrameRendered -= Sample; }
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
            await Task.Delay(650, lifetime.Token);
            if (expandedQueueScroll.VerticalOffset <= offset + 20 || offsets.Select(y => Math.Round(y, 1)).Distinct().Count() < 4)
                throw new InvalidOperationException("Fullscreen queue did not render smooth wheel scrolling.");
        }
        finally { Window.FrameRendered -= SampleScroll; }
        smoothScrolls[expandedQueueScroll].Stop();
        expandedQueueScroll.SetScrollOffsets(0, 1200);
        await Task.Delay(150, lifetime.Token);
        await PlayAsync(tracks[2]);
        await Task.Delay(550, lifetime.Token);
        VerifyNowPlayingAnchor();
        // A distant selection must realize the source heading and keep it at the
        // same viewport position even with hundreds of preceding virtualized rows.
        await PlayAsync(tracks[500]);
        await Task.Delay(550, lifetime.Token);
        VerifyNowPlayingAnchor();
        await PlayAsync(tracks[501]);
        await Task.Delay(550, lifetime.Token);
        VerifyNowPlayingAnchor();
        CaptureExpandedPanelPreview("expanded-queue-scrolled");
        await VerifyQueuePaletteTransitionAsync();
        await VerifyExpandedManualQueueAsync();
        SetQueue(tracks[0]);
        await PlayAsync(tracks[0]);
        await Task.Delay(550, lifetime.Token);
        expandedQueueAnchor = null;
        expandedQueueScroll.SetScrollOffsets(0, 0); expandedQueue.ScrollIntoView(0);
        await WaitForLikedLayoutAsync(() => expandedQueueScroll.VerticalOffset < 1);
        Console.WriteLine("EXPANDED_QUEUE_DESIGN_OK: heading outsets, hidden scrollbar, retained playback origin, animated previous rows, smooth wheel scrolling and stable now-playing source anchor after scrolling and distant selections");
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
        var baseTextures = new HashSet<object>();
        var targetTextures = new HashSet<object>();
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var baseField = typeof(PlayerBackdrop).GetField("rasterImage", fields)!;
        var targetField = typeof(PlayerBackdrop).GetField("targetImage", fields)!;
        void Sample()
        {
            if (!expandedBackdrop.Running) return;
            rendered.Add(expandedQueueFade.RenderedPalette);
            if (expandedQueueFade.RenderedGradient != expandedBackdrop.CurrentGradient) stale++;
            if (baseField.GetValue(expandedBackdrop) is { } texture) baseTextures.Add(texture);
            if (targetField.GetValue(expandedBackdrop) is { } target) targetTextures.Add(target);
        }
        Window.FrameRendered += Sample;
        try
        {
            expandedBackdrop.SetPalette(new(Color.FromRgb(65, 22, 30), Color.FromRgb(50, 18, 35), Color.FromRgb(32, 18, 28)));
            await Task.Delay(400, lifetime.Token);
            expandedBackdrop.SetPalette(new(Color.FromRgb(20, 35, 65), Color.FromRgb(20, 28, 50), Color.FromRgb(18, 25, 36)));
            await Task.Delay(1100, lifetime.Token);
            if (rendered.Count < 4 || stale != 0 || expandedQueueFade.RenderedPalette != expandedBackdrop.Target)
                throw new InvalidOperationException($"Queue edge fades did not follow the live background palette: intermediate={rendered.Count}, stale={stale}");
            if (baseTextures.Count > 4 || targetTextures.Count > 2)
                throw new InvalidOperationException($"Player rebuilt gradient textures during animation: base={baseTextures.Count}, target={targetTextures.Count}");
        }
        finally
        {
            Window.FrameRendered -= Sample;
            expandedBackdrop.SetPalette(original);
            await Task.Delay(1100, lifetime.Token);
        }
        Console.WriteLine($"EXPANDED_QUEUE_PALETTE_OK: native edge fade frames match the artwork palette, including an interrupted color transition; retained gradient textures: base={baseTextures.Count}, target={targetTextures.Count}, color frames={rendered.Count}");
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
        await Task.Delay(500, lifetime.Token);
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
        ((UIElement)expandedPanel.Parent!).Render(context);
        context.EndFrame();
        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var x = (int)(.5 * scale); var top = (int)(.5 * scale); var middle = (int)(bounds.Height / 2 * scale);
        if (pixels[middle * cpu.StrideBytes + x * 4 + 2] < 35 || pixels[top * cpu.StrideBytes + x * 4 + 2] > 27)
            throw new InvalidOperationException("Compact track hover background lost its rounded corners or visible horizontal inset.");
    }
}
