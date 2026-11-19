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
            collectionCover.ActualWidth is >= 224 and <= 280 && collectionTitle.Bounds.Right < collectionCover.Bounds.X && collectionBackdrop.Opacity == 1);
        if (libraryTabs.IsVisible || collectionHero.HeroArtworkLeading || collectionHero.HeroFitContent ||
            !collectionPlay.IsVisible || !collectionPlay.IsEnabled || collectionSocialActions.IsVisible || collectionFollow.IsVisible ||
            collectionMore.IsVisible || collectionTitle.FontSize != 28 || collectionTitle.Height != 36 || !collectionStats.IsVisible)
            throw new InvalidOperationException("Mix detail did not use the track page's shared header layout.");
        VerifyCollectionArtworkLayout();
        if (collectionPlay.Bounds.Right >= collectionTitle.Bounds.X || collectionTags.Text != "#Electronic  #Alternative Rock  #ambient" ||
            collectionMeta.Text != "Исполнитель 1, Исполнитель 2, Исполнитель 3" || collectionDescriptionHost.IsVisible ||
            collectionCount.Text != "6" || collectionCountLabel.Text != "Треков" || collectionDuration.Text != "18:00")
            throw new InvalidOperationException("The mix lost its title, tags, track count or duration.");
        if (collectionPlay.ActualWidth != 64 || collectionPlay.ActualHeight != 64 ||
            collectionPlay.StyleName != TrackButtons.Glass || ((MaterialTrackButton)collectionPlay).GlassAccentSurface != null)
            throw new InvalidOperationException("Mix playback must use the shared neutral round glass button.");
        VerifyMixActionAlignment();
        MoveActionPointer(new Point(2, 2));
        if (Environment.GetEnvironmentVariable("POOKIE_GLASS_PERF") == "1") await ProbeMixGlassPerformanceAsync();
        CaptureUiPreview("mix-detail-narrow");
        await VerifyMixSharingAsync(transport);
        var originalMix = displayedCollection!;
        var longMix = originalMix with { Title = "Музыка для долгих поездок, встреч с друзьями и тихих вечеров",
            Playlist = originalMix.Playlist! with { Genre = "Очень длинное название жанра", Tags = ["Ещё одно длинное название", "Третий музыкальный жанр"] } };
        SetCollectionPresentation(longMix);
        await WaitForLikedLayoutAsync(() => collectionTitle.ActualHeight == 36 && shellPageScroll.IsReady && collectionTitle.Text == longMix.Title);
        VerifyCollectionArtworkLayout();
        CaptureUiPreview("mix-detail-long-title");
        SetCollectionPresentation(originalMix);
        await WaitForLikedLayoutAsync(() => collectionTitle.ActualHeight < 60 && shellPageScroll.IsReady);
        ClickHomeElement(profileButton);
        await WaitForLikedLayoutAsync(() => profileOpen.Value &&
            shellMenus.Single(menu => ReferenceEquals(menu.State, profileOpen)).Layer.Reveal > .999);
        CaptureUiPreview("mix-profile-glass");
        CloseShellMenus(true);
        collectionFilter.Text = "Тихая гавань";
        await WaitForLikedLayoutAsync(() => pageItems[collectionTrackList].DataCount == 1);
        if (activeCollection?.Items.Length != transport.Recent.Length)
            throw new InvalidOperationException("Mix filtering changed its full source track list.");
        collectionFilter.Text = "";
        await WaitForLikedLayoutAsync(() => pageItems[collectionTrackList].DataCount == transport.Recent.Length);
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
        await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth is >= 224 and <= 280 && collectionBackdrop.Opacity == 1 &&
            !collectionLoadingView.Skeleton.IsVisible && shellPageScroll.IsReady);
        await Task.Delay(260, lifetime.Token);
        VerifyCollectionArtworkLayout();
        CaptureUiPreview("mix-detail-wide");
        VerifyMixActionAlignment();
        foreach (var personal in mixes.Where(mix => mix.Playlist!.Urn.Contains(":new-for-you:") || mix.Playlist.Urn.Contains(":weekly:")))
        {
            await OpenLibraryItemAsync(personal);
            await WaitForLikedLayoutAsync(() => collectionBackdrop.Opacity == 1 && !collectionLoadingView.Skeleton.IsVisible);
            if (!collectionPlay.IsVisible || !collectionStats.IsVisible || collectionHero.HeroArtworkLeading ||
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
            await WaitForLikedLayoutAsync(() => collectionCover.ActualWidth is >= 224 and <= 280 && shellPageScroll.IsReady &&
                !collectionLoadingView.Skeleton.IsVisible && collectionBackdrop.Opacity == 1);
            if (collectionHero.HeroArtworkLeading || collectionTitle.FontSize != 28 || !collectionPlay.IsVisible ||
                !collectionStats.IsVisible || !collectionTags.IsVisible ||
                collectionTitle.Bounds.Right >= collectionCover.Bounds.X)
                throw new InvalidOperationException("Ordinary playlist did not share the mix presentation.");
            VerifyCollectionArtworkLayout();
            CaptureUiPreview("playlist-after-mix");
            SetCollectionPresentation(regular with { Title = "Музыка для долгой дороги и прогулок по вечернему городу с друзьями" });
            await WaitForLikedLayoutAsync(() => collectionTitle.ActualHeight > 45 && !collectionTitleBlock.IsArrangeDirty);
            VerifyCollectionArtworkLayout();
            var avatarUrl = HomeFixtureTransport.MixArtwork(2);
            var coverless = regularTransport.Playlist with { ArtworkUrl = null, User = regularTransport.Playlist.User! with { AvatarUrl = avatarUrl } };
            SetCollectionPresentation(LibraryData.FromPlaylist(coverless));
            await WaitForLikedLayoutAsync(() => ReferenceEquals(collectionCover.Source, collectionImages[avatarUrl]));
            VerifyCollectionArtworkLayout();
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

    private async Task VerifyMixSharingAsync(HomeFixtureTransport transport)
    {
        var mix = displayedCollection!;
        var messageTab = shareSectionTabs[1];
        bool DestinationsFadingWithTab() => shareDestinationsPanel.IsVisible && messageTab.IsVisible &&
            messageTab.Opacity is > 0 and < 1 && shareDestinationsPanel.Opacity == messageTab.Opacity;
        ClickHomeElement(collectionShare);
        await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running && !shareContent.Resizing &&
            sharePreview.Opacity == 1 && !mixShareLoading && shareAccessButton.IsEnabled);
        if (mixLockOpen.Opacity != 0 || mixLockClosed.Opacity != 1 || !mixLinkPlaceholder.IsVisible || shareLink.Text.Length != 0 || shareCopy.IsEnabled ||
            shareDestinationsPanel.IsVisible || !sharePrivatePanel.IsVisible || !shareAccessButton.IsVisible || shareResetIcon.IsVisible ||
            shareSectionTabs[1].IsVisible || shareSectionTabs[1].IsEnabled || shareSectionTabs[2].IsVisible || transport.MixPrivacyWrites != 0)
            throw new InvalidOperationException("Opening a private mix exposed sharing or changed its privacy.");
        var field = shareLinkGlass.Bounds;
        if (!shareLinkGlass.IsAncestorOf(shareAccessButton) || !field.Contains(shareAccessButton.Bounds) ||
            mixLinkPlaceholder.Bounds.X > field.X + 17 || mixLinkPlaceholder.Bounds.Right > shareAccessButton.Bounds.Left ||
            shareCopy.Bounds.X <= field.Right || sharePrivatePanel.Bounds.Y < field.Bottom ||
            Math.Abs(sharePrivatePanel.Bounds.X - field.X) > 1 || sharePrivateTitle.Text != "Приватный микс")
            throw new InvalidOperationException("Private mix controls do not follow the private playlist link layout.");
        await WaitForLikedLayoutAsync(() => !shareContent.Animating && !shareDialog.Running);
        await WaitForLoginFrameAsync();
        CaptureUiPreview("mix-share-private");
        var header = startupHeader.NavigationCard;
        var painted = header.PopupBackdropPaintVersion;
        await WaitForLoginFrameAsync();
        if (contentSurface.CacheMode == null || header.PopupBackdropPaintVersion != painted)
            throw new InvalidOperationException("A stationary page was repainted into the header while sharing.");
        contentSurface.InvalidateVisual();
        await WaitForLikedLayoutAsync(() => header.PopupBackdropPaintVersion > painted);
        async Task OpenConfirmation(ShareAccessAction action)
        {
            await WaitForLikedLayoutAsync(() => shareAccessPopup?.IsVisible != true && !shareContent.Resizing);
            var bounds = shareContent.Bounds;
            RouteWaveformClick(SettingsCenter(shareAccessButton));
            await WaitForLikedLayoutAsync(() => shareAccessPopup?.Reveal == 1 && shareAccessCancel.IsFocused);
            if (shareAccessAction != action || shareContent.Bounds != bounds ||
                shareAccessLabel.Text != (action == ShareAccessAction.PublishMix ? "Открыть" : "Закрыть ссылку"))
                throw new InvalidOperationException("Mix link confirmation has the wrong action or resized the share dialog.");
        }
        await OpenConfirmation(ShareAccessAction.PublishMix);
        CaptureUiPreview("mix-open-confirm", shareAccessPopup);
        RouteShareKey(Key.Enter);
        if (transport.MixPrivacyWrites != 0 || shareCopy.IsEnabled || shareDestinationsPanel.IsVisible || !shareDialog.IsOpen)
            throw new InvalidOperationException("Cancelling public access changed the mix.");
        await OpenConfirmation(ShareAccessAction.PublishMix);
        transport.MixWriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RouteWaveformClick(SettingsCenter(shareAccessConfirm));
        if (shareCopy.IsEnabled || shareAccessButton.IsEnabled || transport.MixPrivacyWrites != 1 || mixLockClosed.Opacity != 1)
            throw new InvalidOperationException("Mix link was usable before the privacy change was confirmed.");
        await VerifyAccessResizeAsync(opening: true);
        await WaitForLikedLayoutAsync(() => !mixShareLoading && shareCopy.IsEnabled && shareAccessButton.IsEnabled && mixLockOpen.Opacity == 1);
        if (mixLockClosed.Opacity != 0 || shareLink.Text != "https://soundcloud.com/discover/sets/your-moods:42:1" ||
            !shareSectionTabs[1].IsEnabled || !shareDestinationsPanel.IsVisible || shareDestinations.Any(button => !button.IsEnabled) || shareSectionTabs[2].IsVisible)
            throw new InvalidOperationException("Reopening did not restore public sharing for the same mix.");
        await WaitForLikedLayoutAsync(() => !shareDialog.Running && !shareContent.Resizing && sharePreview.Opacity == 1);
        if (mixLinkPlaceholder.IsVisible || sharePrivateTitle.Text != "Публичный микс" ||
            sharePrivatePanel.Bounds.Y < shareLinkRow.Bounds.Bottom)
            throw new InvalidOperationException("Public mix link and explanation are out of order.");
        await WaitForLikedLayoutAsync(() => !shareContent.Animating && !shareDialog.Running);
        await WaitForLoginFrameAsync();
        CaptureUiPreview("mix-share-public");
        transport.MixWriteGate = null;
        transport.FailMixPrivacy = true;
        await OpenConfirmation(ShareAccessAction.HideMix);
        CaptureUiPreview("mix-close-confirm", shareAccessPopup);
        RouteShareKey(Key.Escape);
        if (transport.MixPrivacyWrites != 1 || !shareCopy.IsEnabled)
            throw new InvalidOperationException("Dismissed confirmation revoked the public link.");
        await OpenConfirmation(ShareAccessAction.HideMix);
        RouteShareKey(Key.Tab); RouteShareKey(Key.Enter);
        await WaitForLikedLayoutAsync(() => shareAccessButton.IsEnabled && shareMessage.IsVisible);
        if (transport.MixPrivacyWrites != 2 || !shareCopy.IsEnabled || mixLockOpen.Opacity != 1 || !messageTab.IsVisible || messageTab.Opacity != 1 || !shareDestinationsPanel.IsVisible || shareDestinations.Any(button => !button.IsEnabled))
            throw new InvalidOperationException("Failed privacy change did not restore the public mix state.");
        transport.FailMixPrivacy = false;
        await OpenConfirmation(ShareAccessAction.HideMix);
        transport.MixWriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RouteWaveformClick(SettingsCenter(shareAccessConfirm));
        if (!messageTab.IsVisible || messageTab.Opacity != 1 || messageTab.IsEnabled || shareDestinationsPanel.Opacity != 1 || shareDestinations.Any(button => button.IsEnabled))
            throw new InvalidOperationException("Pending revocation changed the confirmed tab visibility or allowed sending.");
        await VerifyAccessResizeAsync(opening: false);
        if (messageTab.IsVisible || messageTab.IsEnabled || shareDestinationsPanel.IsVisible)
            throw new InvalidOperationException("Revocation retained public sharing controls.");
        await OpenConfirmation(ShareAccessAction.PublishMix);
        transport.MixWriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RouteWaveformClick(SettingsCenter(shareAccessConfirm));
        CloseShare(true); OpenShare(mix);
        if (shareCopy.IsEnabled) throw new InvalidOperationException("Reopening exposed a pending link.");
        transport.MixWriteGate.SetResult();
        await WaitForLikedLayoutAsync(() => !mixShareLoading && shareCopy.IsEnabled && messageTab.IsVisible && messageTab.Opacity == 1);
        if (!messageTab.IsEnabled || !transport.MixPublic || transport.MixPrivacyWrites != 4)
            throw new InvalidOperationException("Reopening the mix lost the confirmed sharing state or replayed a write.");
        // Reverse a committed close while the row and the card are still moving.
        await WaitForLikedLayoutAsync(() => !shareDialog.Running && !shareContent.Resizing);
        var publicHeight = shareContent.BodyBounds.Height;
        transport.MixWriteGate = null;
        ToggleShareAccessMenu(); ConfirmShareAccess();
        await WaitForLikedLayoutAsync(() => DestinationsFadingWithTab() && shareContent.BodyBounds.Height < publicHeight - 2);
        var partialOpacity = messageTab.Opacity;
        var partialHeight = shareContent.BodyBounds.Height;
        ToggleShareAccessMenu(); ConfirmShareAccess();
        if (Math.Abs(messageTab.Opacity - partialOpacity) > .01 || Math.Abs(shareContent.BodyBounds.Height - partialHeight) > 1)
            throw new InvalidOperationException("Reversing mix access restarted the transition from an endpoint.");
        await WaitForLikedLayoutAsync(() => !shareActionsFade.IsRunning && !shareContent.Resizing && shareCopy.IsEnabled);
        if (!transport.MixPublic || !shareDestinationsPanel.IsVisible || messageTab.Opacity != 1 ||
            Math.Abs(shareContent.BodyBounds.Height - publicHeight) > 1 || transport.MixPrivacyWrites != 6)
            throw new InvalidOperationException("Interrupted mix resize settled at the wrong height or access state.");
        CloseShare(true);
        transport.MixListener = 77; transport.MixPublic = true;
        OpenShare(mix);
        await WaitForLikedLayoutAsync(() => !mixShareLoading && shareCopy.IsEnabled);
        if (shareAccessButton.IsVisible) throw new InvalidOperationException("Another listener can change the mix privacy.");
        CloseShare(true);
        transport.MixListener = 42; transport.MixPublic = false;
        if (contentSurface.CacheMode != null) throw new InvalidOperationException("Closed mix dialog retained the page bitmap.");
        Console.WriteLine("MIX_SHARING_OK");

        async Task VerifyAccessResizeAsync(bool opening)
        {
            // Isolate the privacy acknowledgement from popup dismissal and previous error feedback.
            await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareAccessPopup?.IsVisible != true);
            var samples = new List<(double Height, double Opacity, long Time)>();
            var backdrop = shareDialog.BackdropVersion;
            var allocations = shareDialog.OpticalAllocations;
            var fadedTogether = false;
            var lockFaded = false;
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Frame()
            {
                samples.Add((shareContent.BodyBounds.Height, shareDestinationsPanel.Opacity, System.Diagnostics.Stopwatch.GetTimestamp()));
                fadedTogether |= DestinationsFadingWithTab();
                lockFaded |= mixLockOpen.Opacity is > 0 and < 1 && mixLockClosed.Opacity is > 0 and < 1;
                if (!mixShareLoading && !shareActionsFade.IsRunning && !shareContent.Resizing && shareDestinationsPanel.IsVisible == opening)
                    settled.TrySetResult();
            }
            Frame();
            Window.FrameRendered += Frame;
            try
            {
                transport.MixWriteGate!.SetResult();
                await settled.Task.WaitAsync(TimeSpan.FromSeconds(4), lifetime.Token);
            }
            finally { Window.FrameRendered -= Frame; }

            var start = samples[0].Height;
            var end = samples[^1].Height;
            var direction = opening ? 1 : -1;
            var steps = samples.Zip(samples.Skip(1), (a, b) => direction * (b.Height - a.Height)).ToArray();
            var distinct = samples.Select(f => f.Height).Distinct().Count();
            var finalOpacity = opening ? 1 : 0;
            var late = samples.Where(f => f.Opacity == finalOpacity).Max(f => Math.Abs(end - f.Height));
            var gaps = samples.Zip(samples.Skip(1), (a, b) => System.Diagnostics.Stopwatch.GetElapsedTime(a.Time, b.Time).TotalMilliseconds).Order().ToArray();
            Console.WriteLine($"MIX_ACCESS_RESIZE open={opening} height={start:F1}->{end:F1} sizes={distinct} late={late:F2}dip p95={gaps[(int)((gaps.Length - 1) * .95)]:F2}ms max={gaps[^1]:F2}ms backdrops={shareDialog.BackdropVersion - backdrop} optical_allocations={shareDialog.OpticalAllocations - allocations}");
            if (!fadedTogether || !lockFaded || distinct < 4 || direction * (end - start) < 24 ||
                steps.Min() < -1 || late > 2 || shareDialog.BackdropVersion != backdrop || shareDialog.OpticalAllocations != allocations)
                throw new InvalidOperationException("Mix access resize reversed, jumped after fading, or rebuilt its glass surface.");
        }
    }

    private void VerifyCollectionArtworkLayout()
    {
        VerifyDetailHeadingCentered(collectionPlay, collectionTitle, collectionMetadataLine);
        foreach (var label in new[] { collectionAuthor, collectionMeta })
            if (label.FontSize != trackDetailAuthor.FontSize || label.FontWeight != trackDetailAuthor.FontWeight ||
                label.FontFamily != trackDetailAuthor.FontFamily)
                throw new InvalidOperationException("Collection subtitle typography differs from the track subtitle.");
        var cover = collectionCover.Bounds;
        var circle = collectionStats.Bounds;
        if (collectionDuration.IsVisible && (Math.Abs((collectionCountLabel.Bounds.Top - collectionCount.Bounds.Bottom) -
                (collectionDuration.Bounds.Top - collectionCountLabel.Bounds.Bottom - 2)) > 1 || collectionDuration.Opacity >= collectionCountLabel.Opacity))
            throw new InvalidOperationException("The collection duration lost its spacing or secondary contrast.");
        if (Math.Abs(cover.Width - cover.Height) > 1 || cover.Width <= 208 ||
            Math.Abs(circle.Width - 100) > 1 || circle.Width != circle.Height || collectionStats.CornerRadius != circle.Width / 2 ||
            circle.Right >= cover.X || Math.Abs(circle.Bottom - cover.Bottom) > 1 ||
            (collectionMore.IsVisible ? collectionMore : collectionMixQueue).Bounds.Right >= circle.X ||
            collectionStats.IsHitTestVisible || collectionTags.IsVisible && (Math.Abs(collectionTags.Bounds.Y - cover.Y) > 1 ||
                Math.Abs(collectionTags.Bounds.Right - circle.Right) > 1 || collectionTitle.Bounds.Right >= collectionTags.Bounds.X ||
                collectionTags.Bounds.Width > 169 || collectionTags.Bounds.Bottom >= circle.Y))
            throw new InvalidOperationException($"Collection artwork layout: tags={collectionTags.Bounds}, cover={cover}, stats={circle}, hero={collectionHero.Bounds}");
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
