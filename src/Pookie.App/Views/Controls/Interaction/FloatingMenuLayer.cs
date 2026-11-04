using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal enum FloatingMenuPlacement { AboveLeft, AboveRight, BelowRight }

internal abstract class MenuLayer : Panel
{
    internal abstract bool IsOpen { get; }
    internal abstract double Reveal { get; }
    internal abstract Control Card { get; }
    internal abstract void Show();
    internal abstract void Close(bool immediate = false);
}

// A non-modal layer shared by composer, top-bar and player menus.
// Only the card receives input; the shadow and empty space remain transparent.
internal sealed class FloatingMenuLayer : MenuLayer
{
    internal static readonly Color Surface = PopupAppearance.Surface;
    private readonly ChatPopupLayer view;
    internal override bool IsOpen => view.IsOpen;
    internal override double Reveal => view.Reveal;
    internal override GlassCard Card { get; }
    internal static GlassCard CreateCard(FrameworkElement content, double width, double padding = PopupAppearance.Padding) =>
        new GlassCard(PopupAppearance.AppBackground) { HoverEnabled = false }
            .Background(Surface).CornerRadius(PopupAppearance.Radius).Padding(padding).Width(width).Child(content);

    internal FloatingMenuLayer(FrameworkElement anchor, GlassCard card,
        FloatingMenuPlacement placement = FloatingMenuPlacement.AboveLeft, double gap = 8)
    {
        Card = card;
        view = new ChatPopupLayer(card, () => anchor.Bounds, () => Bounds,
            placement: () => placement == FloatingMenuPlacement.BelowRight ? MenuPlacement.Below : MenuPlacement.Above,
            alignRight: placement != FloatingMenuPlacement.AboveLeft, gap: gap).Initialize();
        view.Closed = () => IsVisible = false;
    }
    internal FloatingMenuLayer Initialize() { Add(view); IsVisible = false; return this; }
    internal override void Show() { IsVisible = true; view.Show(); }
    internal override void Close(bool immediate = false) => view.Close(immediate);
    protected override Size MeasureContent(Size availableSize) { view.Measure(availableSize); return default; }
    protected override Size ArrangeOverride(Size finalSize) { view.Arrange(Bounds); return finalSize; }
    protected override UIElement? OnHitTest(Point point) => view.HitTest(point);
}
