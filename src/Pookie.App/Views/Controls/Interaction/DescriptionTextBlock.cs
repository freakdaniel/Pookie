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
    private (ScrollViewer Viewer, double X, double Y)[] focusViewports = [];
    private bool restoringFocus;
    private long focusRevision;
    internal DescriptionLinks? Links { get; set; }
    internal event Action? TextChanged;
    internal double ExtentHeight => GetTextLayout(ActualWidth).MeasuredSize.Height;

    internal DescriptionTextBlock() { TextWrapping = TextWrapping.Wrap; Cursor = CursorType.IBeam; }
    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        ReleaseFocusViewports();
        var viewports = new List<(ScrollViewer, double, double)>();
        for (Element? parent = Parent; parent != null; parent = parent.Parent)
            if (parent is ScrollViewer viewer && Bounds.IntersectsWith(viewer.Bounds))
                viewports.Add((viewer, viewer.HorizontalOffset, viewer.VerticalOffset));
        if (viewports.Count == 0) return;
        // MewUI brings the entire focused text block into view after GotFocus.
        // For a visible description taller than the viewport this scrolls away
        // from the click. Preserve the visible position for that focus dispatch.
        focusViewports = viewports.ToArray();
        foreach (var item in focusViewports) item.Viewer.ScrollChanged += RestoreFocusViewports;
        var revision = focusRevision;
        Application.Current.Dispatcher!.BeginInvoke(() =>
        { if (revision == focusRevision) ReleaseFocusViewports(); });
    }
    private void RestoreFocusViewports()
    {
        if (restoringFocus) return;
        restoringFocus = true;
        try
        {
            foreach (var item in focusViewports)
                if (item.Viewer.HorizontalOffset != item.X || item.Viewer.VerticalOffset != item.Y)
                    item.Viewer.SetScrollOffsets(item.X, item.Y);
        }
        finally { restoringFocus = false; }
    }
    private void ReleaseFocusViewports()
    {
        ++focusRevision;
        foreach (var item in focusViewports) item.Viewer.ScrollChanged -= RestoreFocusViewports;
        focusViewports = [];
    }
    protected override void OnLostFocus() { ReleaseFocusViewports(); base.OnLostFocus(); }
    protected override void OnDispose() { ReleaseFocusViewports(); base.OnDispose(); }
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
