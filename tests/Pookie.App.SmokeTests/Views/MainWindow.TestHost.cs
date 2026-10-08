using Aprillz.MewUI;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool uiSmoke;
    private bool loginUiSmoke;
    private bool browserShutdownSmoke;
    private bool mediaUiSmoke;
    private bool bufferUiSmoke;
    private bool expandedUiSmoke;
    private bool lyricsUiSmoke;
    private bool artworkUiSmoke;
    internal Exception? VerificationFailure { get; private set; }
    private DispatcherTimer? closeTimer;

    internal void ConfigureVerification(string[] args)
    {
        uiSmoke = args.Contains("--ui-smoke-test");
        loginUiSmoke = args.Contains("--login-ui-smoke-test");
        browserShutdownSmoke = args.Contains("--browser-shutdown-smoke-test");
        mediaUiSmoke = args.Contains("--media-ui-smoke-test");
        bufferUiSmoke = args.Contains("--buffer-ui-smoke-test");
        expandedUiSmoke = args.Contains("--expanded-ui-smoke-test");
        lyricsUiSmoke = args.Contains("--lyrics-ui-smoke-test");
        artworkUiSmoke = args.Contains("--artwork-ui-smoke-test");
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
        if (uiSmoke || expandedUiSmoke)
        {
            lyricsService.Dispose();
            lyricsService = new(new EmptyLyricsProvider(), Path.Combine(dataPaths.Cache, "FixtureLyrics"));
        }
        if (uiSmoke) Run(VerifyUiAsync);
        if (loginUiSmoke) Run(VerifyLoginUiAsync);
        if (browserShutdownSmoke) Run(StartBrowserShutdownCheckAsync);
        if (mediaUiSmoke) Run(VerifySystemMediaAsync);
        if (bufferUiSmoke) Run(VerifyBufferUiAsync);
        if (expandedUiSmoke) Run(VerifyExpandedPlayerAsync);
        if (lyricsUiSmoke) Run(VerifyLyricsUiAsync);
        if (artworkUiSmoke) Run(VerifyPlayerArtworkAsync);
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
