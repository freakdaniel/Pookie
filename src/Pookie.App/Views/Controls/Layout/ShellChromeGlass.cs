using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;

namespace Pookie.App;

internal sealed class ShellChromeGlass(FrameworkElement content, Color surface) : Control
{
    internal const double HeaderHeight = 64;
    private const double BlurRadius = 24;
    private IRenderSurface? raster;
    private IGraphicsContext? renderer;
    private IImage? image;
    private IGraphicsFactory? factory;
    private DefaultFilterContext? filterContext;
    private FilterResult? blurred;
    private IImageFilterExecutor? executor;
    private double amount;
    internal double GlassAmount => amount;
    internal bool HasBlurredBackdrop => blurred != null;

    internal void Update(double offset)
    {
        var value = Math.Clamp(offset / 48, 0, 1);
        var next = value * value * (3 - 2 * value);
        if (amount == next && offset == lastOffset) return;
        lastOffset = offset; amount = next; InvalidateVisual();
    }
    private double lastOffset = -1;
    internal void RefreshBackdrop() => InvalidateVisual();

    protected override void OnRender(IGraphicsContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (amount <= .001) { context.FillRectangle(Bounds, surface); return; }
        const double scale = .5;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling((Bounds.Height + BlurRadius * 2) * scale));
        var graphics = Application.Current.GraphicsFactory;
        if (raster == null || raster.PixelWidth != width || raster.PixelHeight != height || factory != graphics)
        {
            ClearRaster(); factory = graphics;
            raster = graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, scale, debugName: "ShellChromeGlass"));
            renderer = graphics.CreateContext(raster); image = graphics.CreateImageView(raster);
            filterContext = new DefaultFilterContext(raster, image, new Rect(0, 0, width / scale, height / scale), graphics, scale, scale);
            executor = graphics.CreateImageFilterExecutor();
        }
        // Refresh the narrow strip only on a requested UI frame, so late artwork and
        // animations remain live without starting a separate repaint loop.
        renderer!.BeginFrame(raster!);
        renderer.Clear(surface);
        renderer.Translate(-Bounds.X, BlurRadius - Bounds.Y);
        content.Render(renderer);
        renderer.EndFrame();
        blurred?.Dispose();
        blurred = executor!.Execute(new BlurFilter(BlurRadius, BlurRadius), filterContext!);
        context.Save();
        try
        {
            context.SetClip(Bounds);
            context.DrawImage(blurred!.AsImage(), new Rect(Bounds.X + blurred.Bounds.X,
                Bounds.Y - BlurRadius + blurred.Bounds.Y, blurred.Bounds.Width, blurred.Bounds.Height));
            // The settled header remains translucent; the bottom edge has no hard rule.
            context.FillRectangle(Bounds, Color.FromArgb((byte)Math.Round(255 - 88 * amount), surface.R, surface.G, surface.B));
        }
        finally { context.Restore(); }
    }

    private void ClearRaster()
    {
        blurred?.Dispose(); blurred = null; filterContext?.Dispose(); filterContext = null;
        image?.Dispose(); image = null; renderer?.Dispose(); renderer = null; raster?.Dispose(); raster = null;
    }
    protected override void OnDispose() { ClearRaster(); base.OnDispose(); }
}

internal sealed class ContentBottomFade(Color surface) : Control
{
    internal const double FadeHeight = 40;
    internal const double PlayerOpacity = .5;
    private readonly GradientStop[] stops = Enumerable.Range(0, 17).Select(index =>
    {
        var position = index / 16d;
        var alpha = (byte)Math.Round(255 * position * position * (3 - 2 * position));
        return new GradientStop(position, Color.FromArgb(alpha, surface.R, surface.G, surface.B));
    }).ToArray();

    protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds,
        new LinearGradientBrush(new Point(Bounds.X, Bounds.Y), new Point(Bounds.X, Bounds.Bottom), stops));
}
