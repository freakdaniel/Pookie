using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyLibraryLoadingAsync()
    {
        foreach (var listView in new[] { false, true })
        {
            likesAsList.Value = listView;
            await WaitForLikedLayoutAsync(() => (listView ? likedList : likedGrid).ActualWidth > 100);
            var bounds = likesLoadingView.Root.Bounds;
            SetLikesLoading(true);
            await WaitForLikedLayoutAsync(() => likesLoadingView.Skeleton.ActualWidth > 100);
            if (likedEmpty.Value || likesLoadingView.Root.Bounds != bounds || ((FrameworkElement)likesLoadingView.Root.Children[0]).IsHitTestVisible)
                throw new InvalidOperationException("Library loading moved its viewport or exposed empty content.");
            CaptureUiPreview(listView ? "skeleton-list" : "skeleton-grid");
            SetLikesLoading(false);
            await WaitForLikedLayoutAsync(() => likesLoadingView.Skeleton.Opacity is > 0 and < 1);
            CaptureUiPreview(listView ? "reveal-list" : "reveal-grid");
            await WaitForLikedLayoutAsync(() => !likesLoadingView.Skeleton.IsVisible);
            if (likesLoadingView.Root.Bounds != bounds || ((FrameworkElement)likesLoadingView.Root.Children[0]).Opacity != 1)
                throw new InvalidOperationException("Skeleton reveal changed the viewport or left content faded.");
        }
        likesAsList.Value = false;
        await WaitForLikedLayoutAsync(() => likedTiles.Values.Count(tile => tile.Track != null && tile.Root.ActualWidth > 100) >= 12);
        Console.WriteLine("UI_LIBRARY_LOADING_OK: shimmer and crossfade in grid/list views, stable viewport, no empty label or invisible input targets");
    }

    private async Task VerifyPlayerHoverAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!GetLikedActionCursor(out var originalCursor)) throw new InvalidOperationException("Could not save cursor position.");
        Window.Activate();
        timer.Stop();
        try
        {
            foreach (var slider in new[] { progress, volumeSlider })
            {
                var center = new Point(slider.Bounds.X + slider.Bounds.Width / 2, slider.Bounds.Y + slider.Bounds.Height / 2);
                MoveLikedActionCursor(center); SendLikedActionMouse(0x200, 0, center);
                await WaitForLikedLayoutAsync(() => slider.IsMouseOver && slider.ThumbBrush.A == 255);
                if (slider == progress && positionLabel.Opacity < .99)
                    await WaitForLikedLayoutAsync(() => positionLabel.Opacity == 1 && durationLabel.Opacity == 1);
                var outside = new Point(1, 1);
                MoveLikedActionCursor(outside); SendLikedActionMouse(0x200, 0, outside);
                await WaitForLikedLayoutAsync(() => !slider.IsMouseOver);
                if (slider.ThumbBrush.A != 255 || slider == progress && positionLabel.Opacity != 1)
                    throw new InvalidOperationException("Slider details disappeared before the hover debounce elapsed.");
                // A quick return cancels the pending hide rather than flashing the thumb off.
                MoveLikedActionCursor(center); SendLikedActionMouse(0x200, 0, center);
                await WaitForLikedLayoutAsync(() => slider.IsMouseOver && slider.ThumbBrush.A == 255);
                var savedValue = slider.Value;
                var savedUpdating = updatingProgress;
                try
                {
                    updatingProgress = true;
                    SendLikedActionMouse(0x201, 1, center);
                    await WaitForLikedLayoutAsync(() => slider.IsMouseCaptured);
                    MoveLikedActionCursor(outside); SendLikedActionMouse(0x200, 1, outside);
                    var held = System.Diagnostics.Stopwatch.StartNew();
                    // Stay outside longer than the hide delay and fade combined.
                    while (held.ElapsedMilliseconds < 520)
                    {
                        await WaitForLoginFrameAsync();
                        if (slider.ThumbBrush.A != 255 || slider == progress && positionLabel.Opacity != 1)
                            throw new InvalidOperationException("Hover details faded during an active slider drag.");
                    }
                    SendLikedActionMouse(0x202, 0, outside);
                    await WaitForLikedLayoutAsync(() => !slider.IsMouseCaptured);
                }
                finally { slider.Value = savedValue; updatingProgress = savedUpdating; Window.ReleaseMouseCapture(); }
                await WaitForLikedLayoutAsync(() => slider.ThumbBrush.A is > 0 and < 255);
                await WaitForLikedLayoutAsync(() => slider.ThumbBrush.A == 0);
                if (slider == progress)
                    await WaitForLikedLayoutAsync(() => positionLabel.Opacity == 0 && durationLabel.Opacity == 0);
            }
            Console.WriteLine("UI_PLAYER_HOVER_OK: native pointer hover, fade in/out, delayed hide, canceled hide on reentry, drag outside bounds and matching time-label behavior");
        }
        finally
        {
            timer.Start();
            if (!SetLikedActionCursor(originalCursor.X, originalCursor.Y)) throw new InvalidOperationException("Could not restore cursor position.");
        }
    }
}
