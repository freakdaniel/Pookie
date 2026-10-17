using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class ChatColumns(FrameworkElement inbox, FrameworkElement thread, Action<bool> compactChanged) : Panel
{
    private bool selected, compact;
    private double sidebar;
    internal bool Selected { get => selected; set { if (selected == value) return; selected = value; InvalidateMeasure(); } }
    internal ChatColumns Initialize() { Add(inbox); Add(thread); return this; }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1000;
        var narrow = width < 760;
        if (compact != narrow) { compact = narrow; compactChanged(compact); }
        sidebar = Math.Clamp(width * .29, 260, 320);
        inbox.IsVisible = !compact || !selected;
        thread.IsVisible = !compact || selected;
        if (inbox.IsVisible) inbox.Measure(new Size(compact ? width : sidebar, availableSize.Height));
        if (thread.IsVisible) thread.Measure(new Size(compact ? width : Math.Max(0, width - sidebar - 24), availableSize.Height));
        return new Size(width, double.IsFinite(availableSize.Height) ? availableSize.Height : 600);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (inbox.IsVisible) inbox.Arrange(new Rect(Bounds.X, Bounds.Y, compact ? finalSize.Width : sidebar, finalSize.Height));
        if (thread.IsVisible) thread.Arrange(new Rect(compact ? Bounds.X : Bounds.X + sidebar + 24, Bounds.Y,
            compact ? finalSize.Width : Math.Max(0, finalSize.Width - sidebar - 24), finalSize.Height));
        return finalSize;
    }
}
