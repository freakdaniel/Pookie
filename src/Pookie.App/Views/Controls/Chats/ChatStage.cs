using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep the reading column centered without adding a conversation wallpaper.
internal sealed class ChatStage(FrameworkElement content, bool centerReadingColumn = true, Func<double>? rightInset = null) : Panel
{
    internal ChatStage Initialize() { Add(content); return this; }
    protected override Size MeasureContent(Size availableSize)
    {
        var room = Math.Max(0,availableSize.Width-(rightInset?.Invoke() ?? 0));
        var width = ChatColumns.SnapPixel(centerReadingColumn ? Math.Min(ChatColumns.ReadingWidth, room) : room, GetDpi()/96d);
        content.Measure(new Size(width, availableSize.Height));
        return availableSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var room = Math.Max(0,finalSize.Width-(rightInset?.Invoke() ?? 0));
        // Keep the reading width and its pixel phase stable as docked details
        // move its centre; text and glass should move without being resized.
        var scale = GetDpi()/96d;
        var width = ChatColumns.SnapPixel(centerReadingColumn ? Math.Min(ChatColumns.ReadingWidth, room) : room, scale);
        var x = ChatColumns.SnapPixel(Bounds.X + (room - width) / 2, scale);
        content.Arrange(new Rect(x, Bounds.Y, width, finalSize.Height));
        return finalSize;
    }
    protected override UIElement? OnHitTest(Point point) => content.HitTest(point);
}
