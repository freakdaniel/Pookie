using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A reply belongs to its message and follows the bubble's measured width.
internal sealed class ChatMessageFrame(FrameworkElement quote, Border bubble) : Panel
{
    private readonly GlassGeometry geometry = new();
    private Color surface;
    private CornerRadius corners = new(18);
    internal void SetAppearance(Color color,CornerRadius radius) { surface = color; corners = radius; InvalidateVisual(); }
    internal ChatMessageFrame Initialize() { Add(quote); Add(bubble); return this; }
    protected override Size MeasureContent(Size availableSize)
    {
        bubble.Measure(availableSize);
        var width = bubble.DesiredSize.Width;
        if (quote.IsVisible)
        {
            var available = Math.Min(availableSize.Width,bubble.MaxWidth);
            quote.Measure(new Size(Math.Max(0,Math.Min(360,available)-16),double.PositiveInfinity));
            width = Math.Min(available,Math.Max(width,quote.DesiredSize.Width+16));
            bubble.Measure(new Size(width,availableSize.Height));
            quote.Measure(new Size(Math.Max(0,width-16),double.PositiveInfinity));
        }
        return new Size(width, bubble.DesiredSize.Height + (quote.IsVisible ? 58 : 0));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var top = Bounds.Y;
        if (quote.IsVisible)
        {
            quote.Arrange(new Rect(Bounds.X+8, top+8, Math.Max(0,finalSize.Width-16),42));
            top += 58;
        }
        bubble.Arrange(new Rect(Bounds.X, top, finalSize.Width, Math.Max(0, Bounds.Bottom - top)));
        return finalSize;
    }
    protected override void OnRender(IGraphicsContext context)
    { if (quote.IsVisible) context.FillPath(geometry.Get(Bounds,corners),surface); }
    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (!quote.IsVisible) { base.RenderSubtree(context); return; }
        context.Save();
        try { context.SetClipPath(geometry.Get(Bounds,corners)); base.RenderSubtree(context); }
        finally { context.Restore(); }
    }
}
