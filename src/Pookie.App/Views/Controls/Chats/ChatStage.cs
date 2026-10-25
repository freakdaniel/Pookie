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
        content.Measure(new Size(centerReadingColumn ? Math.Min(ChatColumns.ReadingWidth, room) : room, availableSize.Height));
        return availableSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var room = Math.Max(0,finalSize.Width-(rightInset?.Invoke() ?? 0));
        var width = centerReadingColumn ? Math.Min(ChatColumns.ReadingWidth, room) : room;
        content.Arrange(new Rect(Bounds.X + (room - width) / 2, Bounds.Y, width, finalSize.Height));
        return finalSize;
    }
    protected override UIElement? OnHitTest(Point point) => content.HitTest(point);
}
