using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The same soft reflection and specular edge on search, navigation and artwork cards.
internal static class GlassSheen
{
    internal static readonly Color Surface = Color.FromArgb(218, 49, 49, 55);
    // Neutral dark glass should describe its silhouette quietly. Prominent
    // filled controls retain the stronger reflection in their pigment layer.
    internal const double ReflectionOpacity = .38;

    internal static Color ControlTint(double brightness = 0)
    {
        var amount = Math.Clamp((brightness - 65) / 145, 0, 1);
        amount = amount * amount * (3 - 2 * amount);
        return Color.FromArgb((byte)Math.Round(132 + 66 * amount), 29, 29, 35);
    }

    internal static Color AdaptTint(Color tint, double brightness) => Color.FromArgb(
        (byte)Math.Clamp(ControlTint(brightness).A + (tint.A - 132) * 28d / 123, 0, 230), tint.R, tint.G, tint.B);

    // Cards already have a dark scrim: lift their controls with a translucent
    // white layer instead of applying the fullscreen cover's second dark tint.
    internal static Color LightControlTint(Color state) => Color.FromArgb(
        (byte)Math.Clamp(28 + (state.A - 132) * .7, 28, 52), 255, 255, 255);

    internal static PathGeometry Shape(Rect bounds, CornerRadius corners) => PathGeometry.FromRoundedRect(bounds,
        corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft);

}

// One shape per owner, invalidated only when layout or connected corners change.
internal sealed class GlassGeometry
{
    private Rect bounds;
    private CornerRadius corners;
    private PathGeometry? shape;
    internal PathGeometry Get(Rect nextBounds, CornerRadius nextCorners)
    {
        if (shape == null || bounds != nextBounds || corners != nextCorners)
        {
            bounds = nextBounds;
            corners = nextCorners;
            shape = GlassSheen.Shape(bounds, corners);
            shape.Freeze();
        }
        return shape;
    }
}

// This layer belongs above artwork/scrim and below labels, waveforms and buttons.
internal sealed class ArtworkGlassSheen : Control
{
    private readonly double radius;
    private readonly Action? invalidateAfterResize;
    private readonly GlassSheenRaster sheen = new();
    internal ArtworkGlassSheen(double radius = 20, bool deferResize = false)
    {
        this.radius = radius;
        invalidateAfterResize = deferResize ? InvalidateVisual : null;
        IsHitTestVisible = false;
    }
    protected override void OnRender(IGraphicsContext context) => sheen.Draw(context, Bounds, new CornerRadius(radius),
        minimumScale: GetDpi() / 96d, invalidateAfterResize: invalidateAfterResize);
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) sheen.Dispose();
    }

    protected override void OnDispose() { sheen.Dispose(); base.OnDispose(); }
}
