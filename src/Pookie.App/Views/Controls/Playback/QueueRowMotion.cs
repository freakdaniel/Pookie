using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Retain the same row while its queue position changes. Wheel scrolling still uses
// its normal layout position; only queue edits introduce a temporary translation.
internal sealed class QueueRowMotion : ContentControl
{
    private readonly AnimationClock motion;
    private double offset, from;
    private double? previousY;
    internal double VisualY => Bounds.Y + offset;
    internal bool Animating => motion.IsRunning;
    internal string? RetainedKey { get; set; }

    internal QueueRowMotion()
    {
        Background = Color.Transparent; BorderThickness = 0; Padding = new Thickness(0);
        SkipViewportCull = true;
        motion = new AnimationClock(TimeSpan.FromMilliseconds(360), Easing.CubicBezier(.2, .4, .2, 1))
        {
            TickCallback = amount => { offset = from * (1 - amount); InvalidateVisual(); },
            CompletedCallback = () => { offset = 0; InvalidateVisual(); }
        };
    }

    internal void Retarget(double? paintedY)
    {
        motion.Stop(); offset = 0; previousY = paintedY;
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (previousY is { } oldY)
        {
            previousY = null;
            from = offset = oldY - Bounds.Y;
            if (Math.Abs(offset) > .5 && Math.Abs(offset) < 800) motion.Start();
            else offset = 0;
        }
        context.Save(); context.Translate(0, offset);
        base.RenderSubtree(context); context.Restore();
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled) return null;
        return Content is UIElement child ? child.HitTest(new Point(point.X, point.Y - offset)) : null;
    }

    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
