using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

// Render the strip at the page's DPI before downsampling. Drawing the live tree
// at quarter resolution snaps moving artwork to a different pixel grid.
internal sealed class GlassContentFrame : Panel
{
    private readonly FrameworkElement content;
    private IRenderSurface? raster;
    private IGraphicsContext? renderer;
    private IImage? image;
    private IGraphicsFactory? factory;
    private RenderDeviceIdentity? deviceIdentity;
    private nint contextIdentity;
    private readonly List<SampleLevel> samples = [];

    internal GlassContentFrame(FrameworkElement content) { this.content = content; Add(content); }
    internal IGraphicsFactory CaptureGraphics => Application.Current.GraphicsFactory;
    internal IRenderSurface CreateCaptureSurface(IGraphicsFactory graphics, int width, int height, double scale, string name) =>
        OperatingSystem.IsWindows()
            ? graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(width, height, scale, debugName: name))
            : BackdropSurface.Create(graphics, width, height, scale, name);
    protected override Size MeasureContent(Size availableSize)
    { content.Measure(availableSize); return content.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize)
    { content.Arrange(Bounds); return finalSize; }

    internal IRenderSurface CaptureBackdrop(Rect region, Color background)
    {
        var scale = GetDpi() / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(region.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(region.Height * scale));
        var graphics = CaptureGraphics;
        var currentContext = Win32BackdropBlur.CurrentContextIdentity;
        var currentDevice = (graphics as IRenderDevice)?.RenderIdentity;
        if (raster == null || raster.PixelWidth != width || raster.PixelHeight != height || raster.DpiScale != scale ||
            factory != graphics || contextIdentity != currentContext || deviceIdentity != currentDevice)
        {
            ClearRaster(); factory = graphics; contextIdentity = currentContext;
            raster = CreateCaptureSurface(graphics, width, height, scale, "HeaderNativeStrip");
            renderer = graphics.CreateContext(raster);
            deviceIdentity = (graphics as IRenderDevice)?.RenderIdentity;
        }
        var version = (raster as IRasterSource)?.Version;
        renderer!.BeginFrame(raster!);
        try
        {
            renderer.Clear(BackdropSurface.ClearColor(background));
            renderer.Translate(-region.X, -region.Y);
            renderer.SetClip(region);
            base.RenderSubtree(renderer);
        }
        finally { renderer.EndFrame(); }
        if (BackdropSurface.UsesCpuCapture && raster is IExternalWritableGpuSurface completed &&
            (raster as IRasterSource)?.Version == version)
        {
            // The retained frame may still own the shared D2D BeginDraw. EndFrame
            // then restores its target without flushing or versioning this nested
            // capture. Submit its pixels and invalidate the cached readback before
            // reading them; never finish the enclosing window's draw ourselves.
            using (var write = completed.BeginExternalWrite()) write.Flush();
            completed.MarkExternalContentChanged();
        }
        return raster;
    }

    internal (IImage Image, Rect Bounds) PrepareBackdrop(IRenderSurface captured, Rect region, Color background, double targetScale)
    {
        var scale = captured.DpiScale;
        var width = captured.PixelWidth;
        var height = captured.PixelHeight;
        var graphics = factory!;
        image ??= graphics.CreateImageView(captured);
        // A single bilinear sample from (for example) 2x DPI to .25x misses
        // most of each eight-row footprint. Text and artwork then stand still
        // and jump as they cross that sampling grid. Halving in aligned stages
        // averages each 2x2 footprint before the next reduction.
        var source = image!;
        var sourceBounds = new Rect(region.X, region.Y, width / scale, height / scale);
        var sampleScale = scale;
        var level = 0;
        while (sampleScale > targetScale * 2 + .0001)
        {
            sampleScale /= 2;
            var sampleWidth = Math.Max(1, (int)Math.Ceiling(region.Width * sampleScale));
            var sampleHeight = Math.Max(1, (int)Math.Ceiling(region.Height * sampleScale));
            if (level == samples.Count) samples.Add(new SampleLevel(this, graphics, sampleWidth, sampleHeight, sampleScale));
            var sample = samples[level++];
            sample.Draw(source, sourceBounds, region, background);
            source = sample.Image;
            sourceBounds = new Rect(region.X, region.Y, sampleWidth / sampleScale, sampleHeight / sampleScale);
        }
        return (source, sourceBounds);
    }

    private void ClearRaster()
    {
        foreach (var sample in samples) sample.Dispose();
        samples.Clear();
        image?.Dispose(); image = null;
        renderer?.Dispose(); renderer = null;
        raster?.Dispose(); raster = null;
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) ClearRaster();
    }

    protected override void OnDispose() { ClearRaster(); base.OnDispose(); }

    private sealed class SampleLevel : IDisposable
    {
        private readonly IRenderSurface surface;
        private readonly IGraphicsContext renderer;
        internal IImage Image { get; }
        internal SampleLevel(GlassContentFrame owner, IGraphicsFactory graphics, int width, int height, double scale)
        {
            surface = owner.CreateCaptureSurface(graphics, width, height, scale, "HeaderDownsample");
            renderer = graphics.CreateContext(surface);
            Image = graphics.CreateImageView(surface);
        }
        internal void Draw(IImage source, Rect sourceBounds, Rect region, Color background)
        {
            renderer.BeginFrame(surface);
            try
            {
                renderer.Clear(BackdropSurface.ClearColor(background));
                renderer.Translate(-region.X, -region.Y);
                renderer.DrawImage(source, sourceBounds);
            }
            finally { renderer.EndFrame(); }
        }
        public void Dispose() { Image.Dispose(); renderer.Dispose(); surface.Dispose(); }
    }
}
