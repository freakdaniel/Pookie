using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Rows share the reading column; their scroll viewport spans the whole chat.
internal sealed class ChatMessageColumn(FrameworkElement content, Func<double>? rightInset = null) : Panel
{
    internal ChatMessageColumn Initialize() { Add(content); return this; }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : ChatColumns.ReadingWidth;
        content.Measure(new Size(Math.Min(ChatColumns.ReadingWidth,Math.Max(0,width-(rightInset?.Invoke() ?? 0))),availableSize.Height));
        return new Size(width,content.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var room = Math.Max(0,finalSize.Width-(rightInset?.Invoke() ?? 0));
        var width = Math.Min(ChatColumns.ReadingWidth,room);
        content.Arrange(new Rect(Bounds.X+(room-width)/2,Bounds.Y,width,finalSize.Height));
        return finalSize;
    }
}
