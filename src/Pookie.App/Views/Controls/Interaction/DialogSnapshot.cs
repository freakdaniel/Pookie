using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;

namespace Pookie.App;

// Device-local, bounded captures. Filter scratch is reused for the transition
// and released with the dialog; no frame is read back to managed pixel arrays.
internal sealed class DialogSnapshot : IDisposable
{
    private readonly IGraphicsFactory graphics;
    private readonly IImageFilterExecutor executor;
    private readonly IRenderSurface surface;
    private readonly IGraphicsContext renderer;
    private IImage? image;
    private DefaultFilterContext? filters;
    private FilterResult? blurred;
    private double radius = -1;
    internal Rect Bounds { get; }
    internal double Scale { get; }
    internal IImage Image => blurred?.AsImage() ?? image!;
    internal Rect ImageBounds => blurred?.Bounds ?? Bounds;
    internal IRenderSurface CaptureSurface => surface;
    internal IRenderSurface? BlurSurface => blurred?.UnderlyingSurface;

    internal DialogSnapshot(IGraphicsFactory graphics, Rect bounds, double scale)
    {
        using var scope = graphics.AcquireBackgroundRenderScope();
        this.graphics = graphics; Bounds = bounds; Scale = scale;
        surface = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(
            Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)), scale,
            debugName: "DialogSnapshot"));
        renderer = graphics.CreateContext(surface);
        executor = graphics.CreateImageFilterExecutor();
    }

    internal void Capture(Action<IGraphicsContext> draw, Color clear)
    {
        using var scope = graphics.AcquireBackgroundRenderScope();
        renderer.BeginFrame(surface);
        renderer.Save();
        using var viewport = WindowPresentation.CaptureViewport(surface);
        try { renderer.ResetTransform(); renderer.Clear(clear); renderer.Translate(-Bounds.X, -Bounds.Y); draw(renderer); }
        finally { renderer.Restore(); renderer.EndFrame(); }
        FinishCapture();
    }

    internal bool CopyBackdrop(IGraphicsContext context, Rect pixelRegion)
    {
        if (!OpenGlBackdropCopy.TryCopy(context, surface, pixelRegion)) return false;
        FinishCapture();
        return true;
    }

    private void FinishCapture()
    {
        if (image == null)
        {
            image = graphics.CreateImageView(surface);
            filters = new(surface, image, Bounds, graphics, Scale, Scale);
        }
        radius = -1;
    }

    internal void Blur(double next)
    {
        next = Math.Max(0, next);
        if (Math.Abs(radius - next) < .001) return;
        using var scope = graphics.AcquireBackgroundRenderScope();
        var result = next > .01 ? executor.Execute(new BlurFilter(next), filters!) : null;
        var previous = blurred; blurred = result; previous?.Dispose(); radius = next;
    }

    public void Dispose()
    {
        blurred?.Dispose(); blurred = null; filters?.Dispose();
        image?.Dispose(); renderer.Dispose(); surface.Dispose();
    }
}
