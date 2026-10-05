using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private const int StartupMinimumVisibleDurationMs = 1000;
    private Task startupMinimumDisplay = Task.CompletedTask;

    private async Task HoldStartupSplashAsync()
    {
        var sizeEasing = Easing.CubicBezier(0.16, 1, 0.3, 1);
        var fadeEasing = Easing.CubicBezier(0.2, 0, 0, 1);
        await AnimateStartupAsync(560, Easing.Linear, progress =>
        {
            var sizeProgress = sizeEasing(progress);
            startupLogo.Width = 132 * sizeProgress;
            startupLogo.Height = 124 * sizeProgress;
            startupLogo.Opacity = fadeEasing(Math.Clamp(progress * 560 / 360, 0, 1));
            startupSpinner.Opacity = fadeEasing(Math.Clamp(progress * 560 / 300, 0, 1));
        });
        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendered()
        {
            if (startupLogo.Width == 132 && startupLogo.Height == 124 &&
                startupLogo.Opacity == 1 && startupSpinner.Opacity == 1 &&
                Math.Abs(startupLogo.ActualWidth - 132) < 1 && Math.Abs(startupLogo.ActualHeight - 124) < 1)
                presented.TrySetResult();
        }
        Window.FrameRendered += OnRendered;
        try
        {
            // Start the dwell time on a fully appeared frame, independently of session restoration.
            Window.InvalidateVisual();
            await presented.Task.WaitAsync(lifetime.Token);
        }
        finally { Window.FrameRendered -= OnRendered; }
        await Task.Delay(StartupMinimumVisibleDurationMs, lifetime.Token);
    }

    private async Task HideStartupSplashAsync()
    {
        if (disposed) return;
        await startupMinimumDisplay;
        if (!CanUseWorkspace)
        {
            await ShowLoginScreenAsync();
            return;
        }
        workspace.IsVisible = true;
        try
        {
            // Wait for actual rendered animation completion before changing any curtain geometry.
            await AnimateStartupAsync(StartupBrandFadeDurationMs, Easing.CubicBezier(0.2, 0, 0, 1),
                progress => startupBrand.Opacity = 1 - progress);
            startupBrandLayer.IsVisible = false;
            startupSpinner.IsActive = false;

            startupCurtain.Height = startupSplash.ActualHeight;
            startupCurtain.Bottom();
            startupCurtainOffset = 0;
            // A single clock drives position and color on every frame.
            await AnimateStartupAsync(StartupCurtainSlideDurationMs, Easing.CubicBezier(0.65, 0, 0.35, 1), progress =>
            {
                startupCurtainOffset = 64 * progress;
                startupCurtain.Height = Math.Max(0, startupSplash.ActualHeight - startupCurtainOffset);
                startupBackdrop.Background = Surface.Lerp(HeaderSurface, progress);
            });

            startupHeader.IsHitTestVisible = true;
            startupContent.IsHitTestVisible = true;
            startupSplash.IsHitTestVisible = false;
            await AnimateStartupAsync(420, Easing.CubicBezier(0.2, 0, 0, 1), progress =>
            {
                startupHeader.Opacity = progress;
                startupContent.Opacity = progress;
                startupSplash.Opacity = 1 - progress;
            });
            if (uiSmoke) StopStartupLayoutProbe();
            startupSplash.IsVisible = false;
            workspace.IsEnabled = true;
            workspace.IsHitTestVisible = true;
        }
        catch (OperationCanceledException) { startupSplash.IsVisible = false; }
    }

    private async Task AnimateStartupAsync(int durationMs, Func<double, double> easing, Action<double> frame)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new AnimationClock(TimeSpan.FromMilliseconds(durationMs), easing)
        {
            TickCallback = frame,
            CompletedCallback = () => completed.TrySetResult()
        };
        frame(0);
        clock.Start();
        try { await completed.Task.WaitAsync(lifetime.Token); }
        finally { clock.Stop(); }
    }
}
