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
            VerifyBackdropPixelBuffers();
            VerifyBackdropBlurPixels();
            VerifyShadowRasterPixels();
            VerifyGlassCachePixels();
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
                contentBottomFade.Strength == ContentEdgeFade.PlayerOpacity);
            if (HeaderSurface != Surface || contentSurface.CornerRadius != 0 ||
                contentBottomFade.IsHitTestVisible || Math.Abs(contentBottomFade.Bounds.Bottom - playerChrome.Bounds.Y) > 1)
                throw new InvalidOperationException("The resting chrome does not match the square content surface.");
            VerifyTopFadePixels();
            await VerifyHeaderCardsAsync();
            await VerifyShellHistoryFirstPaintAsync();
            if (Environment.GetEnvironmentVariable("POOKIE_SEARCH_ONLY") == "1")
            {
                await VerifySearchScrollingAsync();
                return;
            }
            CaptureUiPreview("shell-chrome-top");
            CheckShellScrollBarBounds();
            await VerifyShellScrollBarDragAsync();
            shellScroll.SetScrollOffsets(0, 48);
            await WaitForLoginFrameAsync();
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
            var tile = likedTiles.Values.Where(item => item.Track != null && item.Cover.Bounds.Y > 200 &&
                item.Cover.Bounds.Y < playerChrome.Bounds.Y).OrderByDescending(item => item.Cover.Bounds.Y).First();
            var fadeTrackId = tile.Track!.Id;
            // Realizing a new grid row refines its height estimate. Position the
            // pixel sample from that row's current geometry after each frame.
            for (var alignment = 0; alignment < 8; alignment++)
            {
                tile = likedTiles.Values.Single(item => item.Track?.Id == fadeTrackId);
                var correction = tile.Cover.Bounds.Y + tile.Cover.ActualHeight / 2 -
                    (playerChrome.Bounds.Y - ContentEdgeFade.BottomHeight / 2);
                if (shellPageScroll.IsReady && Math.Abs(correction) < 1) break;
                shellScroll.SetScrollOffsets(0, shellScroll.VerticalOffset + correction);
                await WaitForLoginFrameAsync();
            }
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady &&
                likedTiles.Values.Any(item => item.Track?.Id == fadeTrackId && Math.Abs(item.Cover.Bounds.Y + item.Cover.ActualHeight / 2 -
                    (playerChrome.Bounds.Y - ContentEdgeFade.BottomHeight / 2)) < 1));
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
                VerifyHeaderCardLayout();
                await VerifyHeaderSearchLayoutAsync(width);
            }
            shellScroll.SetScrollOffsets(0, 320);
            await WaitForLikedLayoutAsync(() => shellScroll.VerticalOffset == 320 && !shellPageScroll.IsArrangeDirty);
            await NavigateAsync(Page.Home);
            await WaitForLikedLayoutAsync(() => shellScroll.VerticalOffset == 0 &&
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
                    await WaitForLikedLayoutAsync(() => contentBottomFade.Strength == ContentEdgeFade.PlayerOpacity &&
                        Math.Abs(playerChrome.ActualHeight - PlayerBarHeight) < 1 / Window.DpiScale);
                    if (fadeSamples.Distinct().Count(value => value > ContentEdgeFade.PlayerOpacity && value < 1) < 3)
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
            Console.WriteLine("UI_SHELL_CHROME_OK: three live glass cards, clear gaps over page content, native scrollbar track/hit area below header and full-range dragging, one-to-one scroll, square viewport, pixel-verified compact persistent fade with visible content at player edge, animated opacity, readable final track, 1000x680/1600x900 bounds, glass search input/dismissal and compact connected history input without a stuck focus cue");
        }
        catch (Exception error)
        {
            VerificationFailure = error; Environment.ExitCode = 1;
            CaptureUiPreview("shell-chrome-failure");
            Console.Error.WriteLine($"UI_SHELL_CHROME_FAILED: offset={shellScroll.VerticalOffset}, max={SmoothScroll.Maximum(shellScroll)}, frame={contentFrame.Bounds}, inner={PageContentScroll()?.Bounds}, innerOffset={PageContentScroll()?.VerticalOffset}: " + error);
        }
        finally { Window.Close(); }
    }

    private async Task VerifyShellHistoryFirstPaintAsync()
    {
        var likes = PageContentScroll()!;
        shellScroll.SetScrollOffsets(0, 320);
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && likes.VerticalOffset > 0);
        var savedLikes = (likes.VerticalOffset, likes.Bounds.Y);
        var homeBody = homeScroll.Content;
        try
        {
            homeScroll.Content = new HeaderSamplingProbe().Height(2400);
            await NavigateAsync(Page.Home);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) > 1000);
            shellScroll.SetScrollOffsets(0, 500);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && homeScroll.VerticalOffset > 0);
            var savedHome = (homeScroll.VerticalOffset, homeScroll.Bounds.Y);
            Window.Activate();
            await WaitForLikedLayoutAsync(() => Window.IsActive);
            await VerifyHistoryFirstPaintAsync(backButton, Page.LibraryTracks, likes, savedLikes);
            await VerifyHistoryFirstPaintAsync(forwardButton, Page.Home, homeScroll, savedHome);
            await MoveNavigationAsync(-1);
        }
        finally { homeScroll.Content = homeBody; }
        shellScroll.SetScrollOffsets(0, 0);
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && likes.VerticalOffset < 1);
        Console.WriteLine("UI_HISTORY_FIRST_PAINT_OK: back and forward paint the saved viewport on their first frame");
    }

    private async Task VerifyHistoryFirstPaintAsync(Button arrow, Page destination, ScrollViewer inner, (double Offset, double Top) saved)
    {
        var firstPaint = new TaskCompletionSource<(double Offset, double Top)>();
        void Sample()
        {
            if (page.Value == destination) firstPaint.TrySetResult((inner.VerticalOffset, inner.Bounds.Y));
        }
        Window.FrameRendered += Sample;
        try
        {
            RouteWaveformClick(arrow.Bounds.Center);
            var painted = await firstPaint.Task.WaitAsync(TimeSpan.FromSeconds(4), lifetime.Token);
            if (Math.Abs(painted.Offset - saved.Offset) > 1 || Math.Abs(painted.Top - saved.Top) > 1)
                throw new InvalidOperationException($"History painted {destination} at {painted.Offset}/{painted.Top} before restoring {saved.Offset}/{saved.Top}.");
        }
        finally { Window.FrameRendered -= Sample; }
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
        internal Color Ink = Color.FromRgb(220, 186, 140);
        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Ink);
    }

    private void VerifyHeaderCardLayout()
    {
        var history = startupHeader.HistoryCard.Bounds;
        var nav = startupHeader.NavigationCard.Bounds;
        var account = startupHeader.AccountCard.Bounds;
        if (history.IsEmpty || nav.IsEmpty || account.IsEmpty || history.Right >= nav.X || nav.Right >= account.X ||
            Math.Abs(nav.Y - startupHeader.Bounds.Y - 14) > 1 || Math.Abs(history.Y - nav.Y) > 1 || Math.Abs(account.Y - nav.Y) > 1 ||
            Math.Abs(history.Height - nav.Height) > 1 || Math.Abs(nav.Height - account.Height) > 1 ||
            Math.Abs(nav.X + nav.Width / 2 - Window.ClientSize.Width / 2) > 1)
            throw new InvalidOperationException($"The header cards lost their spacing or alignment: {history}, {nav}, {account}.");

        var firstNav = ShellHeader.StateLayerBounds((Button)topNavigation.Children[0], Window.DpiScale);
        var lastNav = ShellHeader.StateLayerBounds((Button)topNavigation.Children[^1], Window.DpiScale);
        var forward = ShellHeader.StateLayerBounds(forwardButton, Window.DpiScale);
        var settings = ShellHeader.StateLayerBounds(settingsButton, Window.DpiScale);
        VerifyHeaderInset(history, forward, history.Right - forward.Right);
        VerifyHeaderInset(nav, firstNav, firstNav.X - nav.X);
        VerifyHeaderInset(nav, lastNav, nav.Right - lastNav.Right);
        VerifyHeaderInset(account, settings, settings.X - account.X);
    }

    private static void VerifyHeaderInset(Rect card, Rect item, double side)
    {
        var top = item.Y - card.Y;
        var bottom = card.Bottom - item.Bottom;
        if (top <= 0 || Math.Abs(side - top) > .001 || Math.Abs(bottom - top) > .001)
            throw new InvalidOperationException($"Header highlight lost its even inset: side={side}, top={top}, bottom={bottom}, card={card}, item={item}.");
    }

    private async Task VerifyHeaderCardsAsync()
    {
        VerifyHeaderCardLayout();
        var cards = new[] { startupHeader.HistoryCard, startupHeader.NavigationCard, startupHeader.AccountCard };
        await WaitForLikedLayoutAsync(() => cards.All(card => card.PopupBackdropPaintVersion > 0));
        if (!OperatingSystem.IsLinux()) return;

        using var fixture = new HeaderSamplingProbe { IsHitTestVisible = false };
        workspace.Insert(workspace.Children.ToList().IndexOf((UIElement)startupHeader.Parent!), fixture.Row(0));
        var nav = startupHeader.NavigationCard.Bounds;
        var gap = new Point((startupHeader.HistoryCard.Bounds.Right + nav.X) / 2, nav.Y + 7);
        var inside = new Point(nav.X + nav.Width / 2, gap.Y);
        Color Read(Point point)
        {
            using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var target = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(4, 4, 1));
            using var context = Window.GraphicsFactory.CreateContext(target);
            context.BeginFrame(target);
            context.Translate(2 - point.X, 2 - point.Y);
            fixture.Render(context);
            startupHeader.Render(context);
            context.EndFrame();
            var pixels = (ICpuPixelSurface)target;
            var data = pixels.GetReadOnlyPixelSpan();
            var offset = 2 * pixels.StrideBytes + 8;
            return Color.FromRgb(data[offset + 2], data[offset + 1], data[offset]);
        }
        try
        {
            var versions = cards.Select(card => card.PopupBackdropPaintVersion).ToArray();
            fixture.Ink = Color.FromRgb(235, 20, 20); fixture.InvalidateVisual();
            await WaitForLoginFrameAsync();
            if (cards.Where((card, index) => card.PopupBackdropPaintVersion <= versions[index]).Any())
                throw new InvalidOperationException("A header card did not update with the same frame as its background.");
            var clear = Read(gap);
            var red = Read(inside);
            fixture.Ink = Color.FromRgb(20, 20, 235); fixture.InvalidateVisual();
            await WaitForLoginFrameAsync();
            var blue = Read(inside);
            if (clear != Color.FromRgb(235, 20, 20) || red.R < red.B + 20 || blue.B < blue.R + 20)
                throw new InvalidOperationException($"The header painted its gap or lost the live card pixels: gap={clear}, red={red}, blue={blue}.");

            startupHeader.Opacity = 0;
            await WaitForLoginFrameAsync();
            versions = cards.Select(card => card.PopupBackdropPaintVersion).ToArray();
            fixture.Ink = Color.FromRgb(235, 20, 20); fixture.InvalidateVisual();
            startupHeader.Opacity = .25;
            await WaitForLoginFrameAsync();
            if (cards.Where((card, index) => card.PopupBackdropPaintVersion <= versions[index]).Any())
                throw new InvalidOperationException("A header card missed the first visible frame of the reveal.");
        }
        finally { startupHeader.Opacity = 1; workspace.Remove(fixture); }
        await WaitForLoginFrameAsync();
        Console.WriteLine("HEADER_GLASS_CARDS_OK: separate cards, exact page pixels in the gap, same-frame backdrop changes and first visible reveal frame");
    }

    private async Task VerifyHeaderSearchLayoutAsync(int width)
    {
        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive);
        OpenTopSearch();
        await WaitForLikedLayoutAsync(() => Math.Abs(topSearchBar.ActualWidth - ShellHeader.NavigationWidth) < 1 / Window.DpiScale && topSearchBar.Opacity == 1 && topSearchInput.IsFocused);
        query.Value = "Музыка для твоего вечера";
        await WaitForLikedLayoutAsync(() => topSearchInput.Text == query.Value);
        var navigation = startupHeader.NavigationCard.Bounds;
        if (Math.Abs(backButton.ActualWidth - 36) > 1 / Window.DpiScale || Math.Abs(forwardButton.ActualHeight - 36) > 1 / Window.DpiScale ||
            Math.Abs(forwardButton.Bounds.X - backButton.Bounds.Right - 2) > 1 ||
            topSearchBar.Bounds.X <= forwardButton.Bounds.Right || topSearchBar.Bounds.Right >= settingsButton.Bounds.X ||
            Math.Abs(topSearchInput.Bounds.Y + topSearchInput.ActualHeight / 2 - (navigation.Y + navigation.Height / 2)) > 1 ||
            topSearchInput.Text != query.Value)
            throw new InvalidOperationException("The glass search field overlaps the header actions or lost native input alignment/binding.");
        CaptureUiPreview($"shell-header-search-{width}");
        // A real outside click must still dismiss the field and restore navigation.
        RouteWaveformClick(new Point(2, 2));
        await WaitForLikedLayoutAsync(() => !searching.Value && topSearchBar.Opacity == 0 && Math.Abs(topNavigation.ActualWidth - ShellHeader.NavigationWidth) < 1 / Window.DpiScale);
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
        var above = new Point(bar.Bounds.X + bar.ActualWidth / 2, shellScroll.Bounds.Y + ShellHeader.HeaderHeight / 2);
        if (Math.Abs(shellScroll.ViewportWidth - shellScroll.ActualWidth) > 1 ||
            Math.Abs(contentFrame.Bounds.X - (shellScroll.Bounds.X + (shellScroll.ActualWidth - contentFrame.ActualWidth) / 2)) > 1)
            throw new InvalidOperationException("The overlay scrollbar reduced the content width or shifted the centered frame.");
        if (!bar.IsVisible || Math.Abs(bar.Bounds.Y - shellScroll.Bounds.Y - ShellHeader.HeaderHeight) > 1 ||
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

    private void VerifyTopFadePixels()
    {
        var bounds = contentTopFade.Bounds;
        if (!contentSurface.IsAncestorOf(contentTopFade) || contentTopFade.IsHitTestVisible ||
            Math.Abs(bounds.Y - contentSurface.Bounds.Y) > 1 || bounds.Bottom <= startupHeader.Bounds.Bottom)
            throw new InvalidOperationException("The top fade must cover the content behind the header without intercepting input.");

        var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.White); context.Translate(-bounds.X, -bounds.Y);
        contentTopFade.Render(context); context.EndFrame();
        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var top = pixels[4];
        var middle = pixels[(int)(bounds.Height * scale / 2) * cpu.StrideBytes + 4];
        var bottom = pixels[(cpu.PixelHeight - 2) * cpu.StrideBytes + 4];
        if (top > 75 || middle <= top + 45 || bottom < 250)
            throw new InvalidOperationException($"The top fade does not smoothly uncover a light background: {top}, {middle}, {bottom}.");
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
