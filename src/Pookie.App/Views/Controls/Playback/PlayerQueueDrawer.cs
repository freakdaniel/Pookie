using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The surface grows out of the queue button and joins the player's gradient without a seam.
// Content layout stays fixed while a changing clip reveals it, preserving virtualized rows.
internal sealed class PlayerQueueDrawer : MenuLayer
{
    private readonly FrameworkElement anchor, bar, frame;
    private readonly PlayerBackdrop backdrop;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(420), Easing.CubicBezier(.22, .8, .22, 1));
    private readonly PathGeometry shape = new();
    private bool target;
    private double amount, from;
    internal override bool IsOpen => target;
    internal override double Reveal => amount;
    internal override Border Card { get; }
    internal double ContentReveal => Smooth(Math.Clamp((amount - .2) / .8, 0, 1));
    internal double ContentOffset => 24 * (1 - amount);
    internal PlayerGradient RenderedGradient { get; private set; }
    internal Rect SurfaceBounds
    {
        get
        {
            var full = Card.Bounds;
            var spread = Math.Min(1, amount * 1.5);
            var initialX = anchor.Bounds.X + anchor.ActualWidth / 2 - 28;
            var width = 56 + (full.Width - 56) * spread;
            var x = initialX + (full.X - initialX) * spread;
            var height = full.Height * amount;
            return new Rect(x, full.Bottom - height, width, height + 1);
        }
    }

    internal PlayerQueueDrawer(FrameworkElement anchor, FrameworkElement bar, FrameworkElement frame,
        PlayerBackdrop backdrop, Border card)
    {
        this.anchor = anchor; this.bar = bar; this.frame = frame; this.backdrop = backdrop; Card = card;
        Focusable = true;
        Add(card); IsVisible = false;
        backdrop.PaletteChanged += InvalidateVisual;
        motion.TickCallback = progress => { amount = from + ((target ? 1 : 0) - from) * progress; InvalidateVisual(); };
        motion.CompletedCallback = () => { amount = target ? 1 : 0; if (!target) IsVisible = false; InvalidateVisual(); };
    }

    internal override void Show() => SetOpen(true);
    internal override void Close(bool immediate = false)
    {
        if (immediate)
        { motion.Stop(); target = false; amount = 0; IsVisible = false; Card.IsHitTestVisible = false; InvalidateVisual(); }
        else SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (target == open) return;
        motion.Stop(); from = amount; target = open;
        IsVisible = true; Card.IsHitTestVisible = open;
        motion.Duration = TimeSpan.FromMilliseconds(open ? 420 : 280);
        motion.Start(); InvalidateMeasure();
    }

    private static double Smooth(double value) => value * value * (3 - 2 * value);
    protected override Size MeasureContent(Size availableSize)
    {
        Card.Measure(new Size(Math.Min(460, availableSize.Width), Math.Min(420, Math.Max(0, availableSize.Height - 164))));
        return default;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var right = Math.Min(Bounds.Right, frame.Bounds.Right);
        var width = Math.Min(460, Math.Max(0, right - Bounds.X));
        var bottom = Math.Clamp(bar.Bounds.Y, Bounds.Y, Bounds.Bottom);
        var height = Math.Min(420, Math.Max(0, bottom - Bounds.Y - 92));
        Card.Arrange(new Rect(right - width, bottom - height, width, height));
        return finalSize;
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var surface = SurfaceBounds;
        if (amount <= .0001 || surface.Height <= 1 || Card.ActualHeight <= 0) return;
        var topRadius = Math.Min(24, surface.Height / 2);
        var join = Math.Min(20, surface.Height / 3);
        var leftJoin = Math.Min(join, Math.Max(0, surface.X - Bounds.X));
        var rightJoin = Math.Min(join, Math.Max(0, Bounds.Right - surface.Right));
        var left = surface.X; var right = surface.Right; var top = surface.Y; var bottom = surface.Bottom;
        shape.Reset();
        shape.MoveTo(left + topRadius, top);
        shape.LineTo(right - topRadius, top); shape.QuadTo(right, top, right, top + topRadius);
        shape.LineTo(right, bottom - rightJoin); shape.QuadTo(right, bottom, right + rightJoin, bottom);
        shape.LineTo(left - leftJoin, bottom); shape.QuadTo(left, bottom, left, bottom - leftJoin);
        shape.LineTo(left, top + topRadius); shape.QuadTo(left, top, left + topRadius, top); shape.Close();
        context.Save();
        try
        {
            context.SetClipPath(shape);
            RenderedGradient = backdrop.CurrentGradient;
            backdrop.RenderRegion(context, new Rect(left - leftJoin, top, surface.Width + leftJoin + rightJoin, surface.Height));
            context.GlobalAlpha *= (float)ContentReveal;
            context.Translate(0, ContentOffset);
            Card.Render(context);
        }
        finally { context.Restore(); }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!target || !IsVisible || !SurfaceBounds.Contains(point)) return null;
        // During the morph, consume clicks on the visible surface rather than forwarding them to the page.
        if (amount < .99) return this;
        return Card.HitTest(new Point(point.X, point.Y - ContentOffset));
    }
    protected override void OnDispose()
    { motion.Stop(); backdrop.PaletteChanged -= InvalidateVisual; base.OnDispose(); }
}
