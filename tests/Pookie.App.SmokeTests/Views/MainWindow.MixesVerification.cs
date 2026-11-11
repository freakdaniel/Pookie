using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyLibraryMixesUiAsync(HomeFixtureTransport transport)
    {
        var reads = transport.MixReads;
        var mixes = LibraryItems("mixes");
        if (mixes.Length != 9 || mixes.Select(item => item.Key).Distinct().Count() != 9)
            throw new InvalidOperationException("Library must include all personal mixes, Daily Drops and Weekly Wave, without editorial charts.");
        await ShowLibrarySectionAsync(Page.LibraryMixes);
        await WaitForLikedLayoutAsync(() => collectionGrid.ItemsSource.Count == 9 && collectionGrid.ActualWidth > 100 && libraryTabs.IsVisible);
        if (librarySectionTitle.Value != "Миксы" || transport.MixReads != reads)
            throw new InvalidOperationException("Library mixes did not reuse the account's discover cache.");
        await Task.Delay(260, lifetime.Token);
        CaptureUiPreview("library-mixes-wide");

        Window.WindowSize = WindowSize.Resizable(1000, 900, minWidth: 1000, minHeight: 680);
        await WaitForLikedLayoutAsync(() => libraryColumns == 4 && libraryTabs.Children.Last().Bounds.Width > 0);
        if (libraryTabs.Children.Last().Bounds.Right > contentFrame.Bounds.Right + 1)
            throw new InvalidOperationException("The Mixes tab pushes library navigation outside the narrow viewport.");
        var card = collectionCards.Keys.First(root => collectionGrid.IsAncestorOf(root) && collectionCards[root].Item.Key == mixes[0].Key);
        ClickHomeElement(card);
        await WaitForLikedLayoutAsync(() => page.Value == Page.LibraryCollection && !collectionLoadingView.Skeleton.IsVisible &&
            collectionCover.ActualWidth is >= 144 and <= 160 && collectionCover.Bounds.Right < collectionTitle.Bounds.X && collectionBackdrop.Opacity == 1);
        if (libraryTabs.IsVisible || !collectionHero.HeroArtworkLeading || !collectionMixPlay.IsVisible ||
            !collectionMixPlay.IsEnabled || collectionPlay.IsVisible || collectionSocialActions.IsVisible || collectionFollow.IsVisible ||
            collectionMore.IsVisible || collectionTitle.FontSize != 48 || collectionMeta.Text.StartsWith("·") ||
            VisualTree.Find(collectionHero, element => element is TextBlock { Text: "Микс для тебя" }) != null)
            throw new InvalidOperationException("Mix detail retained library tabs, ordinary playlist actions or the old hero layout.");
        if (collectionHero.ActualHeight > 224 ||
            Math.Abs(collectionMixTags.Bounds.Y - collectionHero.Bounds.Y) > 1 ||
            Math.Abs(collectionMixTags.Bounds.Right - collectionHero.Bounds.Right + 24) > 1 ||
            collectionMixTags.Bounds.X < collectionTitle.Bounds.Right + 16 ||
            collectionMixTags.Children.Count != 3 || Math.Abs(collectionMixPlay.Bounds.Bottom - collectionCover.Bounds.Bottom) > 1 ||
            collectionMeta.Text != "Исполнитель 1, Исполнитель 2, Исполнитель 3" || collectionDescriptionHost.IsVisible ||
            !collectionMixStats.Text.Contains("6 треков") || !collectionMixStats.Text.Contains("18:00"))
            throw new InvalidOperationException("Mix header retained empty vertical space, lost its tags or clipped the playback controls.");
        VerifyMixNotchPaint();
        if (collectionMixShape.NotchBottom - collectionHero.Bounds.Y > 40)
            throw new InvalidOperationException("The mix tag cutout is deeper than the top-aligned badges require.");
        MoveActionPointer(new Point(2, 2));
        await WaitForLikedLayoutAsync(() => ((MaterialTrackButton)collectionMixPlay).GlassAccentSurface == FixtureMixBadgeColor(1) &&
            collectionMixPlay.Background == FixtureMixBadgeColor(1));
        VerifyMixBadgeColors();
        VerifyMixGlassPaint();
        if (Environment.GetEnvironmentVariable("POOKIE_GLASS_PERF") == "1") await ProbeMixGlassPerformanceAsync();
        CaptureUiPreview("mix-detail-narrow");
        ClickHomeElement(profileButton);
        await WaitForLikedLayoutAsync(() => profileOpen.Value &&
            shellMenus.Single(menu => ReferenceEquals(menu.State, profileOpen)).Layer.Reveal > .999);
        CaptureUiPreview("mix-profile-glass");
        CloseShellMenus(true);
        collectionFilter.Text = "Тихая гавань";
        if (pageItems[collectionTrackList].DataCount != 1 || activeCollection?.Items.Length != transport.Recent.Length)
            throw new InvalidOperationException("Mix filtering changed its full source track list.");
        collectionFilter.Text = "";
        ClickHomeElement(collectionMixPlay);
        await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == mixes[0].Key);
        if (playbackQueue.Snapshot.Source.Count != transport.Recent.Length)
            throw new InvalidOperationException("The mix playback button did not use the complete mix queue.");
        await WaitForLikedLayoutAsync(() => collectionMixPlayLabel.Text == "Слушать");
        var listenWidth = collectionMixPlay.ActualWidth;
        current = playbackQueue.Current!.Track; isPlaying.Value = true; RefreshCollectionPlayback();
        if (collectionMixPlayLabel.Text != "Пауза") throw new InvalidOperationException("Mix playback feedback did not update.");
        var widths = new List<double>();
        for (var i = 0; i < 10; i++) { await Task.Delay(30, lifetime.Token); widths.Add(collectionMixPlay.ActualWidth); }
        var pauseWidth = collectionMixPlay.ActualWidth;
        if (pauseWidth >= listenWidth - 1 || !widths.Any(width => width < listenWidth - .5 && width > pauseWidth + .5))
            throw new InvalidOperationException("The mix playback button snapped to its shorter label instead of animating its width.");
        CaptureUiPreview("mix-button-paused");
        isPlaying.Value = false; RefreshCollectionPlayback();
        await Task.Delay(50, lifetime.Token);
        isPlaying.Value = true; RefreshCollectionPlayback();
        await WaitForLikedLayoutAsync(() => Math.Abs(collectionMixPlay.ActualWidth - pauseWidth) < .5);
        if (((MaterialTrackButton)collectionMixPlay).GlassAccentSurface != FixtureMixBadgeColor(1))
            throw new InvalidOperationException("The mix accent changed when switching between play and pause.");
        current = null; isPlaying.Value = false; playbackQueue.Clear(); RefreshCollectionPlayback();

        await MoveNavigationAsync(-1);
        await WaitForLikedLayoutAsync(() => page.Value == Page.LibraryMixes && libraryTabs.IsVisible && collectionGrid.ItemsSource.Count == 9);
        Window.WindowSize = WindowSize.Resizable(1400, 980, minWidth: 1000, minHeight: 680);
        await OpenLibraryItemAsync(mixes[0]);
        await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth is >= 144 and <= 160 && collectionBackdrop.Opacity == 1 &&
            !collectionLoadingView.Skeleton.IsVisible && shellPageScroll.IsReady);
        await Task.Delay(260, lifetime.Token);
        CaptureUiPreview("mix-detail-wide");
        await NavigateAsync(Page.Library);
        await WaitForLikedLayoutAsync(() => libraryMixItems.Length == 9 && libraryMixShelf.Scroll.ViewportWidth > 100 && !libraryMixLoading.Loading);
        if (!libraryTabs.IsVisible || !libraryMixItems.Select(item => item.Key).SequenceEqual(mixes.Select(item => item.Key)))
            throw new InvalidOperationException("Library overview did not bind every mix in a single shelf.");
        shellScroll.SetScrollOffsets(0, Math.Max(0, shellScroll.VerticalOffset + libraryMixShelf.Root.Bounds.Y - startupHeader.Bounds.Bottom - 80));
        await WaitForLoginFrameAsync();
        CaptureUiPreview("library-mixes-overview");

        libraryPages.Remove("mixes"); libraryLoadedAt.Remove("mixes"); overviewReady.Remove("mixes");
        transport.FailMixes = true;
        try { await ShowLibrarySectionAsync(Page.LibraryMixes); }
        catch (Exception error) when (error is HttpRequestException or Pookie.SoundCloud.SoundCloudException) { }
        if (!librarySectionRetry.Value) throw new InvalidOperationException("Failed library mixes have no retry action.");
        transport.FailMixes = false;
        await RetryLibrarySectionAsync();
        await WaitForLikedLayoutAsync(() => collectionGrid.ItemsSource.Count == 9 && !collectionLoadingView.Loading);
        if (librarySectionRetry.Value) throw new InvalidOperationException("Library mix retry did not recover.");
        await NavigateHomeAsync();
        shellScroll.SetScrollOffsets(0, 0);
        await WaitForLoginFrameAsync();
        Console.WriteLine("LIBRARY_MIXES_OK: all personal mixes, overview shelf, detail actions, playback, filtering, back and retry");
    }

    private async Task ProbeMixGlassPerformanceAsync()
    {
        var frames = new List<double>(); var gaps = new List<double>();
        long last = 0, index = -1, openedAt = 0;
        var openings = new List<double>();
        var menuTiming = true;
        var menu = shellMenus.Single(value => ReferenceEquals(value.State, profileOpen)).Layer;
        void Sample()
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var stats = Window.LastFramePerformanceStats;
            if (stats.FrameIndex == index) return;
            index = stats.FrameIndex;
            if (openedAt != 0 && menu.Reveal > 0)
            { openings.Add(System.Diagnostics.Stopwatch.GetElapsedTime(openedAt, now).TotalMilliseconds); openedAt = 0; }
            // Idle time between completed animations isn't a missed frame.
            if (menuTiming && menu.Reveal is <= 0 or >= 1) { last = 0; return; }
            if (last != 0) gaps.Add(System.Diagnostics.Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            last = now; frames.Add(stats.FrameMs);
        }
        void Report(string phase)
        {
            var times = frames.Order().ToArray(); var intervals = gaps.Order().ToArray();
            Console.WriteLine($"GLASS_PERF {phase}: frames={times.Length}, render_p95={times[(int)((times.Length - 1) * .95)]:F2}ms, render_max={times[^1]:F2}ms, gap_p95={intervals[(int)((intervals.Length - 1) * .95)]:F2}ms, gap_max={intervals[^1]:F2}ms");
            frames.Clear(); gaps.Clear(); last = 0;
        }
        Window.FrameRendered += Sample;
        var mode = collectionMode.Value;
        try
        {
            // Include the first opening and subsequent reversals with no preview
            // readbacks in the timed region.
            for (var i = 0; i < 5; i++)
            {
                openedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                profileOpen.Value = true; await Task.Delay(300, lifetime.Token);
                profileOpen.Value = false; await Task.Delay(300, lifetime.Token);
            }
            Report("menus");
            Console.WriteLine($"GLASS_PERF menu-first-paint: cold={openings[0]:F2}ms, max={openings.Max():F2}ms");
            menuTiming = false;
            collectionMode.Value = TrackViewMode.Waveform;
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) > 100);
            frames.Clear(); gaps.Clear(); last = 0;
            var motion = smoothScrolls[shellScroll];
            for (var i = 0; i < 160; i++)
            {
                motion.Scroll(i % 80 < 40 ? 18 : -18);
                await Task.Delay(16, lifetime.Token);
            }
            Report("mix-scroll");
            motion.Stop(); shellScroll.SetScrollOffsets(0, 0);
        }
        finally { Window.FrameRendered -= Sample; CloseShellMenus(true); collectionMode.Value = mode; }
    }

    private void VerifyMixBadgeColors()
    {
        for (var index = 1; index <= 7; index++)
        {
            var source = collectionImages[HomeFixtureTransport.MixArtwork(index)];
            var palette = MixBadgePalette.FromArtwork(source);
            if (palette?.Surface != FixtureMixBadgeColor(index))
                throw new InvalidOperationException("Mix button colour came from the artwork instead of the bottom MIX label.");
            if (index == 4 && palette.Foreground != Color.White || index == 1 && palette.Foreground != Color.FromRgb(0, 0, 0))
                throw new InvalidOperationException("Mix badge contrast did not adapt to light/dark label colours.");
        }
        if (Environment.GetEnvironmentVariable("POOKIE_MIX_BADGE_REFERENCE") is { Length: > 0 } file)
        {
            using var reference = ImageSource.FromFile(file);
            var palette = MixBadgePalette.FromArtwork(reference);
            if (palette == null || palette.Surface.B < palette.Surface.R + 70 || palette.Surface.G < palette.Surface.R + 25)
                throw new InvalidOperationException("The supplied MIX label's blue background was not recovered.");
        }
        Console.WriteLine("MIX_BUTTON_OK: sampled MIX label colours, contrasting ink and interruptible width changes");
    }

    private void VerifyMixGlassPaint()
    {
        var bounds = collectionMixPlay.Bounds;
        var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(-bounds.X, -bounds.Y);
        collectionMixPlay.Render(context);
        context.EndFrame();
        var cpu = (Aprillz.MewUI.Rendering.ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var x = (int)(bounds.Width / 2 * scale);
        var body = (int)(9 * scale) * cpu.StrideBytes + x * 4;
        var upperRim = (int)(.8 * scale) * cpu.StrideBytes + x * 4;
        var lowerRim = (int)((bounds.Height - 1.4) * scale) * cpu.StrideBytes + x * 4;
        var pigment = FixtureMixBadgeColor(1);
        if (pixels[body + 3] != 255 || Math.Abs(pixels[body + 1] - pigment.G) > 3 ||
            Math.Abs(pixels[body + 2] - pigment.R) > 3 ||
            pixels[upperRim] < pixels[body] + 20 || pixels[lowerRim] < pixels[body] + 5)
            throw new InvalidOperationException("Filled mix glass lost its opaque pigment or the distinct upper and lower rim reflections.");
        Console.WriteLine("MIX_GLASS_OK: opaque MIX pigment with directional rim reflections");
    }

    private void VerifyMixNotchPaint()
    {
        var bounds = collectionHero.Bounds;
        var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(-bounds.X, -bounds.Y);
        collectionHeroFrame.Render(context);
        context.EndFrame();
        var cpu = (Aprillz.MewUI.Rendering.ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var outsideX = (int)((collectionMixTags.Bounds.X - 8 - bounds.X) * scale);
        var outsideY = (int)((collectionMixTags.Bounds.Center.Y - bounds.Y) * scale);
        var insideX = (int)((collectionMixTags.Bounds.Center.X - bounds.X) * scale);
        var insideY = (int)((collectionMixShape.NotchBottom + 24 - bounds.Y) * scale);
        if (pixels[outsideY * cpu.StrideBytes + outsideX * 4 + 3] != 0 ||
            pixels[insideY * cpu.StrideBytes + insideX * 4 + 3] != 255 || collectionHeroShadow.DrawShadow)
            throw new InvalidOperationException("Mix tags need a transparent cutout above the curved card background.");
    }
}
