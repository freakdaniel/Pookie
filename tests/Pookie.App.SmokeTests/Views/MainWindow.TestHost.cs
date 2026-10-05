using Aprillz.MewUI;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool uiSmoke;
    private bool loginUiSmoke;
    private DispatcherTimer? closeTimer;

    internal void ConfigureVerification(string[] args)
    {
        uiSmoke = args.Contains("--ui-smoke-test");
        loginUiSmoke = args.Contains("--login-ui-smoke-test");
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
