using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Rounded shadow corners are fixed-size; only the flat edge/centre stretch.
// Keep a small nine-slice raster instead of generating many radial/linear
// gradient brushes on every scrolling frame or caching a full card-sized image.
internal sealed class BoxShadowRaster(double blur, double offset, Color color) : IDisposable
{
    private DecorationRaster? raster;
    private IGraphicsFactory? factory;
    private double radius = -1, scale;
    private Size patchSize;
    private int left, top, right, bottom;
    internal long PixelBytes => raster?.PixelBytes ?? 0;

    internal void Draw(IGraphicsContext context, Rect bounds, double cornerRadius, double opacity = 1)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var nextRadius = Math.Max(0, Math.Min(cornerRadius, Math.Min(bounds.Width, bounds.Height) / 2));
        if (!OperatingSystem.IsWindows())
        {
            context.DrawBoxShadow(bounds, nextRadius, blur,
                Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B), offsetY: offset);
            return;
        }
        var graphics = Application.Current.GraphicsFactory;
        var nextScale = context.DpiScale;
        var corner = nextRadius + blur;
        var side = corner * 2 + 1;
        var patch = new Size(Math.Min(side, bounds.Width), Math.Min(side, bounds.Height));
        if (raster == null || factory != graphics || radius != nextRadius || scale != nextScale || patchSize != patch)
        {
            Dispose(); factory = graphics; radius = nextRadius; scale = nextScale; patchSize = patch;
            var size = new Size(patch.Width + blur * 2, patch.Height + blur * 2);
            raster = new DecorationRaster(graphics, size, scale, (renderer, _) =>
                renderer.DrawBoxShadow(new Rect(blur, blur - offset, patch.Width, patch.Height), radius, blur, color, offsetY: offset));
            left = Math.Min((raster.Image.PixelWidth - 1) / 2, (int)Math.Ceiling((blur + corner) * scale));
            top = Math.Min((raster.Image.PixelHeight - 1) / 2, (int)Math.Ceiling((blur + corner) * scale));
            right = raster.Image.PixelWidth - left - 1;
            bottom = raster.Image.PixelHeight - top - 1;
        }
        var image = raster.Image;
        var transform = context.GetTransform();
        var x1 = Math.Round((bounds.X - blur + transform.M31) * scale) / scale - transform.M31;
        var y1 = Math.Round((bounds.Y - blur + offset + transform.M32) * scale) / scale - transform.M32;
        var x2 = Math.Ceiling((bounds.Right + blur + transform.M31) * scale) / scale - transform.M31;
        var y2 = Math.Ceiling((bounds.Bottom + blur + offset + transform.M32) * scale) / scale - transform.M32;
        var target = new Rect(x1, y1, x2 - x1, y2 - y1);
        // Small controls can be narrower than both full shadow corners. Scaling
        // the outer slices together retains their shape without negative spans.
        var horizontal = Math.Min(1, target.Width * scale / (left + right));
        var vertical = Math.Min(1, target.Height * scale / (top + bottom));
        Span<double> xs = stackalloc double[] { target.X, target.X + left / scale * horizontal, target.Right - right / scale * horizontal, target.Right };
        Span<double> ys = stackalloc double[] { target.Y, target.Y + top / scale * vertical, target.Bottom - bottom / scale * vertical, target.Bottom };
        Span<int> sx = stackalloc int[] { 0, left, image.PixelWidth - right, image.PixelWidth };
        Span<int> sy = stackalloc int[] { 0, top, image.PixelHeight - bottom, image.PixelHeight };
        var alpha = context.GlobalAlpha;
        context.GlobalAlpha = alpha * (float)opacity;
        try
        {
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
                if (xs[x + 1] > xs[x] && ys[y + 1] > ys[y])
                    context.DrawImage(image, new Rect(xs[x], ys[y], xs[x + 1] - xs[x], ys[y + 1] - ys[y]),
                        new Rect(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]));
        }
        finally { context.GlobalAlpha = alpha; }
    }

    public void Dispose() { raster?.Dispose(); raster = null; factory = null; }
}
