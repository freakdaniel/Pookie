using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyShellChromeAsync()
    {
        try
        {
            timer.Stop();
            await ShowWorkspaceAsync();
            playerVisible.Value = true;
            ShowLibraryTracks();
            for (var id = 1; id <= 80; id++)
            {
                var pixels = new byte[32 * 32 * 4];
                Color[] colors = [Color.FromRgb(94, 116, 192), Color.FromRgb(164, 92, 143),
                    Color.FromRgb(58, 155, 146), Color.FromRgb(194, 132, 78)];
                for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var color = colors[id % colors.Length].Lerp(Color.FromRgb(35, 31, 49), (x + y) / 75d);
                    var index = (y * 32 + x) * 4;
                    pixels[index] = color.B; pixels[index + 1] = color.G; pixels[index + 2] = color.R; pixels[index + 3] = 255;
                }
                libraryCoverCache[id] = ImageSource.FromBgraPixels(32, 32, pixels);
            }
            ReplaceTracks(new(Enumerable.Range(1, 80).Select(id => new SoundCloudTrack {
                Id = id, Title = "Музыка для твоего вечера " + id, Duration = 180000,
                User = new() { Username = "Pookie" } }).ToArray(), null));
            await WaitForLikedLayoutAsync(() => SmoothScroll.Maximum(shellScroll) > 1000 &&
                PageContentScroll() is { ViewportHeight: > 100 } && !shellPageScroll.IsMeasureDirty && Math.Abs(playerChrome.ActualHeight - PlayerBarHeight) < 1 / Window.DpiScale &&
                contentBottomFade.Strength == ContentBottomFade.PlayerOpacity);
            if (headerGlass.GlassAmount != 0 || HeaderSurface != Surface || contentSurface.CornerRadius != 0 ||
                contentBottomFade.IsHitTestVisible || Math.Abs(contentBottomFade.Bounds.Bottom - playerChrome.Bounds.Y) > 1)
                throw new InvalidOperationException("The resting chrome does not match the square content surface.");
            if (Environment.GetEnvironmentVariable("POOKIE_SEARCH_ONLY") == "1")
            {
                await VerifySearchScrollingAsync();
                return;
            }
            CaptureUiPreview("shell-chrome-top");
            CheckShellScrollBarBounds();
            await VerifyShellScrollBarDragAsync();
            shellScroll.SetScrollOffsets(0, 48);
            await WaitForLikedLayoutAsync(() => headerGlass.GlassAmount > .999 && headerGlass.HasBlurredBackdrop);
            VerifyGpuBlurState();
            VerifyIconRenderTargets();
            CaptureUiPreview("shell-chrome-glass");
            shellScroll.SetScrollOffsets(0, 460);
            await WaitForLikedLayoutAsync(() => PageContentScroll()!.Bounds.Y < 1 && PageContentScroll()!.VerticalOffset > 0);
            var native = PageContentScroll()!;
            var before = native.VerticalOffset;
            shellScroll.SetScrollOffsets(0, 520);
            await WaitForLikedLayoutAsync(() => Math.Abs(native.VerticalOffset - before - 60) < 1);
            if (native.CornerRadius != 0 || contentFrame.Bounds.Y >= 0 || native.Bounds.Bottom > playerChrome.Bounds.Y + 1)
                throw new InvalidOperationException("Page scrolling lost the under-header viewport, square clipping or player boundary.");
            CaptureUiPreview("shell-chrome-scrolled");
            VerifyHeaderBackdropPixels();
            var tile = likedTiles.Values.Where(item => item.Track != null && item.Cover.Bounds.Y > 200 &&
                item.Cover.Bounds.Y < playerChrome.Bounds.Y).OrderByDescending(item => item.Cover.Bounds.Y).First();
            var fadeTrackId = tile.Track!.Id;
            shellScroll.SetScrollOffsets(0, shellScroll.VerticalOffset + tile.Cover.Bounds.Y + tile.Cover.ActualHeight / 2 -
                (playerChrome.Bounds.Y - ContentBottomFade.FadeHeight / 2));
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady &&
                likedTiles.Values.Any(item => item.Track?.Id == fadeTrackId && Math.Abs(item.Cover.Bounds.Y + item.Cover.ActualHeight / 2 -
                    (playerChrome.Bounds.Y - ContentBottomFade.FadeHeight / 2)) < 1));
            tile = likedTiles.Values.Single(item => item.Track?.Id == fadeTrackId);
            VerifyBottomFadePixels(tile.Cover.Bounds.X + tile.Cover.ActualWidth / 2);
            CaptureUiPreview("shell-chrome-fade-library");
            shellScroll.SetScrollOffsets(0, SmoothScroll.Maximum(shellScroll));
            await WaitForLikedLayoutAsync(() => Math.Abs(native.VerticalOffset - SmoothScroll.Maximum(native)) < 1 &&
                likedTiles.Values.Any(tile => tile.Track?.Id == 80));
            if (likedTiles.Values.Single(item => item.Track?.Id == 80).Root.Bounds.Bottom > contentBottomFade.Bounds.Y + 1)
                throw new InvalidOperationException("The last track cannot scroll clear of the player-edge fade.");
            foreach (var (width, height) in new[] { (1000, 680), (1600, 900) })
            {
                Window.WindowSize = WindowSize.Resizable(width, height, minWidth: 1000, minHeight: 680);
                await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width - width) < 1 / Window.DpiScale && Math.Abs(shellScroll.ActualWidth - width) < 1 / Window.DpiScale &&
                    Math.Abs(shellScroll.Bounds.Bottom - playerChrome.Bounds.Y) < 1 && !shellPageScroll.IsMeasureDirty);
                CaptureUiPreview($"shell-chrome-{width}x{height}");
                CheckShellScrollBarBounds();
                await VerifyHeaderSearchLayoutAsync(width);
            }
            shellScroll.SetScrollOffsets(0, 320);
            await WaitForLikedLayoutAsync(() => shellScroll.VerticalOffset == 320 && !shellPageScroll.IsArrangeDirty);
            await NavigateAsync(Page.Home);
            await WaitForLikedLayoutAsync(() => shellScroll.VerticalOffset == 0 && headerGlass.GlassAmount == 0 &&
                Math.Abs(contentFrame.ActualWidth - Math.Min(1440, Window.ClientSize.Width)) < 1 / Window.DpiScale && !contentFrame.IsArrangeDirty &&
                shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) == 0);
            CaptureUiPreview("shell-chrome-home");
            var homeBody = homeScroll.Content;
            try
            {
                var samplingProbe = new HeaderSamplingProbe().Height(2400);
                homeScroll.Content = samplingProbe;
                await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) > 1000);
                shellScroll.SetScrollOffsets(0, 500);
                await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && samplingProbe.Bounds.Y < -300 &&
                    Math.Abs(homeScroll.Bounds.Bottom - playerChrome.Bounds.Y) < 1);
                VerifyHeaderSampling(samplingProbe);
                VerifyHeaderBlurMotion(samplingProbe);
                VerifyBottomFadePixels(Window.ClientSize.Width / 2);
                CaptureUiPreview("shell-chrome-fade-home");
                var fadeSamples = new List<double>();
                void SampleFade() => fadeSamples.Add(contentBottomFade.Strength);
                Window.FrameRendered += SampleFade;
                try
                {
                    playerVisible.Value = false;
                    await WaitForLikedLayoutAsync(() => contentBottomFade.IsVisible && contentBottomFade.Strength == 1 &&
                        Math.Abs(contentBottomFade.Bounds.Bottom - Window.ClientSize.Height) < 1);
                    VerifyBottomFadePixels(Window.ClientSize.Width / 2);
                    CheckShellScrollBarBounds();
                    CaptureUiPreview("shell-chrome-fade-without-player");
                    playerVisible.Value = true;
                    await WaitForLikedLayoutAsync(() => contentBottomFade.Strength == ContentBottomFade.PlayerOpacity &&
                        Math.Abs(playerChrome.ActualHeight - PlayerBarHeight) < 1 / Window.DpiScale);
                    if (fadeSamples.Distinct().Count(value => value > ContentBottomFade.PlayerOpacity && value < 1) < 3)
                        throw new InvalidOperationException("The persistent bottom fade skipped its opacity transition when toggling the player.");
                }
                finally { Window.FrameRendered -= SampleFade; }
            }
            finally { homeScroll.Content = homeBody; shellScroll.SetScrollOffsets(0, 0); }
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) == 0);
            // The compact connected action remains clickable near its straight inner edge.
            Window.Activate();
            await WaitForLikedLayoutAsync(() => Window.IsActive);
            RouteWaveformClick(new Point(backButton.Bounds.Right - 2, backButton.Bounds.Y + backButton.ActualHeight / 2));
            await WaitForLikedLayoutAsync(() => page.Value == Page.LibraryTracks && pendingNavigationScroll == null &&
                Math.Abs(shellScroll.VerticalOffset - 320) < 1);
            RouteWaveformPointer(new Point(2, 2));
            await WaitForLoginFrameAsync();
            if (KeyboardFocusFeedback.IsVisible(backButton))
                throw new InvalidOperationException("A pointer click left a keyboard focus cue on the history button.");
            await VerifySearchScrollingAsync();
            playerVisible.Value = false;
            await WaitForLikedLayoutAsync(() => Math.Abs(shellScroll.ActualHeight - Window.ClientSize.Height) < 1 &&
                contentBottomFade.IsVisible && contentBottomFade.Strength == 1);
            Console.WriteLine("UI_SHELL_CHROME_OK: matching resting surfaces, real blurred under-header content, native scrollbar track/hit area below header and full-range dragging, one-to-one scroll, square viewport, pixel-verified compact persistent fade with visible content at player edge, animated opacity, readable final track, 1000x680/1600x900 bounds, glass search input/dismissal and compact connected history input without a stuck focus cue");
        }
        catch (Exception error)
        {
            VerificationFailure = error; Environment.ExitCode = 1;
            CaptureUiPreview("shell-chrome-failure");
            Console.Error.WriteLine($"UI_SHELL_CHROME_FAILED: offset={shellScroll.VerticalOffset}, max={SmoothScroll.Maximum(shellScroll)}, frame={contentFrame.Bounds}, inner={PageContentScroll()?.Bounds}, innerOffset={PageContentScroll()?.VerticalOffset}: " + error);
        }
        finally { Window.Close(); }
    }

    private void VerifyIconRenderTargets()
    {
        var icon = Icons.View("heart-filled", 24, Color.White);
        icon.Measure(new Size(24, 24)); icon.Arrange(new Rect(0, 0, 24, 24));
        for (var pass = 0; pass < 2; pass++)
        {
            using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(24, 24, 1));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface); context.Clear(Color.Transparent); icon.Render(context); context.EndFrame();
            var cpu = (ICpuPixelSurface)surface;
            var pixels = cpu.GetReadOnlyPixelSpan();
            var white = 0;
            for (var y = 0; y < 24; y++)
            for (var x = 0; x < 24; x++)
            {
                var index = y * cpu.StrideBytes + x * 4;
                if (pixels[index] > 200 && pixels[index + 1] > 200 && pixels[index + 2] > 200 && pixels[index + 3] > 200) white++;
            }
            if (white < 50) throw new InvalidOperationException($"Shared icon disappeared across render targets: pass={pass}, white pixels={white}.");
        }
        Console.WriteLine("ICON_RENDER_TARGETS_OK: shared SVG remains visible across separate render targets");
    }

    private sealed class HeaderSamplingProbe : Control
    {
        internal readonly List<double> Scales = [];
        internal double StripeY = double.NaN;
        internal bool MotionSampling;
        protected override void OnRender(IGraphicsContext context)
        {
            Scales.Add(context.DpiScale);
            context.FillRectangle(Bounds, MotionSampling ? Color.Black : Color.FromRgb(220, 186, 140));
            if (double.IsFinite(StripeY))
                context.FillRectangle(new Rect(Bounds.X, StripeY, Bounds.Width, 12), Color.White);
        }
    }

    private void VerifyHeaderBlurMotion(HeaderSamplingProbe probe)
    {
        var bounds = headerGlass.Bounds; var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        var centers = new List<double>();
        byte[] ReadColumn()
        {
            context.BeginFrame(surface); context.Clear(Surface); context.Translate(-bounds.X, -bounds.Y);
            headerGlass.Render(context); context.EndFrame();
            var cpu = (ICpuPixelSurface)surface;
            return Enumerable.Range(0, surface.PixelHeight).Select(y => cpu.GetReadOnlyPixelSpan()[y * cpu.StrideBytes + surface.PixelWidth / 2 * 4 + 1]).ToArray();
        }
        probe.MotionSampling = true;
        var background = ReadColumn();
        try
        {
            for (var step = 0; step <= 16; step++)
            {
                probe.StripeY = 20 + step / scale;
                var pixels = ReadColumn();
                double sum = 0, moment = 0;
                for (var y = 0; y < surface.PixelHeight; y++)
                {
                    var weight = Math.Max(0, pixels[y] - background[y]);
                    sum += weight; moment += (y + .5) * weight;
                }
                if (sum <= 0) throw new InvalidOperationException("Header motion probe did not reach the blurred backdrop.");
                centers.Add(moment / sum);
            }
        }
        finally { probe.StripeY = double.NaN; probe.MotionSampling = false; headerGlass.RefreshBackdrop(); }
        var deltas = centers.Zip(centers.Skip(1), (a, b) => b - a).ToArray();
        if (deltas.Any(delta => delta < .35 || delta > 1.65))
            throw new InvalidOperationException($"Header backdrop moved in jumps instead of following one-pixel content motion: dpi={scale}, steps={string.Join(',', deltas.Select(v => v.ToString("F2")))}.");
        Console.WriteLine($"HEADER_MOTION_OK: dpi={scale:F2}, blurred movement={deltas.Min():F2}..{deltas.Max():F2}px for each 1px input step, without frozen samples or coarse jumps");
    }

    private void VerifyHeaderSampling(HeaderSamplingProbe probe)
    {
        var bounds = headerGlass.Bounds;
        var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        probe.Scales.Clear();
        context.BeginFrame(surface);
        context.Translate(-bounds.X, -bounds.Y);
        headerGlass.Render(context);
        context.EndFrame();
        if (probe.Scales.Count == 0 || probe.Scales.Any(value => Math.Abs(value - scale) > .0001))
            throw new InvalidOperationException($"Header capture changed the page's pixel grid: native={scale}; captures={string.Join(',', probe.Scales)}.");
        Console.WriteLine($"HEADER_SAMPLING_OK: page and header capture share native DPI {scale:F4} before downsampling");
    }

    private void VerifyHeaderBackdropPixels()
    {
        var bounds = headerGlass.Bounds;
        var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Surface); context.Translate(-bounds.X, -bounds.Y);
        headerGlass.Render(context);
        context.EndFrame();
        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var minimum = 255; var maximum = 0;
        for (var x = 20; x < surface.PixelWidth - 20; x++)
        {
            var green = pixels[(surface.PixelHeight / 2) * cpu.StrideBytes + x * 4 + 1];
            minimum = Math.Min(minimum, green); maximum = Math.Max(maximum, green);
        }
        if (maximum - minimum < 8)
            throw new InvalidOperationException($"Header blur lost the page pixels: green={minimum}..{maximum}.");
        Console.WriteLine($"SHELL_GLASS_PIXELS_OK: visible page variation through the blurred header, green={minimum}..{maximum}");
    }

    private async Task VerifyHeaderSearchLayoutAsync(int width)
    {
        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive);
        OpenTopSearch();
        await WaitForLikedLayoutAsync(() => Math.Abs(topSearchBar.ActualWidth - 480) < 1 / Window.DpiScale && topSearchBar.Opacity == 1 && topSearchInput.IsFocused);
        query.Value = "Музыка для твоего вечера";
        await WaitForLikedLayoutAsync(() => topSearchInput.Text == query.Value);
        if (Math.Abs(backButton.ActualWidth - 36) > 1 / Window.DpiScale || Math.Abs(forwardButton.ActualHeight - 36) > 1 / Window.DpiScale ||
            Math.Abs(forwardButton.Bounds.X - backButton.Bounds.Right - 2) > 1 ||
            topSearchBar.Bounds.X <= forwardButton.Bounds.Right || topSearchBar.Bounds.Right >= settingsButton.Bounds.X ||
            Math.Abs(topSearchInput.Bounds.Y + topSearchInput.ActualHeight / 2 - ShellChromeGlass.HeaderHeight / 2) > 1 ||
            topSearchInput.Text != query.Value)
            throw new InvalidOperationException("The glass search field overlaps the header actions or lost native input alignment/binding.");
        CaptureUiPreview($"shell-header-search-{width}");
        // A real outside click must still dismiss the field and restore navigation.
        RouteWaveformClick(new Point(2, 2));
        await WaitForLikedLayoutAsync(() => !searching.Value && topSearchBar.Opacity == 0 && Math.Abs(topNavigation.ActualWidth - 480) < 1 / Window.DpiScale);
        if (query.Value.Length != 0 || topSearchBar.IsHitTestVisible)
            throw new InvalidOperationException("Dismissing glass search left stale text or an invisible input target.");
    }

    private ScrollBar ShellVerticalBar()
    {
        ScrollBar? result = null;
        ((IVisualTreeHost)shellScroll).VisitChildren(element =>
        {
            if (element is not ScrollBar { Orientation: Orientation.Vertical } bar) return true;
            result = bar; return false;
        });
        return result ?? throw new InvalidOperationException("The page scrollbar is missing.");
    }

    private void CheckShellScrollBarBounds()
    {
        var bar = ShellVerticalBar();
        var above = new Point(bar.Bounds.X + bar.ActualWidth / 2, shellScroll.Bounds.Y + ShellChromeGlass.HeaderHeight / 2);
        if (Math.Abs(shellScroll.ViewportWidth - shellScroll.ActualWidth) > 1 ||
            Math.Abs(contentFrame.Bounds.X - (shellScroll.Bounds.X + (shellScroll.ActualWidth - contentFrame.ActualWidth) / 2)) > 1)
            throw new InvalidOperationException("The overlay scrollbar reduced the content width or shifted the centered frame.");
        if (!bar.IsVisible || Math.Abs(bar.Bounds.Y - shellScroll.Bounds.Y - ShellChromeGlass.HeaderHeight) > 1 ||
            Math.Abs(bar.Bounds.Bottom - shellScroll.Bounds.Bottom) > 1 || bar.HitTest(above) != null ||
            ReferenceEquals(shellScroll.HitTest(above), bar) || shellScroll.Bounds.Y >= startupHeader.Bounds.Bottom)
            throw new InvalidOperationException($"The page scrollbar overlaps the glass header or shortened the content viewport: bar={bar.Bounds}, viewport={shellScroll.Bounds}.");
    }

    private async Task VerifyShellScrollBarDragAsync()
    {
        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive);
        var bar = ShellVerticalBar();
        var x = bar.Bounds.X + bar.ActualWidth / 2;
        foreach (var down in new[] { true, false })
        {
            var start = new Point(x, down ? bar.Bounds.Y + 8 : bar.Bounds.Bottom - 8);
            var end = new Point(x, down ? bar.Bounds.Bottom - 8 : bar.Bounds.Y + 8);
            try
            {
                RouteWaveformPointer(start); PressActionPointer(start, true);
                await WaitForLikedLayoutAsync(() => bar.IsMouseCaptured);
                RouteActionHeldPointer(end); PressActionPointer(end, false);
                await WaitForLikedLayoutAsync(() => Math.Abs(shellScroll.VerticalOffset -
                    (down ? SmoothScroll.Maximum(shellScroll) : 0)) < 1 && !bar.IsMouseCaptured);
            }
            finally { Window.ReleaseMouseCapture(); }
        }
        RouteWaveformPointer(new Point(2, 2));
    }

    private void VerifyBottomFadePixels(double x)
    {
        var original = contentBottomFade.IsVisible;
        Color[] Read(bool mask)
        {
            contentBottomFade.IsVisible = mask;
            var scale = Window.DpiScale;
            using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                (int)Math.Ceiling(Window.ClientSize.Width * scale), (int)Math.Ceiling(Window.ClientSize.Height * scale), scale));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface); context.Clear(Surface); Window.Content!.Render(context); context.EndFrame();
            var cpu = (ICpuPixelSurface)surface;
            var pixels = cpu.GetReadOnlyPixelSpan();
            var colors = new List<Color>();
            foreach (var fraction in new[] { .02, .12, .5, .88, .98 })
            {
                var y = contentBottomFade.Bounds.Y + contentBottomFade.ActualHeight * fraction;
                var index = (int)(y * scale) * cpu.StrideBytes + (int)(x * scale) * 4;
                colors.Add(Color.FromRgb(pixels[index + 2], pixels[index + 1], pixels[index]));
            }
            return colors.ToArray();
        }
        try
        {
            var raw = Read(false); var masked = Read(true);
            double Distance(Color color) => Math.Abs(color.R - Surface.R) + Math.Abs(color.G - Surface.G) + Math.Abs(color.B - Surface.B);
            var retained = raw.Select((color, index) => Distance(masked[index]) / Math.Max(1, Distance(color))).ToArray();
            var readableAtPlayer = playerVisible.Value ? retained[4] is > .4 and < .65 : retained[4] < .12;
            var middle = playerVisible.Value ? retained[2] is > .65 and < .9 : retained[2] is > .3 and < .7;
            if (raw.Any(color => Distance(color) < 35) || retained[0] < .95 || retained[1] < .9 ||
                !middle || !readableAtPlayer || Math.Abs(retained[3] - retained[4]) > .08)
                throw new InvalidOperationException($"The bottom fade does not cover actual content: raw={string.Join(',', raw)}, retained={string.Join(',', retained)}.");
        }
        finally { contentBottomFade.IsVisible = original; }
    }
}
