using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class ChatVideoLayout(FrameworkElement image, FrameworkElement status, FrameworkElement header,
    FrameworkElement controls, FrameworkElement previous, FrameworkElement next, FrameworkElement speed, FrameworkElement gear) : Panel
{
    private double expansion;
    internal double Expansion { get => expansion; set { expansion = value; InvalidateArrange(); } }
    internal ChatVideoLayout Initialize() { Add(image); Add(status); Add(header); Add(previous); Add(next); Add(controls); Add(speed); return this; }
    protected override Size MeasureContent(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        controls.Measure(new Size(Math.Min(440, availableSize.Width - 48), 84));
        header.Measure(new Size(Math.Max(1, availableSize.Width - 48), 40));
        speed.Measure(new Size(Math.Min(264, availableSize.Width - 24), Math.Max(100, availableSize.Height - 120)));
        return default;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var b = Bounds; var t = expansion;
        var normal = new Rect(b.X + 56, b.Y + 80, Math.Max(1, b.Width - 112), Math.Max(1, b.Height - 192));
        var media = new Rect(normal.X + (b.X - normal.X) * t, normal.Y + (b.Y - normal.Y) * t,
            normal.Width + (b.Width - normal.Width) * t, normal.Height + (b.Height - normal.Height) * t);
        image.Arrange(media); status.Arrange(media);
        header.Arrange(new Rect(b.X + 24, b.Y + 12, Math.Max(1, b.Width - 48), 40));
        var cw = Math.Min(440, b.Width - 48); var ch = controls.DesiredSize.Height;
        controls.Arrange(new Rect(b.X + (b.Width - cw) / 2, b.Bottom - ch - 16, cw, ch));
        previous.Arrange(new Rect(b.X + 24, b.Y + (b.Height - 40) / 2, 40, 40));
        next.Arrange(new Rect(b.Right - 64, b.Y + (b.Height - 40) / 2, 40, 40));
        speed.Arrange(new Rect(Math.Clamp(gear.Bounds.Right - speed.DesiredSize.Width + 12, b.X + 8, b.Right - speed.DesiredSize.Width - 8),
            controls.Bounds.Y - speed.DesiredSize.Height + 4, speed.DesiredSize.Width, speed.DesiredSize.Height));
        return finalSize;
    }
}
