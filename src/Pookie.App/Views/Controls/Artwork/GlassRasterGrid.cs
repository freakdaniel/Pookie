using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// One transparent texel of padding protects the curved silhouette from texture
// edge clamping. Align the samples to device pixels, including fractional layout
// positions, so the already area-filtered rim isn't resampled a second time.
internal readonly record struct GlassRasterGrid(int Width, int Height, Rect Tile)
{
    internal static GlassRasterGrid For(IGraphicsContext context, Rect bounds, double minimumScale = 0)
    {
        var scale = Math.Max(context.DpiScale, minimumScale);
        var transform = context.GetTransform();
        var x = (bounds.X + transform.M31) * scale;
        var y = (bounds.Y + transform.M32) * scale;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)) + 3;
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)) + 3;
        return new(width, height, new Rect((Math.Floor(x) - x - 1) / scale,
            (Math.Floor(y) - y - 1) / scale, width / scale, height / scale));
    }

    internal Rect Destination(Rect bounds) => new(bounds.X + Tile.X, bounds.Y + Tile.Y, Tile.Width, Tile.Height);
}
