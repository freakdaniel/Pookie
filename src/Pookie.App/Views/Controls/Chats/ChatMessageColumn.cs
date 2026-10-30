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
        var reading = ChatColumns.SnapPixel(Math.Min(ChatColumns.ReadingWidth,Math.Max(0,width-(rightInset?.Invoke() ?? 0))),GetDpi()/96d);
        content.Measure(new Size(reading,availableSize.Height));
        return new Size(width,content.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var room = Math.Max(0,finalSize.Width-(rightInset?.Invoke() ?? 0));
        var scale = GetDpi()/96d;
        var width = ChatColumns.SnapPixel(Math.Min(ChatColumns.ReadingWidth,room),scale);
        var x = ChatColumns.SnapPixel(Bounds.X+(room-width)/2,scale);
        content.Arrange(new Rect(x,Bounds.Y,width,finalSize.Height));
        return finalSize;
    }
}
