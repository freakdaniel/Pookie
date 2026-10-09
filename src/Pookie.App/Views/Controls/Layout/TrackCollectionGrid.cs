using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class TrackCollectionGrid(int maximumColumns = 6, double minimumWidth = 170, double gap = 24) : Panel
{
    private int columns;
    private double cellWidth, cellHeight;

    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1200;
        columns = Math.Clamp((int)((width + gap) / (minimumWidth + gap)), 1, maximumColumns);
        cellWidth = Math.Max(0, (width - gap * (columns - 1)) / columns);
        cellHeight = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            cellHeight = Math.Max(cellHeight, child.DesiredSize.Height);
        }
        var rows = (Children.Count + columns - 1) / columns;
        return new Size(width, rows == 0 ? 0 : rows * cellHeight + (rows - 1) * gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < Children.Count; i++)
            Children[i].Arrange(new Rect(Bounds.X + i % columns * (cellWidth + gap),
                Bounds.Y + i / columns * (cellHeight + gap), cellWidth, cellHeight));
        return finalSize;
    }
}

internal sealed class TrackCollectionTile(Border cover, StackPanel text, double captionGap = 1, Action<double>? resize = null) : Panel
{
    private double size;
    internal TrackCollectionTile Initialize() { Add(cover); Add(text); return this; }

    protected override Size MeasureContent(Size availableSize)
    {
        size = double.IsFinite(availableSize.Width) ? availableSize.Width : 170;
        resize?.Invoke(size);
        cover.Width = cover.Height = size;
        cover.Measure(new Size(size, size));
        text.Measure(new Size(size, double.PositiveInfinity));
        return new Size(size, size + captionGap + text.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        cover.Arrange(new Rect(Bounds.X, Bounds.Y, size, size));
        text.Arrange(new Rect(Bounds.X, Bounds.Y + size + captionGap, size, text.DesiredSize.Height));
        return finalSize;
    }
}
