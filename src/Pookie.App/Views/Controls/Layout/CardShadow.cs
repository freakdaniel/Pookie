using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The card keeps moving while scrolling; its shadow geometry does not change.
// Retain only the decoration, so waveform, artwork and controls remain live.
internal sealed class CardShadow : Panel
{
    private const double Blur = 12, Offset = 4, Radius = 20;
    private static readonly Color Shadow = Color.FromArgb(60, 0, 0, 0);
    private static readonly Thickness ShadowPadding = new(Blur, Blur - Offset, Blur, Blur + Offset);
    private readonly FrameworkElement child;
    private DecorationRaster? raster;
    private IGraphicsFactory? factory;
    private Size size;
    private double scale;

    internal CardShadow(FrameworkElement child) { this.child = child; Add(child); }

    protected override Size MeasureContent(Size availableSize)
    {
        child.Measure(availableSize.Deflate(ShadowPadding));
        return child.DesiredSize.Inflate(ShadowPadding);
    }

    protected override void ArrangeContent(Rect bounds) => child.Arrange(bounds.Deflate(ShadowPadding));

    protected override void OnRender(IGraphicsContext context)
    {
        if (child.Bounds.Width <= 0 || child.Bounds.Height <= 0) return;
        if (!OperatingSystem.IsWindows())
        {
            context.DrawBoxShadow(child.Bounds, Radius, Blur, Shadow, offsetY: Offset);
            return;
        }
        var graphics = Application.Current.GraphicsFactory;
        if (raster == null || factory != graphics || size != Bounds.Size || scale != context.DpiScale)
        {
            raster?.Dispose(); raster = null;
            factory = graphics; size = Bounds.Size; scale = context.DpiScale;
            raster = new DecorationRaster(graphics, size, scale, (renderer, region) =>
                renderer.DrawBoxShadow(region.Deflate(ShadowPadding), Radius, Blur, Shadow, offsetY: Offset));
        }
        context.DrawImage(raster.Image, Bounds);
    }

    protected override UIElement? OnHitTest(Point point) =>
        IsVisible && IsHitTestVisible && IsEffectivelyEnabled ? child.HitTest(point) : null;

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { raster?.Dispose(); raster = null; }
    }

    protected override void OnDispose() { raster?.Dispose(); base.OnDispose(); }
}
