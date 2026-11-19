using System.Numerics;
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
    private Matrix3x2 childTransform = Matrix3x2.Identity;
    private readonly UIElement fadeTarget;
    private double inkStrength = 1;
    internal double InkStrength
    {
        get => inkStrength;
        set { if (inkStrength == value) return; inkStrength = value; fadeTarget.Opacity = Math.Clamp(value, 0, 1); }
    }

    internal PlayerMotionLayer(FrameworkElement child, bool fadeClippedContent = false)
    {
        // A clipped panel only needs an opacity surface the size of its viewport.
        // The artwork shadow can extend beyond the card and uses the layer's full bounds.
        fadeTarget = fadeClippedContent ? child : this;
        SkipViewportCull = true;
        Add(child);
    }

    internal void Move(double x, double y, bool interactive, double scale = 1, Point origin = default)
    {
        IsHitTestVisible = interactive;
        var transform = new Matrix3x2((float)scale, 0, 0, (float)scale,
            (float)(x + origin.X * (1 - scale)), (float)(y + origin.Y * (1 - scale)));
        if (childTransform == transform) return;
        childTransform = transform;
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

    protected override bool TryGetChildTransform(out Matrix3x2 transform)
    {
        transform = childTransform;
        return !transform.IsIdentity;
    }
}
