using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

// Reuse the measured, unbounded-height layout for plain comment text. A
// separate height-constrained render layout reshapes each newly revealed row;
// fractional-DPI scrolling also alternates its rounded height.
internal sealed class CommentTextBlock : TextBlock
{
    private ITextLayout? layout;
    private IGraphicsFactory? factory;
    private readonly record struct ParagraphKey(double Width, uint Dpi, TextRunStyle Style, TextWrapping Wrapping,
        TextTrimming Trimming, TextAlignment Alignment, double Spacing, LineBoxTrim Trim, string Culture, string Text);
    private ParagraphKey key;
    private long revision;

    private TextWrapping Wrapping => TextWrapping == TextWrapping.NoWrap && (Text.Contains('\n') || Text.Contains('\r'))
        ? TextWrapping.Wrap : TextWrapping;

    private ITextLayout Paragraph(double width, bool measuring = false)
    {
        width = (measuring && Wrapping == TextWrapping.NoWrap) ||
            (Wrapping == TextWrapping.NoWrap && TextTrimming == TextTrimming.None && TextAlignment == TextAlignment.Left)
            ? double.PositiveInfinity : double.IsFinite(width) && width > 0 ? width : 1_000_000;
        var next = new ParagraphKey(width, GetDpi(), new TextRunStyle(FontFamily, FontSize, FontWeight, FontStyle == FontStyle.Italic),
            Wrapping, TextTrimming, TextAlignment, LineSpacing, LineBoxTrim, CultureInfo.CurrentUICulture.Name, Text);
        var graphics = GetGraphicsFactory();
        if (layout != null && key == next && factory == graphics) return layout;
        // A short single line measured in a larger slot needs no reshaping
        // when its parent then gives it just its natural width.
        if (layout != null && factory == graphics && key with { Width = width } == next && layout.Lines.Count == 1 &&
            layout.MeasuredSize.Width <= width && layout.MeasuredSize.Width < key.Width - .5) return layout;
        key = next; factory = graphics;
        return layout = graphics.TextEngine.GetOrCreateLayout(new TextLayoutRequest
        {
            Text = Text.AsMemory(), Dpi = next.Dpi, DefaultStyle = next.Style, Revision = ++revision,
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = width, MaxHeight = double.PositiveInfinity, Wrapping = Wrapping,
                Trimming = TextTrimming, Alignment = TextAlignment,
                LineSpacing = LineSpacing, LineBoxTrim = LineBoxTrim, Culture = CultureInfo.CurrentUICulture
            }
        }, TextLayoutCachePolicy.Owner, this);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (TextTrimming != TextTrimming.None) return base.MeasureContent(availableSize);
        if (Text.Length == 0) return Size.Empty;
        var size = Paragraph(availableSize.Width, measuring: true).MeasuredSize;
        var scale = GetDpi() / 96d;
        return new Size(Math.Ceiling(size.Width * scale) / scale + (Wrapping == TextWrapping.NoWrap ? 0 : 1 / scale), size.Height);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Ellipsis is width-dependent; retain the framework's separate render
        // layout for names that can be clipped instead of reusing measurement.
        if (TextTrimming != TextTrimming.None) { base.OnRender(context); return; }
        if (Text.Length == 0) return;
        var paragraph = Paragraph(Bounds.Width);
        var options = new TextDrawOptions(Foreground, default, default, this);
        var y = Bounds.Y + (VerticalTextAlignment switch
        {
            TextAlignment.Center => Math.Max(0, (Bounds.Height - paragraph.ContentHeight) / 2),
            TextAlignment.Right => Math.Max(0, Bounds.Height - paragraph.ContentHeight),
            _ => 0
        });
        var origin = new Point(Bounds.X, y);
        if (paragraph.MeasuredSize.Width <= Bounds.Width + .5 && paragraph.ContentHeight <= Bounds.Height + .5)
        { context.Text.Draw(paragraph, origin, in options); return; }
        context.Save();
        try { context.SetClip(Bounds); context.Text.Draw(paragraph, origin, in options); }
        finally { context.Restore(); }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { layout = null; factory = null; }
    }
}
