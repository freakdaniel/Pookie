using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// A reply belongs to its message and follows the bubble's measured width.
internal sealed class ChatMessageFrame(FrameworkElement quote, FrameworkElement bubble) : Panel
{
    internal ChatMessageFrame Initialize() { Add(quote); Add(bubble); return this; }
    protected override Size MeasureContent(Size availableSize)
    {
        bubble.Measure(availableSize);
        if (quote.IsVisible) quote.Measure(new Size(bubble.DesiredSize.Width, double.PositiveInfinity));
        return new Size(bubble.DesiredSize.Width, bubble.DesiredSize.Height + (quote.IsVisible ? quote.DesiredSize.Height : 0));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var top = Bounds.Y;
        if (quote.IsVisible)
        {
            quote.Arrange(new Rect(Bounds.X, top, finalSize.Width, quote.DesiredSize.Height));
            top += quote.DesiredSize.Height;
        }
        bubble.Arrange(new Rect(Bounds.X, top, finalSize.Width, Math.Max(0, Bounds.Bottom - top)));
        return finalSize;
    }
}
