using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

internal sealed class DescriptionTextBlock : SelectableTextBlock
{
    private ITextLayout? layout;
    private double layoutWidth = -1, layoutSpacing;
    private uint layoutDpi;
    private TextRunStyle layoutStyle;
    internal DescriptionLinks? Links { get; set; }
    internal event Action? TextChanged;
    internal double ExtentHeight => GetTextLayout(ActualWidth).MeasuredSize.Height;

    internal DescriptionTextBlock() { TextWrapping = TextWrapping.Wrap; Cursor = CursorType.IBeam; }
    internal void RefreshLayout() { layout = null; InvalidateMeasure(); InvalidateVisual(); }
    protected override void OnTextChanged()
    { layout = null; base.OnTextChanged(); TextChanged?.Invoke(); }
    protected override ITextLayout SelectionLayout(double width) => GetTextLayout(width);
    internal ITextLayout GetTextLayout(double width)
    {
        width = Math.Max(1, width);
        var dpi = (uint)Math.Round(96 * (FindVisualRoot() is Window window ? window.DpiScale : 1));
        var style = new TextRunStyle(FontFamily, FontSize, FontWeight, FontStyle == FontStyle.Italic);
        if (layout != null && layoutWidth.Equals(width) && layoutDpi == dpi && layoutStyle == style && layoutSpacing.Equals(LineSpacing)) return layout;
        layoutWidth = width; layoutDpi = dpi; layoutStyle = style; layoutSpacing = LineSpacing;
        return layout = Application.Current.GraphicsFactory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = Text.AsMemory(), Dpi = dpi, DefaultStyle = style, Inlines = Links?.Inlines ?? [],
            Paragraph = new() { MaxWidth = width, Wrapping = TextWrapping.Wrap, LineSpacing = LineSpacing,
                Alignment = TextAlignment, LineBoxTrim = LineBoxTrim, Culture = System.Globalization.CultureInfo.CurrentUICulture }
        });
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var measured = GetTextLayout(availableSize.Width).MeasuredSize;
        return new(Math.Min(availableSize.Width, measured.Width + 1), measured.Height);
    }
    protected override void OnRender(IGraphicsContext context)
    {
        DrawSelection(context);
        context.Save();
        try
        {
            context.SetClip(Bounds);
            context.Text.Draw(GetTextLayout(Bounds.Width), new(Bounds.X, Bounds.Y),
                new TextDrawOptions(Foreground, PaintSpans: Links?.PaintSpans ?? [], Owner: this));
        }
        finally { context.Restore(); }
    }
}
