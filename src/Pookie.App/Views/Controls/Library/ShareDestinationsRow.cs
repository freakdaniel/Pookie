using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Align button edges with the URL row; captions keep their natural width.
internal sealed class ShareDestinationsRow : Panel
{
    private const double ButtonSize = 56;
    internal ShareDestinationsRow() { Height = 80; CacheMode = new BitmapCache(); }

    protected override Size MeasureContent(Size availableSize)
    {
        var width = 0d;
        foreach (var child in Children)
        {
            child.Measure(new(double.PositiveInfinity, availableSize.Height));
            width += Math.Max(ButtonSize, child.DesiredSize.Width) + 8;
        }
        return new(double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(0, width - 8), Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = Children.Count;
        var step = count > 1 ? Math.Max(0, finalSize.Width - ButtonSize) / (count - 1) : 0;
        for (var index = 0; index < count; index++)
        {
            var child = Children[index];
            child.Arrange(new(Bounds.X + ButtonSize / 2 + step * index - child.DesiredSize.Width / 2,
                Bounds.Y, child.DesiredSize.Width, child.DesiredSize.Height));
        }
        return finalSize;
    }
}
