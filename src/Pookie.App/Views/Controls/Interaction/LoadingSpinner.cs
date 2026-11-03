using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class LoadingSpinner : Control
{
    public static readonly MewProperty<bool> IsActiveProperty = MewProperty<bool>.Register<LoadingSpinner>(
        nameof(IsActive), false, MewPropertyOptions.AffectsRender, static (self, _, _) => self.UpdateHost());
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(1600), Easing.Linear) { RepeatCount = -1 };
    private readonly PathGeometry arc = new();
    private Window? window;
    private double phaseOrigin;
    private bool disposed;
    internal double Phase { get; private set; }
    internal bool Running => motion.IsRunning;

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public LoadingSpinner()
    {
        IsHitTestVisible = false;
        motion.TickCallback = value => { Phase = (phaseOrigin + value) % 1; InvalidateVisual(); };
    }

    private void UpdateHost()
    {
        var next = !disposed && IsActive ? FindVisualRoot() as Window : null;
        if (window != next)
        {
            if (window != null) { window.FrameRendered -= RefreshAnimation; window.WindowStateChanged -= WindowStateChanged; }
            motion.Stop(); window = next;
            if (window != null) { window.FrameRendered += RefreshAnimation; window.WindowStateChanged += WindowStateChanged; }
        }
        RefreshAnimation();
    }

    private void WindowStateChanged(WindowState _) => RefreshAnimation();

    private void RefreshAnimation()
    {
        var visible = !disposed && IsActive && window != null && window.WindowState != WindowState.Minimized;
        for (Element? element = this; visible && element != null; element = element.Parent)
            if (element is UIElement { IsVisible: false } or UIElement { Opacity: <= 0 }) visible = false;
        if (visible && !motion.IsRunning) { phaseOrigin = Phase; motion.Start(); }
        else if (!visible) motion.Stop();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); UpdateHost(); }

    protected override Size MeasureContent(Size availableSize) => new(24, 24);

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);
        if (!IsActive) return;
        Draw(context, Bounds, Foreground, Phase, arc);
    }

    internal static void Draw(IGraphicsContext context, Rect bounds, Color foreground, double phase, PathGeometry arc)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        var thickness = Math.Clamp(size * .085, 1.5, 3);
        var radius = (size - thickness) / 2 - 1;
        if (radius <= 0) return;
        var cx = bounds.X + bounds.Width / 2; var cy = bounds.Y + bounds.Height / 2;
        context.Save();
        try
        {
            context.GlobalAlpha *= .10f;
            context.DrawEllipse(new Rect(cx - radius, cy - radius, radius * 2, radius * 2), foreground, thickness);
        }
        finally { context.Restore(); }

        const double minimum = Math.PI / 4, maximum = Math.PI * 1.5;
        var sweep = minimum + (maximum - minimum) * (.5 - .5 * Math.Cos(phase * Math.Tau));
        // Move both ends forward, with a continuous position and speed across loop boundaries.
        var start = phase * Math.Tau * 2 - Math.PI / 2 - (sweep - minimum) / 2;
        arc.Reset(); arc.Arc(cx, cy, radius, radius, start, start + sweep, false);
        context.DrawPath(arc, foreground, thickness);
        var half = thickness / 2;
        for (var end = 0; end < 2; end++)
        {
            var angle = start + end * sweep;
            context.FillEllipse(new Rect(cx + radius * Math.Cos(angle) - half, cy + radius * Math.Sin(angle) - half, thickness, thickness), foreground);
        }
    }

    protected override void OnDispose()
    { disposed = true; UpdateHost(); base.OnDispose(); }
}

internal static class LoadingSpinnerExtensions
{
    internal static LoadingSpinner IsActive(this LoadingSpinner spinner, bool active = true)
    { spinner.IsActive = active; return spinner; }
}
