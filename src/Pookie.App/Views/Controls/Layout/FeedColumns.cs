using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class FeedColumns(FrameworkElement main, FrameworkElement side) : Panel
{
    private double sidebar;
    internal FeedColumns Initialize() { Add(main); Add(side); return this; }
    protected override Size MeasureContent(Size availableSize)
    {
        sidebar = availableSize.Width >= 1080 ? 264 : 0;
        side.IsVisible = sidebar > 0;
        main.Measure(new Size(Math.Max(0, availableSize.Width - (sidebar > 0 ? sidebar + 32 : 0)), availableSize.Height));
        if (sidebar > 0) side.Measure(new Size(sidebar, availableSize.Height));
        return new Size(availableSize.Width, double.IsFinite(availableSize.Height) ? availableSize.Height : main.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        main.Arrange(new Rect(Bounds.X, Bounds.Y, Math.Max(0, finalSize.Width - (sidebar > 0 ? sidebar + 32 : 0)), finalSize.Height));
        if (sidebar > 0) side.Arrange(new Rect(Bounds.Right - sidebar, Bounds.Y, sidebar, finalSize.Height));
        return finalSize;
    }
}
