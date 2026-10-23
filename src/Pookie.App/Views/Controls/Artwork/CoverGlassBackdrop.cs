using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Sample the fullscreen artwork, or the card's composited background layers.
// Moving either retained surface only changes sampling coordinates.
internal sealed class CoverGlassBackdrop(Image artwork, TrackPageColumns? card = null) : IDisposable
{
    private const double BlurRadius = 12;
    private IRenderSurface? raster;
    private IGraphicsContext? renderer;
    private BackdropBlur? blurred;
    private IGraphicsFactory? factory;
    private IImageSource? source;
    private Size size;
    private double sampledOpacity;
    private IImageSource? observedSource;
    private double observedOpacity = double.NaN;
    private readonly byte[] brightness = new byte[32 * 32];
    internal bool HasBlurredArtwork => blurred != null;
    internal bool IsCard => card != null;
    internal event Action? Changed;
    internal void RefreshArtwork()
    {
        if (observedSource == artwork.Source && observedOpacity == artwork.Opacity) return;
        observedSource = artwork.Source; observedOpacity = artwork.Opacity; Reset();
    }

    // Prepare after the window has presented. Starting another render target and
    // reading GPU pixels inside a button's opacity layer disrupts the live frame.
    internal void Prepare()
    {
        var bounds = card?.Bounds ?? artwork.Bounds;
        if ((card == null && artwork.Source == null) || bounds.Width <= 0 || bounds.Height <= 0) return;
        var graphics = Application.Current.GraphicsFactory;
        if (blurred == null || source != artwork.Source || sampledOpacity != artwork.Opacity ||
            size.Width != bounds.Width || size.Height != bounds.Height || factory != graphics)
        {
            using var rendering = graphics.AcquireBackgroundRenderScope();
            Clear(); factory = graphics; source = artwork.Source; sampledOpacity = artwork.Opacity; size = bounds.Size;
            observedSource = source; observedOpacity = sampledOpacity;
            var scale = Math.Min(1, 512 / Math.Max(bounds.Width, bounds.Height));
            raster = graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)),
                scale, debugName: "ExpandedCoverGlass"));
            renderer = graphics.CreateContext(raster);
            renderer.BeginFrame(raster); renderer.Clear(Color.FromRgb(29, 29, 29));
            renderer.Translate(-bounds.X, -bounds.Y);
            if (card == null) artwork.Render(renderer);
            else card.RenderGlassBackground(renderer);
            renderer.EndFrame();
            blurred = new BackdropBlur(graphics, raster.PixelWidth, raster.PixelHeight);
            blurred.Update((ICpuPixelSurface)raster, BlurRadius * scale / 3);
            // Share one brightness map for each background snapshot across all buttons.
            var pixels = blurred.Pixels;
            var stride = raster.PixelWidth * 4;
            for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var index = Math.Min(raster.PixelHeight - 1, (int)((y + .5) / 32 * raster.PixelHeight)) * stride +
                    Math.Min(raster.PixelWidth - 1, (int)((x + .5) / 32 * raster.PixelWidth)) * 4;
                brightness[y * 32 + x] = (byte)Math.Round(pixels[index + 2] * .2126 + pixels[index + 1] * .7152 + pixels[index] * .0722);
            }
            Changed?.Invoke();
        }
    }

    internal void Draw(IGraphicsContext context, Rect region, CornerRadius corners)
    {
        if (blurred == null || factory != Application.Current.GraphicsFactory) return;
        var bounds = card?.Bounds ?? artwork.Bounds;
        context.Save();
        try
        {
            context.SetClipPath(GlassSheen.Shape(region, corners));
            context.DrawImage(blurred.Image, bounds);
        }
        finally { context.Restore(); }
    }

    internal double Brightness(Rect region)
    {
        if (blurred == null) return 0;
        var bounds = card?.Bounds ?? artwork.Bounds;
        var total = 0d;
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
        {
            var column = (int)((region.X + region.Width * (x + .5) / 3 - bounds.X) / bounds.Width * 32);
            var row = (int)((region.Y + region.Height * (y + .5) / 3 - bounds.Y) / bounds.Height * 32);
            total += brightness[Math.Clamp(row, 0, 31) * 32 + Math.Clamp(column, 0, 31)];
        }
        return total / 9;
    }

    internal void Reset() { Clear(); Changed?.Invoke(); }

    private void Clear()
    {
        blurred?.Dispose(); blurred = null;
        renderer?.Dispose(); renderer = null; raster?.Dispose(); raster = null;
        source = null; factory = null;
    }

    public void Dispose() => Reset();
}
