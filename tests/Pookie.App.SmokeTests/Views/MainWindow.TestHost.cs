using Aprillz.MewUI;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool userProfileUiSmoke;
    private bool feedUiSmoke;
    private bool chatsUiSmoke;
    private bool chatMusicUiSmoke;
    private bool chatMediaUiSmoke;
    private bool menusUiSmoke;
    private bool chromeUiSmoke;
    private bool settingsUiSmoke;
    private bool queueContinuationUiSmoke;
    private bool chatNavigationUiSmoke, chatEmojiUiSmoke;
    private bool chatVideoLiveUi;
    private bool uiSmoke;
    private bool loginUiSmoke;
    private bool browserShutdownSmoke;
    private bool mediaUiSmoke;
    private bool bufferUiSmoke;
    private bool expandedUiSmoke, expandedGlassUiSmoke;
    private bool lyricsUiSmoke;
    private bool artworkUiSmoke;
    private bool scrollingUiSmoke;
    private bool trackPageUiSmoke;
    private bool trackPageLiveUi;
    private bool likesEditsUiSmoke, collectionsUiSmoke, commentsUiSmoke, homeUiSmoke;
    private bool waveJournalUiSmoke, memoryUiSmoke, waveQueueUiSmoke, playbackMemoryUiSmoke;
    internal Exception? VerificationFailure { get; private set; }
    private DispatcherTimer? closeTimer;

    internal void ConfigureVerification(string[] args)
    {
        userProfileUiSmoke = args.Contains("--user-profile-ui-smoke-test");
        feedUiSmoke = args.Contains("--feed-ui-smoke-test");
        chatMusicUiSmoke = args.Contains("--chat-music-ui-smoke-test");
        chatMediaUiSmoke = args.Contains("--chat-media-ui-smoke-test");
        menusUiSmoke = args.Contains("--menus-ui-smoke-test");
        chromeUiSmoke = args.Contains("--chrome-ui-smoke-test");
        settingsUiSmoke = args.Contains("--settings-ui-smoke-test");
        queueContinuationUiSmoke = args.Contains("--queue-continuation-ui-smoke-test");
        chatsUiSmoke = chatMusicUiSmoke || chatMediaUiSmoke || args.Contains("--chats-ui-smoke-test");
        chatNavigationUiSmoke = args.Contains("--chat-navigation-ui-smoke-test");
        chatEmojiUiSmoke = args.Contains("--chat-emoji-ui-smoke-test");
        chatVideoLiveUi = args.Contains("--chat-video-live-ui-probe");
        waveQueueUiSmoke = args.Contains("--wave-queue-ui-smoke-test");
        memoryUiSmoke = args.Contains("--memory-ui-smoke-test");
        playbackMemoryUiSmoke = args.Contains("--playback-memory-ui-smoke-test");
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
        trackPageUiSmoke = args.Contains("--track-page-ui-smoke-test");
        trackPageLiveUi = args.Contains("--track-page-live-ui-probe");
        collectionsUiSmoke = args.Contains("--collections-ui-smoke-test");
        likesEditsUiSmoke = args.Contains("--likes-edits-ui-smoke-test");
        commentsUiSmoke = args.Contains("--comments-ui-smoke-test");
        homeUiSmoke = args.Contains("--home-ui-smoke-test");
        waveJournalUiSmoke = args.Contains("--wave-journal-ui-smoke-test");
        if (args.Contains("--smoke-test"))
        {
            closeTimer = new DispatcherTimer(TimeSpan.FromSeconds(5));
            closeTimer.Tick += () => Window.Close();
        }
    }

    partial void OnWindowReady()
    {
        if (uiSmoke) StartStartupLayoutProbe();
        if (loginUiSmoke) Window.FrameRendered += SampleLoginTransition;
        closeTimer?.Start();
    }

    partial void OnInitialized()
    {
        if (uiSmoke || expandedUiSmoke || playbackMemoryUiSmoke)
        {
            lyricsService.Dispose();
            lyricsService = new(new EmptyLyricsProvider(), Path.Combine(dataPaths.Cache, "FixtureLyrics"));
        }
        if (userProfileUiSmoke) Run(VerifyUserProfilesAsync);
        if (feedUiSmoke) Run(VerifyFeedAsync);
        if (chatsUiSmoke) Run(VerifyChatsUiAsync);
        if (menusUiSmoke) Run(VerifyFloatingMenusAsync);
        if (chromeUiSmoke) Run(VerifyShellChromeAsync);
        if (settingsUiSmoke) Run(VerifySettingsUiAsync);
        if (queueContinuationUiSmoke) Run(VerifyQueueContinuationAsync);
        if (chatNavigationUiSmoke) Run(VerifyChatNavigationUiAsync);
        if (chatEmojiUiSmoke) Run(VerifyChatEmojiUiAsync);
        if (chatVideoLiveUi) Run(VerifyChatVideoLiveUiAsync);
        if (waveQueueUiSmoke) Run(VerifyWaveQueueUiAsync);
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
        if (collectionsUiSmoke) Run(VerifyCollectionsUiAsync);
        if (likesEditsUiSmoke) Run(VerifyLikesEditsUiAsync);
        if (commentsUiSmoke) Run(VerifyCommentsUiAsync);
        if (homeUiSmoke) Run(VerifyHomeUiAsync);
        if (waveJournalUiSmoke) Run(VerifyWaveJournalOnlyUiAsync);
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
