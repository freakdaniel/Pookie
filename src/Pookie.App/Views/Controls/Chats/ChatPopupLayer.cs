using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// One reusable, non-modal overlay. Animation transforms paint and hit testing,
// never the history layout. Reversal starts at the current presentation value.
internal sealed class ChatPopupLayer(FrameworkElement content, Func<Rect> anchor, Func<Rect> viewport, bool atPointer = false) : Panel
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.4, 0, .2, 1));
    private double amount, from;
    private Point origin;
    internal bool IsOpen { get; private set; }
    internal double Reveal => amount;
    internal Rect SurfaceBounds => content.Bounds;
    internal Action? Closed;
    internal ChatPopupLayer Initialize() { Add(content); IsVisible = false; content.Opacity = 0; return this; }
    internal void Show()
    {
        if (IsOpen) { InvalidateMeasure(); return; }
        IsOpen = true; IsVisible = true; IsHitTestVisible = true; Start(1);
    }
    internal void Close(bool immediate = false)
    {
        if (!IsOpen && !immediate) return;
        IsOpen = false; IsHitTestVisible = false;
        if (immediate) { motion.Stop(); amount = content.Opacity = 0; IsVisible = false; Closed?.Invoke(); }
        else Start(0);
    }
    private void Start(double target)
    {
        motion.Stop(); from = amount;
        motion.TickCallback = t => { amount = from + (target - from) * t; content.Opacity = amount; InvalidateVisual(); };
        motion.CompletedCallback = () => { amount = target; if (!IsOpen) { IsVisible = false; Closed?.Invoke(); } };
        motion.Start(); InvalidateMeasure();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var area = viewport();
        content.Measure(new Size(Math.Max(0, Math.Min(availableSize.Width, area.Width) - 16), Math.Max(0, Math.Min(availableSize.Height, area.Height) - 16)));
        return default;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var area = viewport(); var trigger = anchor(); var size = content.DesiredSize;
        var width = Math.Min(size.Width, Math.Max(0, area.Width - 16));
        var height = Math.Min(size.Height, Math.Max(0, area.Height - 16));
        var x = atPointer ? trigger.X : trigger.Right - width;
        var y = atPointer ? trigger.Y - 48 : trigger.Y - height - 8;
        if (!atPointer && y < area.Y + 8 && trigger.Bottom + height + 8 <= area.Bottom - 8) y = trigger.Bottom + 8;
        x = Math.Clamp(x, area.X + 8, Math.Max(area.X + 8, area.Right - width - 8));
        y = Math.Clamp(y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - height - 8));
        content.Arrange(new Rect(x, y, width, height));
        origin = atPointer ? new(Math.Clamp(trigger.X, x, x + width), Math.Clamp(trigger.Y, y, y + height)) : new(x + width, y + height);
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    {
        var scale = .94 + .06 * amount;
        context.Save();
        try { context.Translate(origin.X, origin.Y); context.Scale(scale, scale); context.Translate(-origin.X, -origin.Y); base.RenderSubtree(context); }
        finally { context.Restore(); }
    }
    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsOpen || amount < .1) return null;
        var scale = .94 + .06 * amount;
        var hit = content.HitTest(new(origin.X + (point.X - origin.X) / scale, origin.Y + (point.Y - origin.Y) / scale));
        return ReferenceEquals(hit, content) ? content.Focusable ? content : null : hit;
    }
    protected override void OnDispose() { motion.Stop(); Closed = null; base.OnDispose(); }
}
