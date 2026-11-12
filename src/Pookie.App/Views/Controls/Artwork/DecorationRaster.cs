using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

// CPU-backed images can be sampled by both the window and independent header
// captures. A target-bound Direct2D bitmap cannot serve both of those contexts.
internal sealed class DecorationRaster : IDisposable
{
    private readonly WriteableBitmap bitmap;
    internal IImage Image { get; }
    internal long PixelBytes { get; }

    internal DecorationRaster(IGraphicsFactory factory, WriteableBitmap source)
    {
        bitmap = source;
        PixelBytes = (long)source.PixelWidth * source.PixelHeight * 4;
        try { Image = BitmapImage.Create(factory, bitmap); }
        catch { bitmap.Dispose(); throw; }
    }

    internal DecorationRaster(IGraphicsFactory factory, Size size, double scale, Action<IGraphicsContext, Rect> draw)
    {
        var width = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
        PixelBytes = (long)width * height * 4;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, scale, debugName: "StaticDecoration"));
        bitmap = new WriteableBitmap(width, height, clear: false);
        try
        {
            using var context = factory.CreateContext(surface);
            context.BeginFrame(surface);
            try { context.Clear(Color.Transparent); draw(context, new Rect(0, 0, size.Width, size.Height)); }
            finally { context.EndFrame(); }
            var pixels = (ICpuPixelSurface)surface;
            using (var write = bitmap.LockForWrite())
            {
                for (var y = 0; y < height; y++)
                    pixels.GetReadOnlyPixelSpan().Slice(y * pixels.StrideBytes, width * 4)
                        .CopyTo(write.PixelsBgra32.Slice(y * width * 4, width * 4));
                // Direct2D's transparent DC target leaves premultiplied ink in
                // its DIB, while WriteableBitmap uploads straight-alpha BGRA.
                // Without this conversion a white 23/255 highlight becomes 2/255.
                var output = write.PixelsBgra32;
                for (var i = 0; i < output.Length; i += 4)
                {
                    var alpha = output[i + 3];
                    if (alpha is 0 or 255) continue;
                    for (var channel = 0; channel < 3; channel++)
                        output[i + channel] = (byte)Math.Min(255, (output[i + channel] * 255 + alpha / 2) / alpha);
                }
            }
            Image = BitmapImage.Create(factory, bitmap);
        }
        catch { bitmap.Dispose(); throw; }
    }

    public void Dispose() { Image.Dispose(); bitmap.Dispose(); }
}

// Ownership follows the control: retained scene commands can still reference
// its image between frames, so a global LRU must not dispose another view's ink.
internal sealed class GlassSheenRaster : IDisposable
{
    private readonly GlassRimRaster patch = new();
    internal int Rasterizations => patch.Rasterizations;
    internal long PixelBytes => patch.PixelBytes;

    internal void Draw(IGraphicsContext context, Rect bounds, CornerRadius corners, double emphasis = 1,
        double minimumScale = 0, bool resizing = false, Action? invalidateAfterResize = null, bool reuseHeight = false)
    {
        // Hover is composition alpha, not a new optical raster. The image is
        // local to the control and follows fractional motion through filtering.
        patch.Draw(context, bounds, corners, minimumScale, opacity: .85 + .15 * Math.Clamp(emphasis, 0, 1), reuseHeight: reuseHeight);
    }

    internal static void FillPixels(Span<byte> pixels, int width, int height, Size size, CornerRadius corners, double emphasis,
        bool premultiplied = false, Rect? tileBounds = null, bool reflectionOnly = false)
    {
        var tile = tileBounds ?? new Rect(0, 0, size.Width, size.Height);
        var denominator = size.Width * size.Width + size.Height * size.Height;
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte,uint>(pixels);
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var localY = tile.Y + (y + .5) / height * tile.Height;
            for (var x = 0; x < width; x++)
            {
                var localX = tile.X + (x + .5) / width * tile.Width;
                var position = (localX * size.Width + localY * size.Height) / denominator;
                var ambient = position < .45 ? (3 + emphasis) * (1 - position / .45) + position / .45
                    : 1 - .5 * (position - .45) / .55;
                var rim = GlassRim.SamplePixel(localX, localY, tile.Width / width, tile.Height / height, size, corners, emphasis);
                var alpha = (reflectionOnly ? 0 : ambient * rim.Coverage) + 255 * rim.Reflection * GlassSheen.ReflectionOpacity;
                // A stable, neutral one-level dither preserves fractional alpha
                // without visible grain, moving noise or changes to the glass tint.
                var coverage = rim.Coverage == 0 ? (byte)0 : (byte)Math.Clamp(alpha + PlayerGradient.Noise(x, y), 0, 255);
                // Premultiplied white has identical BGRA bytes. One store also
                // avoids three extra bounds checks in the animated reflection.
                if (premultiplied) words[row + x] = (uint)coverage * 0x01010101u;
                else
                {
                    var offset = (row + x) * 4;
                    pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 255;
                    pixels[offset + 3] = coverage;
                }
            }
        }
    }

    public void Dispose() { patch.Dispose(); }
}
