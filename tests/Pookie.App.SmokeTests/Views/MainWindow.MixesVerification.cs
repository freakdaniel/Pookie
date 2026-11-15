using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

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
            collectionCover.ActualWidth is >= 176 and <= 208 && collectionTitle.Bounds.Right < collectionCover.Bounds.X && collectionBackdrop.Opacity == 1);
        if (libraryTabs.IsVisible || collectionHero.HeroArtworkLeading || collectionHero.HeroFitContent ||
            !collectionPlay.IsVisible || !collectionPlay.IsEnabled || collectionSocialActions.IsVisible || collectionFollow.IsVisible ||
            collectionMore.IsVisible || collectionTitle.FontSize != 28 || collectionTitle.Height != 36 || !collectionMixInfo.IsVisible)
            throw new InvalidOperationException("Mix detail did not use the track page's shared header layout.");
        if (Math.Abs(collectionHero.ActualHeight - collectionCover.ActualHeight - 48) > 1 ||
            Math.Abs(collectionPlay.Bounds.Y - collectionCover.Bounds.Y) > 1 ||
            collectionPlay.Bounds.Right >= collectionTitle.Bounds.X ||
            collectionMixTags.Bounds.Right >= collectionCover.Bounds.X || collectionMixTags.Children.Count != 3 ||
            Math.Abs(collectionShare.Bounds.Bottom - collectionCover.Bounds.Bottom) > 1 ||
            collectionMeta.Text != "Исполнитель 1, Исполнитель 2, Исполнитель 3" || collectionDescriptionHost.IsVisible ||
            !collectionMixStats.Text.Contains("6 треков") || !collectionMixStats.Text.Contains("18:00"))
            throw new InvalidOperationException("Mix title/play, right-hand cover, centered metadata or bottom actions are misaligned.");
        if (collectionPlay.ActualWidth != 64 || collectionPlay.ActualHeight != 64 ||
            collectionPlay.StyleName != TrackButtons.Glass || ((MaterialTrackButton)collectionPlay).GlassAccentSurface != null)
            throw new InvalidOperationException("Mix playback must use the shared neutral round glass button.");
        VerifyMixActionAlignment();
        MoveActionPointer(new Point(2, 2));
        if (Environment.GetEnvironmentVariable("POOKIE_GLASS_PERF") == "1") await ProbeMixGlassPerformanceAsync();
        CaptureUiPreview("mix-detail-narrow");
        var originalMix = displayedCollection!;
        var longMix = originalMix with { Title = "Музыка для долгих поездок, встреч с друзьями и тихих вечеров",
            Playlist = originalMix.Playlist! with { Genre = "Очень длинное название жанра", Tags = ["Ещё одно длинное название", "Третий музыкальный жанр"] } };
        SetCollectionPresentation(longMix);
        await WaitForLikedLayoutAsync(() => collectionTitle.ActualHeight == 36 && shellPageScroll.IsReady && collectionTitle.Text == longMix.Title);
        if (collectionMixTags.Bounds.Right >= collectionCover.Bounds.X ||
            collectionMixTags.Bounds.Bottom > collectionShare.Bounds.Y - 8 || collectionHero.ActualHeight > 450)
            throw new InvalidOperationException("Long mix title/tags overlap playback or overflow the narrow header.");
        CaptureUiPreview("mix-detail-long-title");
        SetCollectionPresentation(originalMix);
        await WaitForLikedLayoutAsync(() => collectionTitle.ActualHeight < 60 && shellPageScroll.IsReady);
        ClickHomeElement(profileButton);
        await WaitForLikedLayoutAsync(() => profileOpen.Value &&
            shellMenus.Single(menu => ReferenceEquals(menu.State, profileOpen)).Layer.Reveal > .999);
        CaptureUiPreview("mix-profile-glass");
        CloseShellMenus(true);
        collectionFilter.Text = "Тихая гавань";
        if (pageItems[collectionTrackList].DataCount != 1 || activeCollection?.Items.Length != transport.Recent.Length)
            throw new InvalidOperationException("Mix filtering changed its full source track list.");
        collectionFilter.Text = "";
        ClickHomeElement(collectionPlay);
        await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == mixes[0].Key);
        if (playbackQueue.Snapshot.Source.Count != transport.Recent.Length)
            throw new InvalidOperationException("The mix playback button did not use the complete mix queue.");
        var listenBounds = collectionPlay.Bounds;
        current = playbackQueue.Current!.Track; isPlaying.Value = true; RefreshCollectionPlayback();
        await WaitForLoginFrameAsync();
        if (TooltipHints.TextFor(collectionPlay) != "Приостановить микс" || collectionPlay.Bounds != listenBounds)
            throw new InvalidOperationException("Mix pause changed the button layout or lost its playback feedback.");
        CaptureUiPreview("mix-button-paused");
        isPlaying.Value = false; RefreshCollectionPlayback();
        await WaitForLoginFrameAsync();
        if (TooltipHints.TextFor(collectionPlay) != "Воспроизвести микс" ||
            ((MaterialTrackButton)collectionPlay).GlassAccentSurface != null || collectionPlay.Bounds != listenBounds)
            throw new InvalidOperationException("Mix playback changed the shared neutral button style.");
        current = null; isPlaying.Value = false; playbackQueue.Clear(); RefreshCollectionPlayback();

        await MoveNavigationAsync(-1);
        await WaitForLikedLayoutAsync(() => page.Value == Page.LibraryMixes && libraryTabs.IsVisible && collectionGrid.ItemsSource.Count == 9);
        Window.WindowSize = WindowSize.Resizable(1400, 980, minWidth: 1000, minHeight: 680);
        await OpenLibraryItemAsync(mixes[0]);
        await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth is >= 176 and <= 208 && collectionBackdrop.Opacity == 1 &&
            !collectionLoadingView.Skeleton.IsVisible && shellPageScroll.IsReady);
        await Task.Delay(260, lifetime.Token);
        CaptureUiPreview("mix-detail-wide");
        VerifyMixActionAlignment();
        foreach (var personal in mixes.Where(mix => mix.Playlist!.Urn.Contains(":new-for-you:") || mix.Playlist.Urn.Contains(":weekly:")))
        {
            await OpenLibraryItemAsync(personal);
            await WaitForLikedLayoutAsync(() => collectionBackdrop.Opacity == 1 && !collectionLoadingView.Skeleton.IsVisible);
            if (!collectionPlay.IsVisible || !collectionMixInfo.IsVisible || collectionHero.HeroArtworkLeading ||
                ((MaterialTrackButton)collectionPlay).GlassAccentSurface != null)
                throw new InvalidOperationException("Daily Drops/Weekly Wave did not share the neutral mix presentation.");
        }
        var regularTransport = new CollectionFixtureTransport();
        var regular = LibraryData.FromPlaylist(regularTransport.Playlist);
        collectionImages[CollectionArtworkUrl(regular)!] = collectionImages[HomeFixtureTransport.MixArtwork(1)];
        api.BrowserTransport = regularTransport;
        try
        {
            await OpenLibraryItemAsync(regular);
            await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth is >= 176 and <= 208 && shellPageScroll.IsReady &&
                !collectionLoadingView.Skeleton.IsVisible && collectionBackdrop.Opacity == 1);
            if (collectionHero.HeroArtworkLeading || collectionTitle.FontSize != 28 || !collectionPlay.IsVisible ||
                collectionMixInfo.IsVisible || collectionMixStats.IsVisible || collectionMixTags.IsVisible ||
                collectionTitle.Bounds.Right >= collectionCover.Bounds.X)
                throw new InvalidOperationException("Ordinary playlist retained the mix styling after navigation.");
            CaptureUiPreview("playlist-after-mix");
        }
        finally { api.BrowserTransport = transport; }
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

    private void VerifyMixActionAlignment()
    {
        if (Math.Abs(collectionShare.Bounds.X - collectionPlay.Bounds.X) > 1 ||
            Math.Abs(collectionMixQueue.Bounds.X - collectionShare.Bounds.Right - TrackButtons.ActionSpacing) > 1 ||
            Math.Abs(collectionMixQueue.Bounds.Y - collectionShare.Bounds.Y) > 1)
            throw new InvalidOperationException("Mix bottom actions moved away from the leading edge of the content column.");
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

}
