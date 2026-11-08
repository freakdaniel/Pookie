using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Capture only the button and its blur halo, never the full window. Exclude the
// overlay's controls so the close icon cannot feed back into its own glass.
internal sealed class OverlayButtonBackdrop(FrameworkElement button, FrameworkElement overlay, Action<IGraphicsContext> drawOverlay) : IDisposable
{
    private readonly LiquidGlassLens lens = new();
    private LiquidGlassSnapshot? sample;
    private Window? window;
    private bool preparing, presenting;
    internal bool Ready => sample is { Ready: true };
    internal Rect CaptureBounds => sample?.Bounds ?? default;
    internal void Attach(Window? source)
    {
        if (window == source) return;
        Dispose(); window = source;
        if (window != null) window.FrameRendered += Prepare;
    }
    private void Prepare()
    {
        if (preparing) return;
        if (!overlay.IsVisible || button.Bounds.Width <= 0 || window?.WindowState == WindowState.Minimized)
        { Clear(); return; }
        // The one repaint that presents a prepared sample must not recursively
        // request another repaint while the underlying window is idle.
        if (presenting) { presenting = false; return; }
        if (window == null || overlay.Parent is not Panel parent) return;
        preparing = true;
        try
        {
            var bounds = button.Bounds.Inflate(24, 24);
            var scale = Math.Min(2, window.DpiScale);
            if (sample == null || sample.Bounds != bounds || sample.Scale != scale)
            { Clear(); sample = new(window.GraphicsFactory, bounds, scale); }
            sample.Capture(context =>
            {
                context.SetClip(bounds);
                foreach (var sibling in parent.Children)
                {
                    if (ReferenceEquals(sibling, overlay)) break;
                    sibling.Render(context);
                }
                drawOverlay(context);
            }, Color.FromRgb(24, 24, 24), blurRadius: 12);
            presenting = true; button.InvalidateVisual();
        }
        finally { preparing = false; }
    }
    internal void Draw(IGraphicsContext context, Rect bounds, double radius)
    {
        if (sample is { Ready: true }) sample.Draw(lens, context, bounds, radius);
    }
    private void Clear() { lens.Dispose(); sample?.Dispose(); sample = null; presenting = false; }
    public void Dispose()
    {
        if (window != null) window.FrameRendered -= Prepare;
        window = null; Clear();
    }
}
