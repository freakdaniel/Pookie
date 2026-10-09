using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Both columns are measured against this pass's viewport. Updating grid definitions
// from SizeChanged uses yesterday's desired width and causes a resize feedback loop.
internal sealed class TrackPageColumns(FrameworkElement main, FrameworkElement side, bool hero = false, params FrameworkElement[] background) : Panel
{
    private double sideWidth, height;

    internal TrackPageColumns Initialize()
    {
        foreach (var layer in background) Add(layer);
        Add(main); Add(side);
        return this;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1200;
        sideWidth = Math.Clamp(Math.Round(width * .27), 280, 400);
        var innerWidth = Math.Max(0, width - 48);
        var mainWidth = Math.Max(0, innerWidth - sideWidth - 32);
        if (hero) side.Width = side.Height = sideWidth;
        side.Measure(new Size(sideWidth, hero ? sideWidth : availableSize.Height));
        main.Measure(new Size(mainWidth, hero ? sideWidth : availableSize.Height));
        height = (hero ? sideWidth : Math.Max(main.DesiredSize.Height, side.DesiredSize.Height)) + (hero ? 48 : 0);
        foreach (var layer in background) layer.Measure(new Size(width, height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var layer in background) layer.Arrange(Bounds);
        var y = Bounds.Y + (hero ? 24 : 0);
        var mainWidth = Math.Max(0, finalSize.Width - 48 - sideWidth - 32);
        main.Arrange(new Rect(Bounds.X + 24, y, mainWidth, height - (hero ? 48 : 0)));
        side.Arrange(new Rect(Bounds.X + 24 + mainWidth + 32, y, sideWidth, height - (hero ? 48 : 0)));
        return finalSize;
    }
}
