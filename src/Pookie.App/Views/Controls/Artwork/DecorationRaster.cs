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
            Image = ((IImageSource)bitmap).CreateImage(factory);
        }
        catch { bitmap.Dispose(); throw; }
    }

    public void Dispose() { Image.Dispose(); bitmap.Dispose(); }
}

// Ownership follows the control: retained scene commands can still reference
// its image between frames, so a global LRU must not dispose another view's ink.
internal sealed class GlassSheenRaster : IDisposable
{
    private DecorationRaster? raster;
    private (IGraphicsFactory Factory, Size Size, CornerRadius Corners, double Emphasis, double Scale)? key;

    internal void Draw(IGraphicsContext context, Rect bounds, CornerRadius corners, double emphasis = 1)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (!OperatingSystem.IsWindows()) { GlassSheen.Draw(context, bounds, corners, emphasis); return; }
        var graphics = Application.Current.GraphicsFactory;
        var next = (graphics, bounds.Size, corners, emphasis, context.DpiScale);
        if (key != next)
        {
            raster?.Dispose(); raster = null; key = null;
            raster = new DecorationRaster(graphics, bounds.Size, context.DpiScale,
                (renderer, region) => GlassSheen.Draw(renderer, region, corners, emphasis));
            key = next;
        }
        context.DrawImage(raster!.Image, bounds);
    }

    public void Dispose() { raster?.Dispose(); raster = null; key = null; }
}
