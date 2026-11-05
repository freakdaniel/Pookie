using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// One reusable, non-modal overlay. Animation transforms paint and hit testing,
// never the history layout. Reversal starts at the current presentation value.
internal sealed class ChatPopupLayer(FrameworkElement content, Func<Rect> anchor, Func<Rect> viewport, bool atPointer = false,
    Func<MenuPlacement>? placement = null, bool alignRight = false, double gap = 8) : Panel
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.4, 0, .2, 1));
    private double amount, from;
    private Point origin;
    private Rect arrangedArea, arrangedTrigger;
    private Size measuredConstraint;
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
        MeasurePopup(new Size(Math.Max(0, Math.Min(availableSize.Width, area.Width) - 16), Math.Max(0, Math.Min(availableSize.Height, area.Height) - 16)));
        return default;
    }
    private void MeasurePopup(Size constraint)
    { content.Measure(constraint); measuredConstraint = constraint; }
    protected override Size ArrangeOverride(Size finalSize)
    {
        ArrangePopup(viewport(), anchor());
        return finalSize;
    }
    private void ArrangePopup(Rect area, Rect trigger)
    {
        arrangedArea = area; arrangedTrigger = trigger;
        // A newly shown layer can be measured before its viewport is arranged.
        // Resolve the final constraint before using a zero/stale desired size.
        var constraint = new Size(Math.Max(0, area.Width - 16), Math.Max(0, area.Height - 16));
        if (constraint != measuredConstraint) MeasurePopup(constraint);
        var size = content.DesiredSize;
        var width = Math.Min(size.Width, Math.Max(0, area.Width - 16));
        var height = Math.Min(size.Height, Math.Max(0, area.Height - 16));
        var x = atPointer ? trigger.X : trigger.Right - width;
        var y = atPointer ? trigger.Y - 48 : trigger.Y - height - 8;
        if (!atPointer && y < area.Y + 8 && trigger.Bottom + height + 8 <= area.Bottom - 8) y = trigger.Bottom + 8;
        if (placement?.Invoke() is { } requested)
        {
            x = alignRight ? trigger.Right - width : trigger.X;
            y = requested == MenuPlacement.Above ? trigger.Y - height - gap : trigger.Bottom + gap;
            if (requested == MenuPlacement.Pointer) { x = trigger.X; y = trigger.Y; }
            else if (requested is MenuPlacement.Left or MenuPlacement.Right)
            {
                y = trigger.Y;
                x = requested == MenuPlacement.Right ? trigger.Right + gap : trigger.X - width - gap;
                if (x + width > area.Right - 8 && trigger.X - width - gap >= area.X + 8) x = trigger.X - width - gap;
                else if (x < area.X + 8 && trigger.Right + width + gap <= area.Right - 8) x = trigger.Right + gap;
            }
            else if (requested == MenuPlacement.Above && y < area.Y + 8 && trigger.Bottom + height + gap <= area.Bottom - 8)
                y = trigger.Bottom + gap;
            else if (requested == MenuPlacement.Below && y + height > area.Bottom - 8 && trigger.Y - height - gap >= area.Y + 8)
                y = trigger.Y - height - gap;
        }
        x = Math.Clamp(x, area.X + 8, Math.Max(area.X + 8, area.Right - width - 8));
        y = Math.Clamp(y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - height - 8));
        content.Arrange(new Rect(x, y, width, height));
        origin = atPointer || placement != null
            ? new(Math.Clamp(alignRight ? trigger.Right : trigger.X, x, x + width), Math.Clamp(trigger.Y, y, y + height))
            : new(x + width, y + height);
    }
    protected override void OnRender(IGraphicsContext context)
    {
        // The overlay can be arranged before its sibling chat columns during a
        // resize. Reconcile the live anchor/viewport before the first paint, so
        // an open picker/menu never retains the previous window's coordinates.
        var area = viewport(); var trigger = anchor();
        if (area != arrangedArea || trigger != arrangedTrigger)
        {
            if (area != arrangedArea) MeasureContent(new Size(ActualWidth, ActualHeight));
            ArrangePopup(area, trigger);
        }
    }

    protected override bool TryGetChildTransform(out Matrix3x2 transform)
    {
        transform = Matrix3x2.CreateScale((float)(.94 + .06 * amount), new Vector2((float)origin.X, (float)origin.Y));
        return !transform.IsIdentity;
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
