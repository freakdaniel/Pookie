using Aprillz.MewUI.Controls;
using Pookie.Localization;
using Aprillz.MewUI;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool userProfileUiSmoke;
    private bool searchLinksUiSmoke;
    private bool feedUiSmoke;
    private bool chatsUiSmoke;
    private bool chatMusicUiSmoke;
    private bool chatMediaUiSmoke;
    private bool menusUiSmoke;
    private bool tooltipsUiSmoke;
    private bool chromeUiSmoke;
    private bool settingsUiSmoke;
    private bool queueContinuationUiSmoke;
    private bool chatNavigationUiSmoke, chatDrawerUiSmoke, chatEmojiUiSmoke;
    private bool chatVideoLiveUi;
    private string? expectedLanguage;
    private bool uiSmoke;
    private bool loginUiSmoke;
    private bool browserShutdownSmoke;
    private bool mediaUiSmoke;
    private bool bufferUiSmoke;
    private bool expandedUiSmoke, expandedGlassUiSmoke;
    private bool lyricsUiSmoke;
    private bool artworkUiSmoke;
    private bool scrollingUiSmoke;
    private bool trackPageUiSmoke, trackDescriptionUiSmoke, trackPlaybackUiSmoke, waveformUiSmoke;
    private bool trackPageLiveUi;
    private string? livePlaylistUrl;
    private bool likesEditsUiSmoke, likesFilterUiSmoke, collectionsUiSmoke, commentsUiSmoke, homeUiSmoke, libraryMixesUiSmoke;
    private bool webPlaylistEditingOnly, shareUiSmoke, playlistDialogUiSmoke;
    private bool playlistEditorLiveUi;
    private bool memoryUiSmoke, playbackMemoryUiSmoke;
    internal Exception? VerificationFailure { get; private set; }
    private DispatcherTimer? closeTimer;

    internal void ConfigureVerification(string[] args)
    {
        userProfileUiSmoke = args.Contains("--user-profile-ui-smoke-test");
        searchLinksUiSmoke = args.Contains("--search-links-ui-smoke-test");
        feedUiSmoke = args.Contains("--feed-ui-smoke-test");
        chatMusicUiSmoke = args.Contains("--chat-music-ui-smoke-test");
        chatMediaUiSmoke = args.Contains("--chat-media-ui-smoke-test");
        menusUiSmoke = args.Contains("--menus-ui-smoke-test");
        tooltipsUiSmoke = args.Contains("--tooltips-ui-smoke-test");
        chromeUiSmoke = args.Contains("--chrome-ui-smoke-test");
        settingsUiSmoke = args.Contains("--settings-ui-smoke-test");
        queueContinuationUiSmoke = args.Contains("--queue-continuation-ui-smoke-test");
        chatsUiSmoke = chatMusicUiSmoke || chatMediaUiSmoke || args.Contains("--chats-ui-smoke-test");
        chatDrawerUiSmoke = args.Contains("--chat-drawer-ui-smoke-test");
        chatNavigationUiSmoke = chatDrawerUiSmoke || args.Contains("--chat-navigation-ui-smoke-test");
        chatEmojiUiSmoke = args.Contains("--chat-emoji-ui-smoke-test");
        chatVideoLiveUi = args.Contains("--chat-video-live-ui-probe");
        memoryUiSmoke = args.Contains("--memory-ui-smoke-test");
        playbackMemoryUiSmoke = args.Contains("--playback-memory-ui-smoke-test");
        var languageIndex = Array.IndexOf(args, "--language-ui-smoke-test");
        if (languageIndex >= 0)
            expectedLanguage = args[languageIndex + 1];
        uiSmoke = args.Contains("--ui-smoke-test");
        loginUiSmoke = args.Contains("--login-ui-smoke-test");
        browserShutdownSmoke = args.Contains("--browser-shutdown-smoke-test");
        mediaUiSmoke = args.Contains("--media-ui-smoke-test");
        bufferUiSmoke = args.Contains("--buffer-ui-smoke-test");
        expandedGlassUiSmoke = args.Contains("--expanded-glass-ui-smoke-test");
        expandedUiSmoke = expandedGlassUiSmoke || args.Contains("--expanded-ui-smoke-test");
        lyricsUiSmoke = args.Contains("--lyrics-ui-smoke-test");
        artworkUiSmoke = args.Contains("--artwork-ui-smoke-test");
        scrollingUiSmoke = args.Contains("--scrolling-ui-smoke-test");
        trackDescriptionUiSmoke = args.Contains("--track-description-ui-smoke-test");
        trackPlaybackUiSmoke = args.Contains("--track-playback-ui-smoke-test");
        waveformUiSmoke = args.Contains("--waveform-ui-smoke-test");
        trackPageUiSmoke = trackDescriptionUiSmoke || trackPlaybackUiSmoke || waveformUiSmoke || args.Contains("--track-page-ui-smoke-test");
        trackPageLiveUi = args.Contains("--track-page-live-ui-probe");
        if (args is ["--playlist-page-live-ui-probe", var playlistUrl]) livePlaylistUrl = playlistUrl;
        shareUiSmoke = args.Contains("--share-ui-smoke-test");
        playlistDialogUiSmoke = args.Contains("--playlist-dialog-ui-smoke-test");
        collectionsUiSmoke = shareUiSmoke || playlistDialogUiSmoke || args.Contains("--collections-ui-smoke-test");
        webPlaylistEditingOnly = args.Contains("--web-playlist-editing-only");
        playlistEditorLiveUi = args.Contains("--playlist-editor-live-ui-probe");
        likesEditsUiSmoke = args.Contains("--likes-edits-ui-smoke-test");
        likesFilterUiSmoke = args.Contains("--likes-filter-ui-smoke-test");
        commentsUiSmoke = args.Contains("--comments-ui-smoke-test");
        libraryMixesUiSmoke = args.Contains("--mixes-ui-smoke-test");
        homeUiSmoke = libraryMixesUiSmoke || args.Contains("--home-ui-smoke-test");
        if (args.Contains("--smoke-test"))
        {
            closeTimer = new DispatcherTimer(TimeSpan.FromSeconds(5));
            closeTimer.Tick += () => Window.Close();
        }
    }

    private async Task VerifyLanguageAsync(string expected)
    {
        try
        {
            if (System.Globalization.CultureInfo.CurrentUICulture.Name != expected)
                throw new InvalidOperationException($"Expected {expected}, got {System.Globalization.CultureInfo.CurrentUICulture.Name}.");
            await ShowSettingsAsync(SettingsSection.General);
            var selector = settingsChoices["language"];
            var tab = settingsTabs[SettingsSection.General].FindVisualChild<TextBlock>() as TextBlock
                ?? throw new InvalidOperationException("Missing settings tab caption.");
            var typed = new string(UiText.Get("header.library").AsSpan());
            topSearchInput.Text = typed;
            var entry = playbackQueue.StartContext(new("test", typed, ""), [new Pookie.SoundCloud.SoundCloudTrack { Id = 1, Title = typed }], 0);
            var history = navigationIndex;
            // Build and detach the startup dropdown in the original language.
            var startup = settingsChoices["startup"];
            startup.IsDropDownOpen = true;
            startup.IsDropDownOpen = false;
            await WaitForLoginFrameAsync();

            void VerifyVisible(string culture)
            {
                if (UiText.Language.Name != culture || topSearchInput.Placeholder != UiText.Get("search.input.placeholder") ||
                    tab.Text != UiText.Get("settings.general.title") || startup.SelectedText != UiText.Get("header.home"))
                    throw new InvalidOperationException($"Stale shell or settings text after selecting {culture}: {topSearchInput.Placeholder}, {tab.Text}, {startup.SelectedText}.");
                if (topSearchInput.Text != typed || playbackQueue.Current != entry || entry.Track.Title != typed ||
                    page.Value != Page.Settings || navigationIndex != history || settingsChoices["language"] != selector)
                    throw new InvalidOperationException("Changing language replaced the route, search text or queue entry.");
            }

            VerifyVisible(expected);
            await VerifyLanguageMenuAsync(selector);
            var headerWidths = new Dictionary<string, double>();
            var originalContext = ExecutionContext.Capture()!;
            for (var i = 0; i < UiText.Choices.Count; i++)
            {
                selector.SelectedIndex = i + 1;
                VerifyVisible(UiText.Choices[i].Code);
                Window.PerformLayout();
                VerifyHeaderCardLayout();
                var nav = topNavigation.Children.OfType<Button>().ToArray();
                for (var j = 1; j < nav.Length; j++)
                    if (Math.Abs(nav[j].Bounds.X - nav[j - 1].Bounds.Right - 8) > 1 / Window.DpiScale)
                        throw new InvalidOperationException($"Unexpected navigation gap in {UiText.Language}.");
                headerWidths[UiText.Language.Name] = topNavigation.ActualWidth;
                if (UiText.Language.Name is "sv" or "ru") CaptureUiPreview("header-" + UiText.Language.Name);
                var heading = UiText.Get("header.library");
                ExecutionContext.Run(originalContext, _ =>
                {
                    if (UiText.Get("header.library") != heading)
                        throw new InvalidOperationException("An older async context used the previous language.");
                }, null);
                startup.IsDropDownOpen = true;
                if ((startup.Menu!.Options[0].FindVisualChild<TextBlock>() as TextBlock)?.Text != UiText.Get("header.home"))
                    throw new InvalidOperationException("Stale cached dropdown label.");
                startup.IsDropDownOpen = false;
            }
            SaveConfiguration();
            await configuration.FlushAsync();
            if (headerWidths["sv"] >= headerWidths["ru"] - 10)
                throw new InvalidOperationException("The navigation card did not shrink for shorter Swedish labels.");
            if (configuration.Load().InterfaceLanguage != UiText.Choices[^1].Code)
                throw new InvalidOperationException("The selected language was not saved.");
            selector.SelectedIndex = 0;
            VerifyVisible(expected);
            SaveConfiguration();
            await configuration.FlushAsync();
            if (configuration.Load().InterfaceLanguage != "" || selector.SelectedText != UiText.Get("settings.language.system"))
                throw new InvalidOperationException("Could not restore the system language in the same window.");
            Console.WriteLine($"UI_LANGUAGE_OK: {expected}");
        }
        catch (Exception error) { VerificationFailure = error; }
        finally { Window.Close(); }
    }

    private async Task VerifyLanguageMenuAsync(MaterialSelect selector)
    {
        await RevealSettingsControlAsync(selector);
        selector.IsDropDownOpen = true;
        await WaitForSettingsLayoutAsync(() => selector.MenuReveal == 1);
        var menu = selector.Menu!;
        var scroll = (ScrollViewer)menu.FindVisualChild<ScrollViewer>()!;
        var bar = (ScrollBar)VisualTree.Find(scroll, e => e is ScrollBar { Orientation: Orientation.Vertical })!;
        if (!bar.IsVisible || menu.Bounds.Right - bar.Bounds.Right > 2)
            throw new InvalidOperationException("Language scrollbar is inset from the dropdown edge.");
        for (var i = 0; i < UiText.Choices.Count; i++)
        {
            var code = UiText.Choices[i].Code;
            var flag = Icons.CountryFlag(code switch { "en-US" => "us", "sv" => "se", _ => code });
            if (flag == null || VisualTree.Find(menu.Options[i + 1], e => e is Image icon && icon.Source == flag) == null)
                throw new InvalidOperationException($"Missing flag for {code}.");
        }
        CaptureUiPreview("language-dropdown", menu);
        var parentOffset = shellScroll.VerticalOffset;
        RouteSearchTestWheel(scroll.Bounds.Center, -3);
        if (!menu.Motion.IsRunning) throw new InvalidOperationException("Wheel input bypassed smooth dropdown scrolling.");
        await WaitForLikedLayoutAsync(() => scroll.VerticalOffset > 0 && !menu.Motion.IsRunning);
        var down = scroll.VerticalOffset;
        RouteSearchTestWheel(scroll.Bounds.Center, 1);
        await WaitForLikedLayoutAsync(() => scroll.VerticalOffset < down && !menu.Motion.IsRunning);
        if (!selector.IsDropDownOpen || shellScroll.VerticalOffset != parentOffset)
            throw new InvalidOperationException("Scrolling the dropdown moved its owner or dismissed it.");
        selector.IsDropDownOpen = false;
        await WaitForSettingsLayoutAsync(() => selector.MenuReveal == 0);

        UpdatePreferences(p => p with { SmoothScrolling = false });
        selector.IsDropDownOpen = true;
        await WaitForSettingsLayoutAsync(() => selector.MenuReveal == 1);
        var before = scroll.VerticalOffset;
        RouteSearchTestWheel(scroll.Bounds.Center, -1);
        if (menu.Motion.IsRunning || scroll.VerticalOffset <= before)
            throw new InvalidOperationException("The dropdown ignored the smooth scrolling preference.");
        selector.IsDropDownOpen = false;
        UpdatePreferences(p => p with { SmoothScrolling = true });
        await WaitForSettingsLayoutAsync(() => selector.MenuReveal == 0);
    }

    partial void OnWindowReady()
    {
        if (uiSmoke) StartStartupLayoutProbe();
        if (loginUiSmoke) Window.FrameRendered += SampleLoginTransition;
        closeTimer?.Start();
    }

    partial void OnInitialized()
    {
        // Shell execution can start Windows children with SW_HIDE. Native pointer checks need a visible window.
        if (OperatingSystem.IsWindows()) { ProbeShowWindow(Window.Handle, 9); Window.Activate(); }
        if (uiSmoke || expandedUiSmoke || playbackMemoryUiSmoke)
        {
            lyricsService.Dispose();
            lyricsService = new(new EmptyLyricsProvider(), Path.Combine(dataPaths.Cache, "FixtureLyrics"));
        }
        if (expectedLanguage != null) Run(() => VerifyLanguageAsync(expectedLanguage));
        if (userProfileUiSmoke) Run(VerifyUserProfilesAsync);
        if (searchLinksUiSmoke) Run(VerifySearchLinksUiAsync);
        if (feedUiSmoke) Run(VerifyFeedAsync);
        if (chatsUiSmoke) Run(VerifyChatsUiAsync);
        if (menusUiSmoke) Run(VerifyFloatingMenusAsync);
        if (tooltipsUiSmoke) Run(VerifyMaterialTooltipsAsync);
        if (chromeUiSmoke) Run(VerifyShellChromeAsync);
        if (settingsUiSmoke) Run(VerifySettingsUiAsync);
        if (queueContinuationUiSmoke) Run(VerifyQueueContinuationAsync);
        if (chatNavigationUiSmoke) Run(VerifyChatNavigationUiAsync);
        if (chatEmojiUiSmoke) Run(VerifyChatEmojiUiAsync);
        if (chatVideoLiveUi) Run(VerifyChatVideoLiveUiAsync);
        if (memoryUiSmoke) Run(VerifyArtworkLifetimeAsync);
        if (playbackMemoryUiSmoke) Run(VerifyBackgroundPlaybackMemoryAsync);
        if (uiSmoke) Run(VerifyUiAsync);
        if (loginUiSmoke) Run(VerifyLoginUiAsync);
        if (browserShutdownSmoke) Run(StartBrowserShutdownCheckAsync);
        if (mediaUiSmoke) Run(VerifySystemMediaAsync);
        if (bufferUiSmoke) Run(VerifyBufferUiAsync);
        if (expandedUiSmoke) Run(VerifyExpandedPlayerAsync);
        if (lyricsUiSmoke) Run(VerifyLyricsUiAsync);
        if (artworkUiSmoke) Run(VerifyPlayerArtworkAsync);
        if (scrollingUiSmoke) Run(VerifyScrollingUiAsync);
        if (trackPageUiSmoke) Run(VerifyTrackPageUiAsync);
        if (trackPageLiveUi) Run(VerifyTrackPageLiveUiAsync);
        if (livePlaylistUrl is { } playlistUrl) Run(() => VerifyPlaylistLiveUiAsync(playlistUrl));
        if (collectionsUiSmoke) Run(VerifyCollectionsUiAsync);
        if (playlistEditorLiveUi) Run(VerifyPlaylistEditorLiveAsync);
        if (likesEditsUiSmoke) Run(VerifyLikesEditsUiAsync);
        if (likesFilterUiSmoke) Run(VerifyLikesFilterUiAsync);
        if (commentsUiSmoke) Run(VerifyCommentsUiAsync);
        if (homeUiSmoke) Run(VerifyHomeUiAsync);
    }

    private sealed class EmptyLyricsProvider : Pookie.Lyrics.ILyricsProvider
    {
        public Task<Pookie.Lyrics.LyricsCandidate?> GetAsync(Pookie.Lyrics.LyricsQuery query, CancellationToken token) => Task.FromResult<Pookie.Lyrics.LyricsCandidate?>(null);
        public Task<Pookie.Lyrics.LyricsCandidate[]> SearchAsync(string title, string artist, CancellationToken token) => Task.FromResult(Array.Empty<Pookie.Lyrics.LyricsCandidate>());
    }

    partial void OnStartupTransitionCompleted()
    {
        if (uiSmoke) StopStartupLayoutProbe();
    }

    partial void OnDisposed()
    {
        Window.FrameRendered -= SampleLoginTransition;
        startupLayoutProbe?.Dispose();
        closeTimer?.Dispose();
    }
}
