using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Keep rounded ends at their original size while only the flat middle changes
// height. Foreground controls are laid out normally and never stretched.
internal static class VerticalRasterStretch
{
    internal static void Draw(IGraphicsContext context, IImage image, Rect target, double fixedEdge, double? originalHeight = null)
    {
        var scale = originalHeight is { } height ? image.PixelHeight / height : image.PixelWidth / target.Width;
        var sourceEdge = Math.Min(Math.Ceiling(fixedEdge * scale), (image.PixelHeight - 1) / 2d);
        var edge = Math.Min(sourceEdge / scale, target.Height / 2);
        context.DrawImage(image, new(target.X, target.Y, target.Width, edge),
            new(0, 0, image.PixelWidth, sourceEdge));
        if (target.Height > edge * 2)
            context.DrawImage(image, new(target.X, target.Y + edge, target.Width, target.Height - edge * 2),
                new(0, sourceEdge, image.PixelWidth, image.PixelHeight - sourceEdge * 2));
        context.DrawImage(image, new(target.X, target.Bottom - edge, target.Width, edge),
            new(0, image.PixelHeight - sourceEdge, image.PixelWidth, sourceEdge));
    }
}
