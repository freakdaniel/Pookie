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
        context.DrawImage(image!, new Rect(region.X, region.Y, width / scale, height / scale));
    }

    private void ClearRaster()
    {
        image?.Dispose(); image = null;
        renderer?.Dispose(); renderer = null;
        raster?.Dispose(); raster = null;
    }
    protected override void OnDispose() { ClearRaster(); base.OnDispose(); }
}
