using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// CPU-backed images can be sampled by both the window and independent header
// captures. A target-bound Direct2D bitmap cannot serve both of those contexts.
internal sealed class DecorationRaster : IDisposable
{
    private readonly WriteableBitmap bitmap;
    internal IImage Image { get; }
    internal long PixelBytes { get; }

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
    private WriteableBitmap? bitmap;
    private IImage? image;
    private IGraphicsFactory? factory;
    private double paintedEmphasis = double.NaN;
    private readonly GlassGeometry clip = new(), edge = new();
    private Rect edgeBounds;
    private double edgeEmphasis = double.NaN;
    private Pen? edgePen;

    internal void Draw(IGraphicsContext context, Rect bounds, CornerRadius corners, double emphasis = 1, double minimumScale = 0)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var graphics = Application.Current.GraphicsFactory;
        // A smaller header-blur capture can share the view's full-resolution
        // raster instead of reallocating it between capture and window frames.
        var scale = Math.Max(context.DpiScale, minimumScale);
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        if (image == null || factory != graphics || image.PixelWidth != width || image.PixelHeight != height)
        {
            Dispose(); factory = graphics;
            bitmap = new WriteableBitmap(width, height, clear: false);
            image = OperatingSystem.IsWindows() ? BitmapImage.CreatePremultipliedView(graphics, bitmap) : BitmapImage.Create(graphics, bitmap);
        }
        if (paintedEmphasis != emphasis)
        {
            using var write = bitmap!.LockForWrite();
            FillPixels(write.PixelsBgra32, width, height, emphasis, OperatingSystem.IsWindows());
            paintedEmphasis = emphasis;
        }
        // Quantize only at the final physical pixel size. Stretching a small
        // dithered texture would average the noise away and restore the bands.
        context.Save();
        try
        {
            // Scrolling only moves the coordinate system. Keeping paths and
            // gradient endpoints local lets Direct2D reuse geometry and stop
            // collections instead of filling its caches with every position.
            context.Translate(bounds.X, bounds.Y);
            var local = new Rect(0, 0, bounds.Width, bounds.Height);
            context.Save();
            try
            {
                context.SetClipPath(clip.Get(local, corners));
                context.DrawImage(image!, local);
            }
            finally { context.Restore(); }
            DrawEdge(context, local, corners, emphasis);
        }
        finally { context.Restore(); }
    }

    private void DrawEdge(IGraphicsContext context, Rect bounds, CornerRadius corners, double emphasis)
    {
        if (edgePen == null || edgeBounds != bounds || edgeEmphasis != emphasis)
        {
            edgeBounds = bounds; edgeEmphasis = emphasis;
            edgePen = new Pen(new LinearGradientBrush(new Point(bounds.X,bounds.Y),new Point(bounds.X,bounds.Bottom),
                [new(0,Color.FromArgb((byte)(28+emphasis*16),255,255,255)),new(.55,Color.FromArgb(9,255,255,255)),new(1,Color.FromArgb(4,255,255,255))]),1);
        }
        var insetBounds = new Rect(bounds.X+.5,bounds.Y+.5,Math.Max(0,bounds.Width-1),Math.Max(0,bounds.Height-1));
        var insetCorners = new CornerRadius(Math.Max(0,corners.TopLeft-.5),Math.Max(0,corners.TopRight-.5),Math.Max(0,corners.BottomRight-.5),Math.Max(0,corners.BottomLeft-.5));
        context.DrawPath(edge.Get(insetBounds,insetCorners),edgePen);
    }

    internal static void FillPixels(Span<byte> pixels, int width, int height, double emphasis, bool premultiplied = false)
    {
        var denominator = (double)width * width + (double)height * height;
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte,uint>(pixels);
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var vertical = (y + .5) * height;
            for (var x = 0; x < width; x++)
            {
                var position = ((x + .5) * width + vertical) / denominator;
                var alpha = position < .45 ? (17 + emphasis * 6) * (1 - position / .45) + 5 * position / .45
                    : 5 - 4 * (position - .45) / .55;
                // A stable, neutral one-level dither preserves fractional alpha
                // without visible grain, moving noise or changes to the glass tint.
                var coverage = (byte)Math.Clamp(alpha + PlayerGradient.Noise(x, y), 0, 255);
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

    public void Dispose()
    {
        image?.Dispose(); image = null; bitmap?.Dispose(); bitmap = null;
        factory = null; paintedEmphasis = double.NaN;
    }
}
