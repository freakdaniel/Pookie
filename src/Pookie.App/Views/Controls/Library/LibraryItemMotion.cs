using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Change the virtualized layout once, then animate the contents from their last
// painted position. Scrolling and waveform layout never run once per frame.
internal sealed class LibraryItemMotion : ContentControl
{
    private readonly AnimationClock clock = new(TimeSpan.FromMilliseconds(320), Easing.CubicBezier(.2, 0, 0, 1));
    private Point offset, from;
    private Point? previous;
    private bool entering, exiting;
    private TaskCompletionSource? exitCompletion;
    internal long? TrackId { get; set; }
    internal long Edit { get; set; }
    internal Point PaintedPosition => new(Bounds.X + offset.X, Bounds.Y + offset.Y);
    internal bool Moving => clock.IsRunning;

    internal LibraryItemMotion()
    {
        Background = Color.Transparent; BorderThickness = 0; Padding = new Thickness(0);
        SkipViewportCull = true;
        clock.TickCallback = amount =>
        {
            offset = exiting ? new Point(0, -8 * amount) : new Point(from.X * (1 - amount), from.Y * (1 - amount));
            if (entering || exiting) Opacity = exiting ? 1 - amount : amount;
            InvalidateVisual();
        };
        clock.CompletedCallback = () =>
        {
            if (!exiting) { offset = default; Opacity = 1; }
            exitCompletion?.TrySetResult(); exitCompletion = null;
        };
    }

    internal void Retarget(Point? painted, bool appear = false)
    {
        ResetMotion(); previous = painted; entering = painted == null && appear;
        if (entering) { Opacity = 0; clock.Start(); }
    }

    internal Task DisappearAsync()
    {
        ResetMotion(); exiting = true; IsHitTestVisible = false;
        exitCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.Duration = TimeSpan.FromMilliseconds(180);
        clock.Start();
        return exitCompletion.Task;
    }

    internal void ResetMotion()
    {
        clock.Stop(); exitCompletion?.TrySetResult(); exitCompletion = null;
        previous = null; offset = from = default; entering = exiting = false;
        Opacity = 1; IsHitTestVisible = true; clock.Duration = TimeSpan.FromMilliseconds(320);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (previous is { } old)
        {
            previous = null;
            from = new Point(old.X - Bounds.X, old.Y - Bounds.Y);
            if (Math.Abs(from.X) + Math.Abs(from.Y) is > .5 and < 1500)
            { offset = from; clock.Start(); }
        }
        context.Save(); context.Translate(offset.X, offset.Y);
        base.RenderSubtree(context); context.Restore();
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled || Opacity < .05) return null;
        return Content is UIElement child ? child.HitTest(new Point(point.X - offset.X, point.Y - offset.Y)) : null;
    }

    protected override void OnDispose() { ResetMotion(); base.OnDispose(); }
}
