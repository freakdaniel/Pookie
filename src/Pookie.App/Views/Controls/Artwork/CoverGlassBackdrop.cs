using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Sample the fullscreen artwork, or the card's composited background layers.
// Moving either retained surface only changes sampling coordinates.
internal sealed class CoverGlassBackdrop(Image artwork, TrackPageColumns? card = null, FrameworkElement? background = null,
    double centeredSampleSize = 0, bool deferRefraction = false) : IDisposable
{
    private const double BlurRadius = 12;
    private IRenderSurface? raster;
    private IGraphicsContext? renderer;
    private BackdropBlur? blurred;
    private BackdropBlur? resizeSample;
    private BackdropBlur? outgoing;
    private byte[]? outgoingBrightness;
    private Func<double>? outgoingOpacity;
    private IGraphicsFactory? factory;
    private IImageSource? source;
    private Size size, sampledArtworkSize;
    private double sampledOpacity;
    private bool dirty, preparing;
    private long revision;
    private IImageSource? observedSource;
    private double observedOpacity = double.NaN;
    private byte[] brightness = new byte[32 * 32];
    internal bool HasArtwork => artwork.Source != null;
    internal bool HasBitmapArtwork => artwork.Source is ImageSource;
    internal bool HasBlurredArtwork => blurred is { Ready: true };
    internal bool IsBlending => outgoing != null;
    internal bool IsCurrent => HasBlurredArtwork && !preparing && !dirty && source == artwork.Source &&
        sampledOpacity == artwork.Opacity && sampledArtworkSize == ArtworkBounds.Size;
    internal bool NeedsPrepare => !preparing && !IsCurrent;
    internal bool CanReuseWhileScrolling => deferRefraction && (HasBlurredArtwork || resizeSample != null);
    internal bool IsCard => card != null || background != null;
    private Rect ArtworkBounds => card?.Bounds ?? background?.Bounds ?? artwork.Bounds;
    private Rect BackgroundBounds
    {
        get
        {
            var bounds = ArtworkBounds;
            if (centeredSampleSize <= 0) return bounds;
            var width = Math.Min(bounds.Width, centeredSampleSize);
            var height = Math.Min(bounds.Height, centeredSampleSize);
            return new Rect(bounds.Center.X - width / 2, bounds.Center.Y - height / 2, width, height);
        }
    }
    internal event Action? Changed;
    // Keep the last prepared image until the next frame snapshot is ready.
    internal void Invalidate() => dirty = true;
    internal void RefreshArtwork(bool keepCurrent = false)
    {
        if (observedSource == artwork.Source && observedOpacity == artwork.Opacity) return;
        var changed = observedSource != artwork.Source;
        observedSource = artwork.Source; observedOpacity = artwork.Opacity;
        if (changed && !keepCurrent) { Clear(); Changed?.Invoke(); }
        else dirty = true;
    }

    // Prepare after the window has presented. Starting another render target and
    // reading GPU pixels inside a button's opacity layer disrupts the live frame.
    internal bool Prepare()
    {
        var bounds = BackgroundBounds;
        if (preparing || (card == null && background == null && artwork.Source == null) ||
            bounds.Width <= 0 || bounds.Height <= 0) return false;
        var graphics = Application.Current.GraphicsFactory;
        if (dirty || blurred == null || source != artwork.Source || sampledOpacity != artwork.Opacity ||
            sampledArtworkSize != ArtworkBounds.Size || factory != graphics)
        {
            var rendering = graphics.AcquireBackgroundRenderScope();
            try
            {
            if (raster == null || size != bounds.Size || factory != graphics)
            {
                var previous = deferRefraction && factory == graphics && source == artwork.Source
                    ? blurred is { Ready: true } ? blurred : resizeSample : null;
                if (ReferenceEquals(previous, blurred)) blurred = null;
                if (ReferenceEquals(previous, resizeSample)) resizeSample = null;
                Clear(); factory = graphics; size = bounds.Size;
                resizeSample = previous;
                var scale = Math.Min(1, 512 / Math.Max(bounds.Width, bounds.Height));
                raster = graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                    Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)),
                    scale, debugName: "ArtworkControlGlass"));
                renderer = graphics.CreateContext(raster);
            }
            blurred ??= new BackdropBlur(graphics, raster.PixelWidth, raster.PixelHeight, preserveSharp: true);
            source = artwork.Source; sampledOpacity = artwork.Opacity; sampledArtworkSize = ArtworkBounds.Size;
            observedSource = source; observedOpacity = sampledOpacity;
            renderer!.BeginFrame(raster); renderer.Save();
            using var captureViewport = WindowPresentation.CaptureViewport(raster);
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
                    resizeSample?.Dispose(); resizeSample = null;
                    var pixels = target.Pixels; var stride = width*4;
                    for(var y=0;y<32;y++)for(var x=0;x<32;x++)
                    {
                        var index = Math.Min(height-1,(int)((y+.5)/32*height))*stride + Math.Min(width-1,(int)((x+.5)/32*width))*4;
                        brightness[y*32+x]=(byte)Math.Round(pixels[index+2]*.2126+pixels[index+1]*.7152+pixels[index]*.0722);
                    }
                    Changed?.Invoke();
                });
            });
            return true;
        }
        return false;
    }

    internal void BeginBlend(CoverGlassBackdrop staged, Func<double> previousOpacity)
    {
        ReleaseOutgoing();
        outgoing = blurred;
        outgoingBrightness = brightness;
        outgoingOpacity = previousOpacity;
        (blurred, staged.blurred) = (staged.blurred, null);
        (raster, staged.raster) = (staged.raster, raster);
        (renderer, staged.renderer) = (staged.renderer, renderer);
        (factory, staged.factory) = (staged.factory, factory);
        (size, staged.size) = (staged.size, size);
        brightness = staged.brightness;
        staged.brightness = new byte[32 * 32];
        staged.source = null;
        staged.dirty = true;
        source = observedSource = artwork.Source;
        sampledOpacity = observedOpacity = artwork.Opacity;
        sampledArtworkSize = ArtworkBounds.Size;
        dirty = false;
        Changed?.Invoke();
    }

    internal void RefreshBlend() => Changed?.Invoke();

    internal void EndBlend(CoverGlassBackdrop staged)
    {
        if (outgoing == null) return;
        // The other slot is idle during the blend. Reuse its full-cover pixel
        // buffers on the next switch when the cover geometry still matches.
        if (staged.blurred == null && staged.size == size && staged.factory == factory)
        {
            staged.blurred = outgoing;
            staged.brightness = outgoingBrightness!;
            outgoing = null;
        }
        ReleaseOutgoing();
        Changed?.Invoke();
    }

    private void ReleaseOutgoing()
    {
        outgoing?.Dispose(); outgoing = null;
        outgoingBrightness = null; outgoingOpacity = null;
    }

    internal void Draw(IGraphicsContext context, Rect region, CornerRadius corners, PathGeometry? shape = null,
        LiquidGlassLens? lens = null, double minimumScale = 1, Action? invalidate = null)
    {
        var redrawLens = deferRefraction ? invalidate : null;
        var bounds = BackgroundBounds;
        var staleSize = deferRefraction && sampledArtworkSize != ArtworkBounds.Size;
        if (blurred is not { Ready: true } || factory == null || factory != Application.Current.GraphicsFactory)
        {
            if (resizeSample is { } previousSample && factory == Application.Current.GraphicsFactory)
            {
                if (lens?.DrawPrepared(context, region, corners, shape) != true) DrawSample(previousSample, null);
            }
            else if (card != null && lens != null)
                lens.Draw(context, Application.Current.GraphicsFactory, null, bounds, region, corners, shape, minimumScale);
            else lens?.Dispose();
            return;
        }
        if (outgoing is { } previous && outgoingOpacity is { } opacity)
        {
            var previousAmount = opacity();
            if (previousAmount > 0) DrawSample(previous, lens?.Outgoing);
            if (previousAmount < 1)
            {
                var alpha = context.GlobalAlpha;
                try
                {
                    context.GlobalAlpha = alpha * (float)(1 - previousAmount);
                    DrawSample(blurred, lens);
                }
                finally { context.GlobalAlpha = alpha; }
            }
            return;
        }
        lens?.ClearOutgoing();
        DrawSample(blurred, lens);

        void DrawSample(BackdropBlur sample, LiquidGlassLens? sampleLens)
        {
            // Until the resized sample is ready, reuse the old lens. A job
            // against the old sample and new bounds would be discarded.
            if ((staleSize ? sampleLens?.DrawPrepared(context, region, corners, shape)
                : sampleLens?.Draw(context, factory!, sample, bounds, region, corners, shape, minimumScale, redrawLens)) == true)
                return;
            context.Save();
            try
            {
                context.SetClipPath(shape ?? GlassSheen.Shape(region, corners));
                context.DrawImage(sample.Image, bounds);
            }
            finally { context.Restore(); }
        }
    }

    internal double Brightness(Rect region)
    {
        if (blurred is not { Ready: true }) return 0;
        var bounds = BackgroundBounds;
        var previousAmount = outgoingOpacity?.Invoke() ?? 0;
        var total = 0d;
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
        {
            var column = (int)((region.X + region.Width * (x + .5) / 3 - bounds.X) / bounds.Width * 32);
            var row = (int)((region.Y + region.Height * (y + .5) / 3 - bounds.Y) / bounds.Height * 32);
            var index = Math.Clamp(row, 0, 31) * 32 + Math.Clamp(column, 0, 31);
            var value = brightness[index];
            total += outgoingBrightness is { } previous ? value + (previous[index] - value) * previousAmount : value;
        }
        return total / 9;
    }

    internal void Reset()
    {
        observedSource = null;
        observedOpacity = double.NaN;
        Clear();
        Changed?.Invoke();
    }

    private void Clear()
    {
        revision++;
        ReleaseOutgoing();
        resizeSample?.Dispose(); resizeSample = null;
        blurred?.Dispose(); blurred = null;
        renderer?.Dispose(); renderer = null; raster?.Dispose(); raster = null;
        source = null; factory = null;
    }

    public void Dispose() => Reset();
}
