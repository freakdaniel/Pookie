using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The card keeps moving while scrolling; its shadow geometry does not change.
// Retain only the decoration, so waveform, artwork and controls remain live.
internal sealed class CardShadow : Panel
{
    private readonly Thickness shadowPadding;
    private readonly double radius;
    private readonly FrameworkElement child;
    private readonly BoxShadowRaster shadow;

    internal CardShadow(FrameworkElement child, double blur = 12, double offset = 4, double radius = 20, Color? color = null)
    {
        this.child = child; this.radius = radius;
        shadowPadding = new Thickness(blur, Math.Max(0, blur - offset), blur, Math.Max(0, blur + offset));
        shadow = new BoxShadowRaster(blur, offset, color ?? Color.FromArgb(60, 0, 0, 0));
        Add(child);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        child.Measure(availableSize.Deflate(shadowPadding));
        return child.DesiredSize.Inflate(shadowPadding);
    }

    protected override void ArrangeContent(Rect bounds) => child.Arrange(bounds.Deflate(shadowPadding));

    protected override void OnRender(IGraphicsContext context)
    {
        if (child.Bounds.Width <= 0 || child.Bounds.Height <= 0) return;
        shadow.Draw(context, child.Bounds, radius);
    }

    protected override UIElement? OnHitTest(Point point) =>
        IsVisible && IsHitTestVisible && IsEffectivelyEnabled ? child.HitTest(point) : null;

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) shadow.Dispose();
    }

    protected override void OnDispose() { shadow.Dispose(); base.OnDispose(); }
}
