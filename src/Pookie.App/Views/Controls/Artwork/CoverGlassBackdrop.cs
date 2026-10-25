using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Sample the fullscreen artwork, or the card's composited background layers.
// Moving either retained surface only changes sampling coordinates.
internal sealed class CoverGlassBackdrop(Image artwork, TrackPageColumns? card = null, FrameworkElement? background = null) : IDisposable
{
    private const double BlurRadius = 12;
    private IRenderSurface? raster;
    private IGraphicsContext? renderer;
    private BackdropBlur? blurred;
    private IGraphicsFactory? factory;
    private IImageSource? source;
    private Size size;
    private double sampledOpacity;
    private bool dirty, preparing;
    private long revision;
    private IImageSource? observedSource;
    private double observedOpacity = double.NaN;
    private readonly byte[] brightness = new byte[32 * 32];
    internal bool HasBlurredArtwork => blurred is { Ready: true };
    internal bool IsCurrent => HasBlurredArtwork && !preparing && !dirty && source == artwork.Source &&
        sampledOpacity == artwork.Opacity && size == BackgroundBounds.Size;
    internal bool IsCard => card != null || background != null;
    private Rect BackgroundBounds => card?.Bounds ?? background?.Bounds ?? artwork.Bounds;
    internal event Action? Changed;
    // Keep the last prepared image until the next frame snapshot is ready.
    internal void Invalidate() => dirty = true;
    internal void RefreshArtwork()
    {
        if (observedSource == artwork.Source && observedOpacity == artwork.Opacity) return;
        var changed = observedSource != artwork.Source;
        observedSource = artwork.Source; observedOpacity = artwork.Opacity;
        if (changed) Reset(); else dirty = true;
    }

    // Prepare after the window has presented. Starting another render target and
    // reading GPU pixels inside a button's opacity layer disrupts the live frame.
    internal void Prepare()
    {
        var bounds = BackgroundBounds;
        if (preparing) return;
        if ((card == null && background == null && artwork.Source == null) || bounds.Width <= 0 || bounds.Height <= 0) return;
        var graphics = Application.Current.GraphicsFactory;
        if (dirty || blurred == null || source != artwork.Source || sampledOpacity != artwork.Opacity ||
            size.Width != bounds.Width || size.Height != bounds.Height || factory != graphics)
        {
            var rendering = graphics.AcquireBackgroundRenderScope();
            try
            {
            if (raster == null || size != bounds.Size || factory != graphics)
            {
                Clear(); factory = graphics; size = bounds.Size;
                var scale = Math.Min(1, 512 / Math.Max(bounds.Width, bounds.Height));
                raster = graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                    Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)),
                    scale, debugName: "ArtworkControlGlass"));
                renderer = graphics.CreateContext(raster);
                blurred = new BackdropBlur(graphics, raster.PixelWidth, raster.PixelHeight);
            }
            source = artwork.Source; sampledOpacity = artwork.Opacity;
            observedSource = source; observedOpacity = sampledOpacity;
            renderer!.BeginFrame(raster); renderer.Save();
            try
            {
                renderer.Clear(Color.FromRgb(29, 29, 29)); renderer.Translate(-bounds.X, -bounds.Y);
                if (card != null) card.RenderGlassBackground(renderer);
                else if (background != null) background.Render(renderer);
                else artwork.Render(renderer);
            }
            finally { renderer.Restore(); renderer.EndFrame(); }
            blurred!.Capture((ICpuPixelSurface)raster);
            }
            finally { rendering.Dispose(); }
            dirty = false; preparing = true;
            var target = blurred!; var captured = revision;
            var width = raster!.PixelWidth; var height = raster.PixelHeight;
            var sigma = BlurRadius * Math.Min(1,512 / Math.Max(bounds.Width,bounds.Height)) / 3;
            var dispatcher = Application.Current.Dispatcher!;
            // No background work starts while the offscreen GL scope is held.
            _ = Task.Run(() =>
            {
                target.Process(sigma);
                dispatcher.BeginInvoke(() =>
                {
                    preparing = false;
                    if (captured != revision || !ReferenceEquals(blurred,target)) { Changed?.Invoke(); return; }
                    target.Upload();
                    var pixels = target.Pixels; var stride = width*4;
                    for(var y=0;y<32;y++)for(var x=0;x<32;x++)
                    {
                        var index = Math.Min(height-1,(int)((y+.5)/32*height))*stride + Math.Min(width-1,(int)((x+.5)/32*width))*4;
                        brightness[y*32+x]=(byte)Math.Round(pixels[index+2]*.2126+pixels[index+1]*.7152+pixels[index]*.0722);
                    }
                    Changed?.Invoke();
                });
            });
        }
    }

    internal void Draw(IGraphicsContext context, Rect region, CornerRadius corners, PathGeometry? shape = null)
    {
        if (blurred is not { Ready: true } || factory != Application.Current.GraphicsFactory) return;
        var bounds = BackgroundBounds;
        context.Save();
        try
        {
            context.SetClipPath(shape ?? GlassSheen.Shape(region, corners));
            context.DrawImage(blurred.Image, bounds);
        }
        finally { context.Restore(); }
    }

    internal double Brightness(Rect region)
    {
        if (blurred is not { Ready: true }) return 0;
        var bounds = BackgroundBounds;
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
        revision++;
        blurred?.Dispose(); blurred = null;
        renderer?.Dispose(); renderer = null; raster?.Dispose(); raster = null;
        source = null; factory = null;
    }

    public void Dispose() => Reset();
}
