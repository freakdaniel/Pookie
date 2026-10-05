using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task HideStartupSplashAsync()
    {
        if (disposed) return;
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
