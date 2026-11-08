using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Captures only an overlay's footprint and halo, at a bounded resolution.
internal sealed class LiquidGlassSnapshot : IDisposable
{
    private readonly IGraphicsFactory graphics;
    private readonly IRenderSurface surface;
    private readonly IGraphicsContext renderer;
    private readonly BackdropBlur backdrop;
    internal Rect Bounds { get; }
    internal double Scale { get; }
    internal bool Ready => backdrop.Ready;

    internal LiquidGlassSnapshot(IGraphicsFactory graphics, Rect bounds, double scale)
    {
        this.graphics = graphics;
        Bounds = bounds;
        Scale = scale;
        using var scope = graphics.AcquireBackgroundRenderScope();
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        surface = graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels(pixelWidth, pixelHeight, scale, debugName: "LiquidGlassSnapshot"));
        try
        {
            renderer = graphics.CreateContext(surface);
            try { backdrop = new BackdropBlur(graphics, pixelWidth, pixelHeight, preserveSharp: true); }
            catch { renderer.Dispose(); throw; }
        }
        catch { surface.Dispose(); throw; }
    }

    internal void Capture(Action<IGraphicsContext> draw, Color clear, double blurRadius)
    {
        CapturePixels(draw, clear);
        backdrop.Process(blurRadius * Scale / 3);
        backdrop.Upload();
    }

    private void CapturePixels(Action<IGraphicsContext> draw, Color clear)
    {
        using var scope = graphics.AcquireBackgroundRenderScope();
        renderer.BeginFrame(surface);
        using var viewport = WindowPresentation.CaptureViewport(surface);
        try
        {
            renderer.Clear(clear);
            renderer.Translate(-Bounds.X, -Bounds.Y);
            draw(renderer);
        }
        finally { renderer.EndFrame(); }
        backdrop.Capture((ICpuPixelSurface)surface);
    }

    internal void Draw(LiquidGlassLens lens, IGraphicsContext context, Rect bounds, double radius) =>
        lens.Draw(context, graphics, backdrop, Bounds, bounds, new CornerRadius(radius));

    public void Dispose() { backdrop.Dispose(); renderer.Dispose(); surface.Dispose(); }
}
