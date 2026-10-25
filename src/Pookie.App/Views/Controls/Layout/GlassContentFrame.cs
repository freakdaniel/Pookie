using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

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
    private nint contextIdentity;
    private readonly List<SampleLevel> samples = [];

    internal GlassContentFrame(FrameworkElement content) { this.content = content; Add(content); }
    protected override Size MeasureContent(Size availableSize)
    { content.Measure(availableSize); return content.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize)
    { content.Arrange(Bounds); return finalSize; }

    internal void DrawBackdrop(IGraphicsContext context, Rect region)
    {
        var scale = GetDpi() / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(region.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(region.Height * scale));
        var graphics = Application.Current.GraphicsFactory;
        var currentContext = Win32BackdropBlur.CurrentContextIdentity;
        if (raster == null || raster.PixelWidth != width || raster.PixelHeight != height || raster.DpiScale != scale ||
            factory != graphics || contextIdentity != currentContext)
        {
            ClearRaster(); factory = graphics; contextIdentity = currentContext;
            raster = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(width, height, scale, debugName: "HeaderNativeStrip"));
            renderer = graphics.CreateContext(raster);
            image = graphics.CreateImageView(raster);
        }
        renderer!.BeginFrame(raster!);
        try
        {
            renderer.Clear(Color.Transparent);
            renderer.Translate(-region.X, -region.Y);
            renderer.SetClip(region);
            base.RenderSubtree(renderer);
        }
        finally { renderer.EndFrame(); }
        // A single bilinear sample from (for example) 2x DPI to .25x misses
        // most of each eight-row footprint. Text and artwork then stand still
        // and jump as they cross that sampling grid. Halving in aligned stages
        // averages each 2x2 footprint before the next reduction.
        var source = image!;
        var sourceBounds = new Rect(region.X, region.Y, width / scale, height / scale);
        var sampleScale = scale;
        var level = 0;
        while (sampleScale > context.DpiScale * 2 + .0001)
        {
            sampleScale /= 2;
            var sampleWidth = Math.Max(1, (int)Math.Ceiling(region.Width * sampleScale));
            var sampleHeight = Math.Max(1, (int)Math.Ceiling(region.Height * sampleScale));
            if (level == samples.Count) samples.Add(new SampleLevel(graphics, sampleWidth, sampleHeight, sampleScale));
            var sample = samples[level++];
            sample.Draw(source, sourceBounds, region);
            source = sample.Image;
            sourceBounds = new Rect(region.X, region.Y, sampleWidth / sampleScale, sampleHeight / sampleScale);
        }
        context.DrawImage(source, sourceBounds);
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
        internal SampleLevel(IGraphicsFactory graphics, int width, int height, double scale)
        {
            surface = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(width, height, scale, debugName: "HeaderDownsample"));
            renderer = graphics.CreateContext(surface);
            Image = graphics.CreateImageView(surface);
        }
        internal void Draw(IImage source, Rect sourceBounds, Rect region)
        {
            renderer.BeginFrame(surface);
            try
            {
                renderer.Clear(Color.Transparent);
                renderer.Translate(-region.X, -region.Y);
                renderer.DrawImage(source, sourceBounds);
            }
            finally { renderer.EndFrame(); }
        }
        public void Dispose() { Image.Dispose(); renderer.Dispose(); surface.Dispose(); }
    }
}
