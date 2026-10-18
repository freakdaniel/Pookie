using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// A non-modal, in-window layer: transparent space never captures the composer.
internal sealed class ChatAttachLayer(FrameworkElement anchor, FrameworkElement menu) : Panel
{
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(160), Easing.CubicBezier(.2, 0, 0, 1));
    private bool target;
    private double from, amount;
    internal bool IsOpen => target;
    internal double Reveal => amount;
    internal ChatAttachLayer Initialize() { Add(menu); IsVisible = false; return this; }
    internal void Show() => SetOpen(true);
    internal void Close(bool immediate = false)
    {
        if (immediate) { target = false; fade.Stop(); amount = menu.Opacity = 0; IsVisible = false; return; }
        SetOpen(false);
    }
    private void SetOpen(bool open)
    {
        if (target == open) return;
        target = open; from = amount; IsVisible = true; menu.IsHitTestVisible = open;
        fade.TickCallback = t => { amount = from + ((target ? 1 : 0) - from) * t; menu.Opacity = amount; };
        fade.CompletedCallback = () => { if (!target) IsVisible = false; };
        fade.Start(); InvalidateMeasure();
    }
    protected override Size MeasureContent(Size availableSize) { menu.Measure(availableSize); return default; }
    protected override Size ArrangeOverride(Size finalSize)
    {
        // ShadowDecorator's gutter is part of its bounds; align the actual card.
        menu.Arrange(new Rect(anchor.Bounds.X - 12, Math.Max(Bounds.Y, anchor.Bounds.Y - menu.DesiredSize.Height + 4),
            menu.DesiredSize.Width, menu.DesiredSize.Height));
        return finalSize;
    }
    protected override UIElement? OnHitTest(Point point)
    {
        var hit = IsVisible && menu.IsHitTestVisible ? menu.HitTest(point) : null;
        return ReferenceEquals(hit, menu) ? null : hit;
    }
    protected override void OnDispose() { fade.Stop(); base.OnDispose(); }
}
