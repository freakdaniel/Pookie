using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyExpandedPlayerAsync()
    {
        var original = player;
        var fixture = new BufferFixturePlayer();
        var frames = new List<(double Reveal, double Side, double X, double Gap, double PanelReveal, double PanelX)>();
        var frameIntervals = new List<double>();
        long previousMotionFrame = 0;
        var escapedMotionBounds = 0;
        string? escapedMotionDetail = null;
        void Sample()
        {
            if (panelAnimation.IsRunning || panelFade.IsRunning)
            {
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (previousMotionFrame != 0)
                    frameIntervals.Add(System.Diagnostics.Stopwatch.GetElapsedTime(previousMotionFrame, now).TotalMilliseconds);
                previousMotionFrame = now;
            }
            else previousMotionFrame = 0;
            var cardLayer = (PlayerMotionLayer)expandedLayout.Children[0];
            var panelLayer = (PlayerMotionLayer)expandedLayout.Children[1];
            if (panelAnimation.IsRunning || panelFade.IsRunning)
            {
                foreach (var layer in new[] { cardLayer, panelLayer })
                {
                    if (layer.Opacity == 0) continue;
                    var childBounds = layer.Children[0].Bounds;
                    var paintedBounds = new Rect(childBounds.X + layer.OffsetX, childBounds.Y + layer.OffsetY,
                        childBounds.Width, childBounds.Height);
                    if (!layer.Bounds.Contains(paintedBounds))
                    {
                        escapedMotionBounds++;
                        escapedMotionDetail ??= $"layer={layer.Bounds}; painted={paintedBounds}; " +
                            $"visible={expandedHost.IsVisible}; reveal={expandedReveal}; side={expandedLayout.SideAmount}";
                    }
                }
            }
            var x = expandedCover.Bounds.X + cardLayer.OffsetX;
            var panelX = panelLayer.Children[0].Bounds.X + panelLayer.OffsetX;
            frames.Add((expandedReveal, expandedLayout.SideAmount, x,
                panelX - x - expandedCover.Bounds.Width, panelReveal, panelX));
        }
        timer.Stop(); player = fixture;
        Window.FrameRendered += Sample;
        try
        {
            ReplaceTracks(new TrackPage(Enumerable.Range(1, 1000).Select(i => new SoundCloudTrack
            {
                Id = i, Title = i == 1 ? "На твоей волне" : $"Следующий трек {i}", Duration = 180000,
                User = new() { Username = "Pookie preview" }
            }).ToArray(), null));
            SetQueue(tracks[0]);
            await PlayAsync(tracks[0]);
            var pixels = new byte[512 * 512 * 4];
            for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++)
            {
                var offset = (y * 512 + x) * 4;
                var ring = Math.Sqrt(Math.Pow(x - 256, 2) + Math.Pow(y - 256, 2));
                pixels[offset] = (byte)(35 + x * .15);
                pixels[offset + 1] = (byte)(95 + y * .15 + (ring is > 100 and < 145 ? 55 : 0));
                pixels[offset + 2] = (byte)(20 + x * .10);
                pixels[offset + 3] = 255;
            }
            SetPlayerArtwork(ImageSource.FromBgraPixels(512, 512, pixels), playGeneration);
            fixture.State = new(40, 180, true, false, false) { BufferedEnd = 120 };
            Poll();
            await WaitForLikedLayoutAsync(() => !progressAnimation.IsRunning && playerChrome.Height == PlayerBarHeight);
            playerArtworkButton.Focus();
            await WaitForLikedLayoutAsync(() => playerArtworkOverlay.Opacity > .99);
            CaptureUiPreview("expanded-cover-hover");
            var previous = Window.WindowState;
            SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning && expandedCover.ActualWidth > 200);
            if (Window.WindowState != WindowState.FullScreen || !expandedHost.IsVisible || workspace.IsHitTestVisible ||
                expandedArtwork.Source != artwork.Source || expandedProgress.Value != progress.Value ||
                frames.Count(frame => frame.Reveal is > 0 and < 1) < 3)
                throw new InvalidOperationException("Expanded player did not open with animation and retain playback state.");
            var centeredX = expandedCover.Bounds.X;
            var centerError = Math.Abs(expandedCover.Bounds.X + expandedCover.ActualWidth / 2 - Window.ClientSize.Width / 2);
            if (centerError > 1) throw new InvalidOperationException($"Cover is not centered: {centerError}");
            CaptureUiPreview("expanded-centered");
            await VerifyExpandedCoverHoverAsync();
            await VerifyPausedExpandedReentryAsync();
            frames.Clear();
            ToggleExpandedPanel(PlayerPanel.Queue);
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning && expandedQueue.ActualHeight > 100);
            if (escapedMotionBounds != 0)
                throw new InvalidOperationException($"Animated drawing escaped its repaint region in {escapedMotionBounds} rendered samples: {escapedMotionDetail}");
            if (expandedUpcoming.Length != 999 || expandedCover.Bounds.X >= centeredX - 100 ||
                frames.Count(frame => frame.Side is > 0 and < 1) < 3)
                throw new InvalidOperationException("Queue did not animate the cover left or display upcoming tracks.");
            CaptureExpandedPanelPreview("expanded-queue");
            await VerifyExpandedQueueDesignAsync();
            await VerifyExpandedCoverHoverAsync();
            await WaitForLikedLayoutAsync(() => !expandedLayout.IsMeasureDirty && !expandedLayout.IsArrangeDirty);
            expandedLayout.SetMotion(expandedReveal, .99, .99);
            if (expandedLayout.IsMeasureDirty || expandedLayout.IsArrangeDirty)
                throw new InvalidOperationException("Panel motion laid out the queue again.");
            expandedLayout.SetMotion(expandedReveal, 1, 1);
            var openCoverX = expandedCover.Bounds.X;
            var openPanelX = ((PlayerMotionLayer)expandedLayout.Children[1]).Children[0].Bounds.X;
            frames.Clear();
            ToggleExpandedPanel(PlayerPanel.Queue);
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning && !panelFade.IsRunning && Math.Abs(expandedCover.Bounds.X - centeredX) < 1);
            if (frames.Count(frame => frame.Side is > 0 and < 1) < 3 ||
                frames.Any(frame => frame.PanelReveal > .01 && frame.Gap < 0) ||
                !frames.Any(frame => frame.PanelReveal is > .01 and < .99 && frame.X > openCoverX + 1 && frame.PanelX < openPanelX - 1))
                throw new InvalidOperationException("Closing the queue did not slide left and fade alongside the returning cover, or overlapped it.");
            await VerifyExpandedVolumeAsync();
            await VerifyExpandedTimelineHoverAsync();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                ToggleExpandedPanel(PlayerPanel.Queue);
                await WaitForLikedLayoutAsync(() => expandedLayout.SideAmount > .15 && panelAnimation.IsRunning);
                ToggleExpandedPanel(PlayerPanel.Queue);
                await WaitForLoginFrameAsync();
                ToggleExpandedPanel(PlayerPanel.Queue);
                await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning && panelReveal == 1);
                ToggleExpandedPanel(PlayerPanel.Queue);
                await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning && !panelFade.IsRunning && panelReveal == 0);
            }
            ToggleExpandedPanel(PlayerPanel.Queue);
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning);
            ToggleExpandedPanel(PlayerPanel.Lyrics);
            await WaitForLikedLayoutAsync(() => expandedPanel.Content == expandedLyricsContent);
            await WaitForLoginFrameAsync();
            CaptureExpandedPanelPreview("expanded-lyrics-transition");
            ToggleExpandedPanel(PlayerPanel.Queue);
            await WaitForLikedLayoutAsync(() => expandedPanel.Content == expandedQueueContent);
            await WaitForLikedLayoutAsync(() => expandedQueueRows.Values.Any(row => row.Track?.Id == 3 && row.Root.ActualHeight > 0));
            var nextRow = expandedQueueRows.Values.Single(row => row.Track?.Id == 3);
            if (nextRow.Cover.ActualWidth != 44 || nextRow.Root.ActualHeight != 64 || nextRow.Title.FontSize != 14 ||
                nextRow.Author.FontSize != 12 || nextRow.Duration.ActualWidth != 48)
                throw new InvalidOperationException("Fullscreen queue does not share the compact search row layout.");
            RouteWaveformClick(new Point(nextRow.Cover.Bounds.X + 22, nextRow.Cover.Bounds.Y + 22));
            await WaitForLikedLayoutAsync(() => current?.Id == 3 && audioReady);
            if (title.Value != tracks[2].Title || expandedUpcoming.Length != 997)
                throw new InvalidOperationException("Queue selection did not update the current track and upcoming items.");
            var previousSeeks = fixture.SeekCount;
            BeginSeekDrag();
            expandedProgress.Value = 75;
            EndSeekDrag();
            await WaitForLikedLayoutAsync(() => !seeking);
            if (fixture.State.Position != 75 || fixture.SeekCount != previousSeeks + 1 || progress.Value != 75)
                throw new InvalidOperationException("Expanded seeking fed back or failed to use the existing transport.");
            fixture.State = fixture.State with { Position = 78, BufferedEnd = 145 };
            Poll();
            await WaitForLikedLayoutAsync(() => !progressAnimation.IsRunning && !expandedBuffer.Animating);
            if (expandedProgress.Value != 78 || Math.Abs(expandedBuffer.EndFraction - 145d / 180) > .00001)
                throw new InvalidOperationException("Expanded progress and buffer stopped following playback.");
            SetExpandedPlayer(false);
            await WaitForLoginFrameAsync();
            SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            if (!expandedOpen || !expandedHost.IsVisible) throw new InvalidOperationException("Interrupted close hid the reopened player.");
            await VerifyExpandedCoverHoverAsync();
            SetExpandedPlayer(false);
            await WaitForLikedLayoutAsync(() => !expandedHost.IsVisible && Window.WindowState == previous);
            await WaitForLikedLayoutAsync(() => playerArtworkOverlay.Opacity == 0);
            if (!playerArtworkButton.IsFocused)
                throw new InvalidOperationException("Closing fullscreen lost the artwork's keyboard focus.");
            Window.WindowState = WindowState.Maximized;
            await WaitForLoginFrameAsync();
            SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            SetExpandedPlayer(false);
            await WaitForLikedLayoutAsync(() => !expandedHost.IsVisible && Window.WindowState == WindowState.Maximized);
            SetExpandedPlayer(true);
            playerVisible.Value = false;
            if (expandedHost.IsVisible || expandedAnimation.IsRunning || panelAnimation.IsRunning)
                throw new InvalidOperationException("Reset left a fullscreen overlay or animation alive.");
            frameIntervals.Sort();
            if (escapedMotionBounds != 0)
                throw new InvalidOperationException($"Animated drawing escaped its repaint region in {escapedMotionBounds} rendered samples: {escapedMotionDetail}");
            if (frameIntervals.Count > 0)
                Console.WriteLine($"EXPANDED_MOTION_TIMING: queue=1000 tracks; frame_interval_p95_ms={frameIntervals[(int)((frameIntervals.Count - 1) * .95)]:F1}; samples={frameIntervals.Count}");
            Console.WriteLine("EXPANDED_UI_OK: hover reset, native fullscreen, volume/time reveal and debounce, repaint bounds across repeated/interrupted motion, simultaneous closing without queue remeasurement/overlap, queue selection, shared seek/buffer and window restore");
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("EXPANDED_UI_FAILED: " + error); }
        finally { Window.FrameRendered -= Sample; player = original; Window.Close(); }
    }

    private void CaptureExpandedPanelPreview(string name)
    {
        if (expandedPanel.Content is not UIElement content) throw new InvalidOperationException("Panel content is missing.");
        // CPU previews cannot read a previously built native GPU bitmap. Render the
        // same live tree for the artifact, then restore the production cache policy.
        var cache = content.CacheMode;
        content.CacheMode = null;
        try { CaptureUiPreview(name); }
        finally { content.CacheMode = cache; }
    }

    private async Task VerifyExpandedCoverHoverAsync()
    {
        var transport = (UIElement)((Grid)expandedCover.Child!).Children[1];
        for (var cycle = 0; cycle < 3; cycle++)
        {
            RouteWaveformPointer(new Point(1, 1));
            await WaitForLikedLayoutAsync(() => transport.Opacity < .001);
            if (transport.IsHitTestVisible)
                throw new InvalidOperationException("Hidden cover controls still accept clicks.");
            var point = new Point(expandedCover.Bounds.X + expandedCover.ActualWidth * .5,
                expandedCover.Bounds.Y + expandedCover.ActualHeight * .5);
            RouteWaveformPointer(point);
            await WaitForLikedLayoutAsync(() => expandedCover.IsMouseOver && transport.Opacity > .999);
        }
        CaptureUiPreview("expanded-controls-hover");
        RouteWaveformPointer(new Point(1, 1));
        await WaitForLikedLayoutAsync(() => transport.Opacity < .001);
    }

    private async Task VerifyPausedExpandedReentryAsync()
    {
        var transport = (UIElement)((Grid)expandedCover.Child!).Children[1];
        if (isPlaying.Value) await ToggleAsync();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            RouteWaveformPointer(new Point(1, 1));
            await WaitForLikedLayoutAsync(() => transport.Opacity < .001);
            SetExpandedPlayer(false);
            await WaitForLikedLayoutAsync(() => !expandedHost.IsVisible);
            SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            await Task.Delay(600, lifetime.Token);

            // Reproduce an intermediate fullscreen resize whose viewport misses the retained
            // motion layer. Its invisible overlay must still be able to request future frames.
            var viewport = typeof(UIElement).GetProperty("RenderCullViewport",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var previousViewport = viewport.GetValue(null);
            using (var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope())
            using (var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(1, 1, Window.DpiScale)))
            using (var context = Window.GraphicsFactory.CreateContext(surface))
            {
                context.BeginFrame(surface);
                try
                {
                    viewport.SetValue(null, new Rect(0, 0, 1, 1));
                    ((UIElement)expandedLayout.Children[0]).Render(context);
                }
                finally { viewport.SetValue(null, previousViewport); context.EndFrame(); }
            }
            Window.Invalidate();
            await Task.Delay(300, lifetime.Token);
            var frames = new List<double>();
            void Sample() => frames.Add(transport.Opacity);
            Window.FrameRendered += Sample;
            try
            {
                RouteWaveformPointer(new Point(expandedCover.Bounds.X + expandedCover.ActualWidth * .12,
                    expandedCover.Bounds.Y + expandedCover.ActualHeight * .35));
                await Task.Delay(500, lifetime.Token);
                if (isPlaying.Value || transport.Opacity < .999 || frames.Count(value => value is > 0 and < 1) < 3 ||
                    !frames.Any(value => value > .999))
                    throw new InvalidOperationException($"Paused fullscreen reentry did not paint the cover overlay without a progress hover: cycle={cycle}, opacity={transport.Opacity}, frames={string.Join(',', frames)}");
            }
            finally { Window.FrameRendered -= Sample; }
        }
        CaptureUiPreview("expanded-paused-reentry-hover");
        RouteWaveformPointer(new Point(1, 1));
        await WaitForLikedLayoutAsync(() => transport.Opacity < .001);
        await ToggleAsync();
        Console.WriteLine("EXPANDED_PAUSED_REENTRY_OK: three fullscreen reopen cycles, resize culling, native intermediate and final overlay frames without clicks or progress hover");
    }

    private async Task VerifyExpandedVolumeAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!GetLikedActionCursor(out var originalCursor)) throw new InvalidOperationException("Could not save cursor position.");
        var widths = new List<double>();
        void Sample() => widths.Add(expandedVolume.ActualWidth);
        Window.FrameRendered += Sample;
        Window.Activate();
        try
        {
            var coverPoint = new Point(expandedCover.Bounds.X + expandedCover.Bounds.Width / 2,
                expandedCover.Bounds.Y + expandedCover.Bounds.Height / 2);
            MoveLikedActionCursor(coverPoint); SendLikedActionMouse(0x200, 0, coverPoint);
            await WaitForExpandedPointerAsync(coverPoint, () => expandedCover.IsMouseOver && (expandedVolume.Parent as UIElement)?.Opacity > .99);
            var icon = new Point(expandedVolume.Bounds.X + 19, expandedVolume.Bounds.Y + 19);
            MoveLikedActionCursor(icon); SendLikedActionMouse(0x200, 0, icon);
            await WaitForExpandedPointerAsync(icon, () => expandedVolume.IsMouseOver && expandedVolume.ActualWidth > 159 && expandedVolumeSlider.Opacity > .99);
            if (widths.Count(width => width is > 38 and < 160) < 3 ||
                expandedVolume.Bounds.X < expandedCover.Bounds.X || expandedVolume.Bounds.Y < expandedCover.Bounds.Y)
                throw new InvalidOperationException("Volume did not unfold on the cover's top left with rendered intermediate frames.");
            var previous = volume.Value;
            var sliderPoint = new Point(expandedVolumeSlider.Bounds.X + expandedVolumeSlider.Bounds.Width * .36,
                expandedVolumeSlider.Bounds.Y + expandedVolumeSlider.Bounds.Height / 2);
            MoveLikedActionCursor(sliderPoint); SendLikedActionMouse(0x200, 0, sliderPoint);
            SendLikedActionMouse(0x201, 1, sliderPoint);
            await WaitForLikedLayoutAsync(() => expandedVolumeSlider.IsMouseCaptured);
            SendLikedActionMouse(0x202, 0, sliderPoint);
            await WaitForLikedLayoutAsync(() => !expandedVolumeSlider.IsMouseCaptured);
            if (Math.Abs(volume.Value - previous) < .1 || volume.Value != expandedVolumeSlider.Value || volumeSlider.Value != volume.Value)
                throw new InvalidOperationException("Cover volume stopped sharing the player volume binding.");
            volume.Value = previous;
            CaptureUiPreview("expanded-volume");
            var outside = new Point(1, 1);
            MoveLikedActionCursor(outside); SendLikedActionMouse(0x200, 0, outside);
            await WaitForLikedLayoutAsync(() => !expandedVolume.IsMouseOver);
            if (expandedVolume.Width != 160 || !expandedVolumeSlider.IsEnabled)
                throw new InvalidOperationException("Volume collapsed before the hover debounce elapsed.");
            MoveLikedActionCursor(icon); SendLikedActionMouse(0x200, 0, icon);
            await WaitForExpandedPointerAsync(icon, () => expandedVolume.IsMouseOver && expandedVolume.ActualWidth > 159);
            widths.Clear();
            MoveLikedActionCursor(outside); SendLikedActionMouse(0x200, 0, outside);
            await WaitForLikedLayoutAsync(() => Math.Abs(expandedVolume.ActualWidth - 38) <= 1 / Window.DpiScale && expandedVolumeSlider.Opacity == 0);
            if (widths.Count(width => width is > 38 and < 160) < 3 || expandedVolumeSlider.IsEnabled)
                throw new InvalidOperationException("Volume did not fold smoothly or left a hidden slider enabled.");
        }
        finally
        {
            Window.FrameRendered -= Sample;
            if (!SetLikedActionCursor(originalCursor.X, originalCursor.Y)) throw new InvalidOperationException("Could not restore cursor position.");
        }
    }

    private async Task WaitForExpandedPointerAsync(Point point, Func<bool> ready)
    {
        // Drive a pointer trajectory through the transition. Opacity and placement
        // changes can replace the hit target underneath a stationary native pointer.
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (ready()) return;
            var next = new Point(point.X + (attempt % 2), point.Y);
            SendLikedActionMouse(0x200, 0, next);
            await WaitForLoginFrameAsync();
        }
        throw new InvalidOperationException($"Pointer reveal failed: cover_hover={expandedCover.IsMouseOver}, volume_hover={expandedVolume.IsMouseOver}, width={expandedVolume.ActualWidth}, opacity={expandedVolumeSlider.Opacity}");
    }

    private async Task VerifyExpandedTimelineHoverAsync()
    {
        if (expandedPositionLabel.Opacity != 0 || expandedDurationLabel.Opacity != 0)
            throw new InvalidOperationException("Expanded time labels were visible before progress hover.");
        if (!OperatingSystem.IsWindows()) return;
        if (!GetLikedActionCursor(out var originalCursor)) throw new InvalidOperationException("Could not save cursor position.");
        var labelBounds = expandedPositionLabel.Bounds;
        var frames = new List<double>();
        void Sample() => frames.Add(expandedPositionLabel.Opacity);
        Window.FrameRendered += Sample;
        try
        {
            var point = new Point(expandedProgress.Bounds.X + expandedProgress.Bounds.Width * .4,
                expandedProgress.Bounds.Y + expandedProgress.Bounds.Height / 2);
            var outside = new Point(1, 1);
            MoveLikedActionCursor(point); SendLikedActionMouse(0x200, 0, point);
            await WaitForExpandedPointerAsync(point, () => expandedPositionLabel.Opacity > .99 && expandedDurationLabel.Opacity > .99);
            MoveLikedActionCursor(outside); SendLikedActionMouse(0x200, 0, outside);
            await WaitForLikedLayoutAsync(() => !expandedProgress.IsMouseOver);
            if (expandedPositionLabel.Opacity < .99) throw new InvalidOperationException("Time labels skipped their hide debounce.");
            MoveLikedActionCursor(point); SendLikedActionMouse(0x200, 0, point);
            await WaitForExpandedPointerAsync(point, () => expandedProgress.IsMouseOver);
            SendLikedActionMouse(0x201, 1, point);
            await WaitForLikedLayoutAsync(() => expandedProgress.IsMouseCaptured);
            MoveLikedActionCursor(outside); SendLikedActionMouse(0x200, 1, outside);
            await WaitForLikedLayoutAsync(() => !expandedProgress.IsMouseOver);
            var dragStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            await WaitForLikedLayoutAsync(() => System.Diagnostics.Stopwatch.GetElapsedTime(dragStarted).TotalMilliseconds > 350);
            if (expandedPositionLabel.Opacity < .99 || expandedDurationLabel.Opacity < .99)
                throw new InvalidOperationException("Time labels disappeared during a captured progress drag.");
            SendLikedActionMouse(0x202, 0, outside);
            await WaitForLikedLayoutAsync(() => !expandedProgress.IsMouseCaptured && !seeking);
            await WaitForLikedLayoutAsync(() => expandedPositionLabel.Opacity == 0 && expandedDurationLabel.Opacity == 0);
            if (frames.Count(value => value is > 0 and < 1) < 6 || expandedPositionLabel.Bounds != labelBounds)
                throw new InvalidOperationException("Time labels failed to fade without moving the layout.");
        }
        finally
        {
            Window.FrameRendered -= Sample;
            Window.ReleaseMouseCapture();
            if (!SetLikedActionCursor(originalCursor.X, originalCursor.Y)) throw new InvalidOperationException("Could not restore cursor position.");
        }
    }
}
