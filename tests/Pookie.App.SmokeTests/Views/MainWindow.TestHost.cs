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
        if (uiSmoke) Run(VerifyUiAsync);
        if (loginUiSmoke) Run(VerifyLoginUiAsync);
        if (browserShutdownSmoke) Run(StartBrowserShutdownCheckAsync);
        if (mediaUiSmoke) Run(VerifySystemMediaAsync);
        if (bufferUiSmoke) Run(VerifyBufferUiAsync);
        if (expandedUiSmoke) Run(VerifyExpandedPlayerAsync);
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
