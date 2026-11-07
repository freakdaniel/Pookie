using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Capture only the popup footprint and blur halo. Rendering stops at the popup,
// so neither its foreground nor a submenu can feed back into the glass.
internal sealed class PopupGlassBackdrop(ChatPopupLayer popup) : IDisposable
{
    private DialogSnapshot? sample;
    private Window? window;
    private bool preparing, presenting;
    private readonly HashSet<GlassCard> cards = [];
    private const double Halo = 40;
    internal long Version { get; private set; }
    internal bool Ready => sample != null && sample.Bounds == popup.SurfaceBounds.Inflate(Halo, Halo);
    internal Rect CaptureBounds => sample?.Bounds ?? Rect.Empty;
    internal void Register(GlassCard card) => cards.Add(card);
    internal void Unregister(GlassCard card) => cards.Remove(card);
    private void InvalidateCards() { foreach (var card in cards) card.InvalidateVisual(); }
    internal void Reset() { Clear(); InvalidateCards(); }

    internal void Attach(Window? source)
    {
        if (window == source) return;
        Dispose(); window = source;
        if (source != null) source.FrameRendered += Prepare;
    }

    private void Prepare()
    {
        if (preparing) return;
        if (!popup.IsVisible || window?.WindowState == WindowState.Minimized || popup.SurfaceBounds.Width <= 0)
        { Clear(); return; }
        if (presenting && Ready) { presenting = false; return; }
        if (window?.Content == null) return;
        preparing = true;
        try
        {
            var bounds = popup.SurfaceBounds.Inflate(Halo, Halo);
            var scale = Math.Min(1, window.DpiScale);
            if (sample == null || sample.Bounds != bounds || sample.Scale != scale)
            { Clear(); sample = new(window.GraphicsFactory, bounds, scale); }
            sample.Capture(context =>
            {
                context.SetClip(bounds);
                popup.RenderBehind(context, window.Content);
            }, PopupAppearance.AppBackground);
            sample.Blur(20);
            ++Version;
            presenting = true;
            // The retained renderer caches the card's drawing independently
            // from its parent. Refresh that drawing when the image changes.
            InvalidateCards(); popup.InvalidateVisual();
        }
        finally { preparing = false; }
    }

    internal void Draw(IGraphicsContext context, Rect bounds, double radius)
    {
        if (!Ready)
        { context.FillRoundedRectangle(bounds, radius, radius, PopupAppearance.Surface); return; }
        context.Save();
        try { context.SetClipRoundedRect(bounds, radius, radius); context.DrawImage(sample!.Image, sample.ImageBounds); }
        finally { context.Restore(); }
    }

    private void Clear() { sample?.Dispose(); sample = null; presenting = false; }
    public void Dispose()
    {
        if (window != null) window.FrameRendered -= Prepare;
        window = null; Clear();
    }
}
