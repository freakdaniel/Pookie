using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Sample the curved corners once, then copy constant straight-edge runs and
// interior rows. Retain one image: movement and hover opacity never repaint it,
// and the renderer submits one image draw rather than nine slices per control.
internal sealed class GlassRimRaster : IDisposable
{
    private DecorationRaster? raster;
    private IGraphicsFactory? factory;
    private Size paintedSize;
    private CornerRadius paintedCorners;
    private Color? paintedPigment;
    private double paintedScale;
    private Rect tile;
    internal int Rasterizations { get; private set; }
    internal long PixelBytes => raster?.PixelBytes ?? 0;

    internal void Draw(IGraphicsContext context, Rect bounds, CornerRadius corners, double minimumScale,
        Color? pigment = null, double opacity = 1, bool reuseHeight = false)
    {
        if (bounds.IsEmpty) return;
        var graphics = Application.Current.GraphicsFactory;
        var scale = Math.Max(context.DpiScale, minimumScale);
        if (reuseHeight && raster != null && factory == graphics && paintedSize.Width == bounds.Width &&
            paintedCorners == corners && paintedScale == scale && paintedPigment == pigment)
        {
            var previousAlpha = context.GlobalAlpha;
            context.GlobalAlpha = previousAlpha * (float)opacity;
            try
            {
                var target = new Rect(bounds.X + tile.X, bounds.Y + tile.Y, tile.Width,
                    tile.Height + bounds.Height - paintedSize.Height);
                var edge = Math.Max(Math.Max(corners.TopLeft, corners.TopRight), Math.Max(corners.BottomLeft, corners.BottomRight)) + 2;
                VerticalRasterStretch.Draw(context, raster.Image, target, edge);
            }
            finally { context.GlobalAlpha = previousAlpha; }
            return;
        }
        if (raster == null || factory != graphics || paintedSize != bounds.Size || paintedCorners != corners ||
            paintedScale != scale || paintedPigment != pigment)
        {
            var grid = GlassRasterGrid.For(context, bounds, minimumScale);
            var bitmap = new WriteableBitmap(grid.Width, grid.Height, clear: false);
            try
            {
                using (var write = bitmap.LockForWrite())
                    Fill(write.PixelsBgra32, grid.Width, grid.Height, bounds.Size, corners, grid.Tile, pigment);
            }
            catch { bitmap.Dispose(); throw; }
            var next = new DecorationRaster(graphics, bitmap);
            raster?.Dispose(); raster = next; factory = graphics;
            paintedSize = bounds.Size; paintedCorners = corners; paintedPigment = pigment; paintedScale = scale;
            tile = grid.Tile; Rasterizations++;
        }
        var alpha = context.GlobalAlpha;
        context.GlobalAlpha = alpha * (float)opacity;
        try { context.DrawImage(raster.Image, new Rect(bounds.X + tile.X, bounds.Y + tile.Y, tile.Width, tile.Height)); }
        finally { context.GlobalAlpha = alpha; }
    }

    private static void Fill(Span<byte> pixels, int width, int height, Size size, CornerRadius corners, Rect tile, Color? pigment)
    {
        var radius = Math.Min(size.Width, size.Height) / 2;
        corners = new CornerRadius(Math.Clamp(corners.TopLeft, 0, radius), Math.Clamp(corners.TopRight, 0, radius),
            Math.Clamp(corners.BottomRight, 0, radius), Math.Clamp(corners.BottomLeft, 0, radius));
        var pixelWidth = tile.Width / width; var pixelHeight = tile.Height / height;
        // A copied run must lie wholly outside every corner's bounding square,
        // including the sample footprint on fractional pixel boundaries.
        int Start(double value, double origin, double step, int limit) => Math.Clamp((int)Math.Ceiling((value - origin) / step), 0, limit);
        int End(double value, double origin, double step, int limit) => Math.Clamp((int)Math.Floor((value - origin) / step), 0, limit);
        var left = Start(Math.Max(corners.TopLeft, corners.BottomLeft), tile.X, pixelWidth, width);
        var right = Math.Max(left, End(size.Width - Math.Max(corners.TopRight, corners.BottomRight), tile.X, pixelWidth, width));
        var top = Start(Math.Max(corners.TopLeft, corners.TopRight), tile.Y, pixelHeight, height);
        var bottom = Math.Max(top, End(size.Height - Math.Max(corners.BottomLeft, corners.BottomRight), tile.Y, pixelHeight, height));
        var words = MemoryMarshal.Cast<byte, uint>(pixels);
        for (var row = 0; row < height; row++)
        {
            var target = words.Slice(row * width, width);
            if (row > top && row < bottom)
            {
                words.Slice(top * width, width).CopyTo(target);
                continue;
            }
            var y = tile.Y + (row + .5) * pixelHeight;
            for (var x = 0; x < left; x++)
                target[x] = Pixel(tile.X + (x + .5) * pixelWidth, y, pixelWidth, pixelHeight, size, corners, pigment);
            if (right > left)
            {
                var middle = Pixel(size.Width / 2, y, pixelWidth, pixelHeight, size, corners, pigment);
                target.Slice(left, right - left).Fill(middle);
            }
            for (var x = right; x < width; x++)
                target[x] = Pixel(tile.X + (x + .5) * pixelWidth, y, pixelWidth, pixelHeight, size, corners, pigment);
        }
        if (pigment == null)
        {
            // Bake the very faint broad sheen once as well. During motion the
            // whole reflection is composed with alpha, with no extra path draw.
            var denominator = size.Width * size.Width + size.Height * size.Height;
            var step = pixelWidth * size.Width / denominator;
            for (var y = 0; y < height; y++)
            {
                var position = ((tile.X + pixelWidth / 2) * size.Width +
                    (tile.Y + (y + .5) * pixelHeight) * size.Height) / denominator;
                for (var x = 0; x < width; x++, position += step)
                {
                    var offset = y * width + x;
                    var value = words[offset];
                    var coverage = (value >> 16) & 255;
                    if (coverage == 0) { words[offset] = 0; continue; }
                    var ambient = position < .45 ? 4 * (1 - position / .45) + position / .45
                        : 1 - .5 * (position - .45) / .55;
                    var alpha = (uint)Math.Clamp(Math.Round((value >> 24) + ambient * coverage / 255), 0, 255);
                    words[offset] = alpha << 24 | 0x00ffffff;
                }
            }
        }
    }

    private static uint Pixel(double x, double y, double pixelWidth, double pixelHeight, Size size, CornerRadius corners, Color? pigment)
    {
        var rim = GlassRim.SamplePixel(x, y, pixelWidth, pixelHeight, size, corners, pigment.HasValue ? .35 : 1);
        if (rim.Coverage == 0) return 0;
        if (pigment is not { } color)
        {
            var alpha = (uint)Math.Clamp(Math.Round(255 * rim.Reflection * GlassSheen.ReflectionOpacity), 0, 255);
            // Temporarily retain silhouette coverage for the cheap ambient
            // pass; no second optical sample is needed for the interior.
            return alpha << 24 | (uint)Math.Round(rim.Coverage * 255) << 16;
        }
        byte Channel(byte value) => (byte)Math.Clamp(Math.Round(value + (255 - value) * rim.Reflection / rim.Coverage), 0, 255);
        var coverage = (uint)Math.Clamp(Math.Round(color.A * (rim.Coverage - rim.Transmission)), 0, 255);
        return coverage << 24 | (uint)Channel(color.R) << 16 | (uint)Channel(color.G) << 8 | Channel(color.B);
    }

    public void Dispose() { raster?.Dispose(); raster = null; factory = null; }
}
