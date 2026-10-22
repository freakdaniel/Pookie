using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The same soft reflection and specular edge on search, navigation and artwork cards.
internal static class GlassSheen
{
    internal static readonly Color Surface = Color.FromArgb(218, 49, 49, 55);

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

    internal static void Draw(IGraphicsContext context, Rect bounds, CornerRadius corners, double emphasis = 1)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        context.FillPath(Shape(bounds, corners), new LinearGradientBrush(
            new Point(bounds.X, bounds.Y), new Point(bounds.Right, bounds.Bottom),
            [new(0, Color.FromArgb((byte)(17 + emphasis * 6), 255, 255, 255)),
             new(.45, Color.FromArgb(5, 255, 255, 255)), new(1, Color.FromArgb(1, 255, 255, 255))]));
        var edge = new Rect(bounds.X + .5, bounds.Y + .5, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
        var inset = new CornerRadius(Math.Max(0, corners.TopLeft - .5), Math.Max(0, corners.TopRight - .5),
            Math.Max(0, corners.BottomRight - .5), Math.Max(0, corners.BottomLeft - .5));
        context.DrawPath(Shape(edge, inset), new Pen(new LinearGradientBrush(
            new Point(bounds.X, bounds.Y), new Point(bounds.X, bounds.Bottom),
            [new(0, Color.FromArgb((byte)(28 + emphasis * 16), 255, 255, 255)),
             new(.55, Color.FromArgb(9, 255, 255, 255)), new(1, Color.FromArgb(4, 255, 255, 255))]), 1));
    }
}

// This layer belongs above artwork/scrim and below labels, waveforms and buttons.
internal sealed class ArtworkGlassSheen : Control
{
    private readonly double radius;
    internal ArtworkGlassSheen(double radius = 20) { this.radius = radius; IsHitTestVisible = false; }
    protected override void OnRender(IGraphicsContext context) => GlassSheen.Draw(context, Bounds, new CornerRadius(radius));
}
