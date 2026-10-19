using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal enum FloatingMenuPlacement { AboveLeft, AboveRight, BelowRight }

// A non-modal layer shared by composer, top-bar and player menus.
// Only the card receives input; the shadow and empty space remain transparent.
internal sealed class FloatingMenuLayer : Panel
{
    internal static readonly Color Surface = Color.FromRgb(51, 51, 51);
    private readonly FrameworkElement anchor;
    private readonly ShadowDecorator menu;
    private readonly FloatingMenuPlacement placement;
    private readonly double gap;
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(160), Easing.CubicBezier(.2, 0, 0, 1));
    private bool target;
    private double from, amount;
    internal bool IsOpen => target;
    internal double Reveal => amount;
    internal Border Card { get; }
    internal static Border CreateCard(FrameworkElement content, double width, double padding = 6) =>
        new Border().Background(Surface).BorderThickness(0).CornerRadius(14).Padding(padding).Width(width).Child(content);

    internal FloatingMenuLayer(FrameworkElement anchor, Border card,
        FloatingMenuPlacement placement = FloatingMenuPlacement.AboveLeft, double gap = 8)
    {
        this.anchor = anchor; Card = card; this.placement = placement; this.gap = gap;
        menu = new ShadowDecorator().BlurRadius(12).OffsetY(3).ShadowColor(Color.FromArgb(85, 0, 0, 0))
            .CornerRadius(14).Child(card);
    }
    internal FloatingMenuLayer Initialize() { Add(menu); menu.Opacity = 0; IsVisible = false; return this; }
    internal void Show() => SetOpen(true);
    internal void Close(bool immediate = false)
    {
        if (immediate) { target = false; fade.Stop(); amount = menu.Opacity = 0; menu.IsHitTestVisible = false; IsVisible = false; return; }
        SetOpen(false);
    }
    private void SetOpen(bool open)
    {
        if (target == open) return;
        fade.Stop(); target = open; from = amount; IsVisible = true; menu.IsHitTestVisible = open;
        fade.TickCallback = t => { amount = from + ((target ? 1 : 0) - from) * t; menu.Opacity = amount; };
        fade.CompletedCallback = () => { if (!target) IsVisible = false; };
        fade.Start(); InvalidateMeasure();
    }
    protected override Size MeasureContent(Size availableSize) { menu.Measure(availableSize); return default; }
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Align the card, excluding the shadow gutter, with the trigger.
        var size = menu.DesiredSize; var padding = menu.ShadowPadding;
        var x = placement == FloatingMenuPlacement.AboveLeft ? anchor.Bounds.X - padding.Left : anchor.Bounds.Right + padding.Right - size.Width;
        var above = anchor.Bounds.Y - gap + padding.Bottom - size.Height;
        var below = anchor.Bounds.Bottom + gap - padding.Top;
        var y = placement == FloatingMenuPlacement.BelowRight ? below : above;
        if (y < Bounds.Y && below + size.Height <= Bounds.Bottom) y = below;
        if (y + size.Height > Bounds.Bottom && above >= Bounds.Y) y = above;
        x = Math.Clamp(x, Bounds.X, Math.Max(Bounds.X, Bounds.Right - size.Width));
        y = Math.Clamp(y, Bounds.Y, Math.Max(Bounds.Y, Bounds.Bottom - size.Height));
        menu.Arrange(new Rect(x, y, size.Width, size.Height));
        return finalSize;
    }
    protected override UIElement? OnHitTest(Point point)
    {
        var hit = IsVisible && menu.IsHitTestVisible ? menu.HitTest(point) : null;
        return ReferenceEquals(hit, menu) ? null : hit;
    }
    protected override void OnDispose() { fade.Stop(); base.OnDispose(); }
}
