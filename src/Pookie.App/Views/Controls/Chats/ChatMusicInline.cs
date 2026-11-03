using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Pookie.App;

internal sealed class ChatMusicInline : IInlineTextObject, IDisposable
{
    private readonly TextElement owner;
    private readonly LinkPreviewVisual visual;
    private InlineMetrics metrics;
    internal LinkPreviewData Link => visual.Data;
    internal bool IsInline => !visual.IsFallback;
    internal Rect Bounds { get; private set; }
    internal double MaximumWidth = 460;
    internal ChatMusicInline(TextElement owner, LinkPreviewData link)
    { this.owner = owner; visual = new(owner, link, Invalidate); }
    private void Invalidate()
    {
        if (owner is MultiLineTextBox editor) editor.InvalidateTextView();
        owner.InvalidateMeasure(); owner.InvalidateVisual();
    }
    internal void Update(LinkPreviewData link) => visual.Update(link);
    public InlineMetrics Measure()
    {
        var maximum = owner is MultiLineTextBox editor ? Math.Max(32, editor.ActualWidth - editor.Padding.HorizontalThickness - 8) : MaximumWidth;
        const double height = 18, baseline = 14;
        return metrics = new(visual.Measure(maximum), height, baseline);
    }
    public void Draw(ITextRenderContext context, Point origin)
    {
        if (metrics.Width <= 0) Measure();
        Bounds = new(origin.X, origin.Y, metrics.Width, metrics.Height);
        visual.Draw(context.Graphics, Bounds);
    }
    internal void ClearBounds() => Bounds = Rect.Empty;
    public void Dispose() { visual.Dispose(); ClearBounds(); }
}
