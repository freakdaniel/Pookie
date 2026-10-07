using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The layer bounds cover the entire motion, including live rendering after the
// renderer stops recording rapidly changing frames. Child layout stays fixed.
internal sealed class PlayerMotionLayer : Panel
{
    private Rect contentBounds;
    internal double OffsetX { get; private set; }
    internal double OffsetY { get; private set; }

    internal PlayerMotionLayer(FrameworkElement child)
    {
        Add(child);
    }

    internal void Move(double x, double y, bool interactive)
    {
        IsHitTestVisible = interactive;
        if (OffsetX == x && OffsetY == y) return;
        OffsetX = x; OffsetY = y;
        InvalidateVisual();
    }

    internal void ArrangeWithin(Rect motionBounds, Rect childBounds)
    {
        if (contentBounds != childBounds)
        {
            contentBounds = childBounds;
            InvalidateArrange();
        }
        Arrange(motionBounds);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Children[0].Measure(availableSize);
        return Children[0].DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Children[0].Arrange(contentBounds);
        return finalSize;
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        context.Save();
        context.Translate(OffsetX, OffsetY);
        base.RenderSubtree(context);
        context.Restore();
    }
}
