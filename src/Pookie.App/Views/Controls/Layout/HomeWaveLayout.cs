using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Anchor the Home drawer to the outer content surface, outside page padding
// and the centered content frame's maximum width.
// Measure from this pass's bounds so resizing cannot feed back through SizeChanged.
internal sealed class HomeWaveLayout : Panel
{
    private readonly FrameworkElement content, drawer;
    private readonly AnimationClock clock = new(TimeSpan.FromMilliseconds(300), Easing.CubicBezier(.2, 0, 0, 1));
    private double amount, from, target;
    internal double Expansion => amount;
    internal bool Open { get; private set; }

    internal HomeWaveLayout(FrameworkElement content, FrameworkElement drawer)
    {
        this.content = content; this.drawer = drawer; ClipToBounds = true;
        Add(content); Add(drawer); ApplyState();
        clock.TickCallback = value => { amount = from + (target - from) * value; ApplyState(); InvalidateMeasure(); };
    }
    internal void SetOpen(bool open)
    {
        if (Open == open) return;
        Open = open; clock.Stop(); from = amount; target = open ? 1 : 0;
        ApplyState(); clock.Start();
    }
    private void ApplyState()
    {
        drawer.Opacity = amount;
        drawer.IsVisible = Open || amount > .000001;
        drawer.IsHitTestVisible = Open && amount > .8;
        drawer.IsEnabled = Open;
    }
    internal const double DrawerInset = 12;
    private static Size DrawerSize(Size viewport) => new(Math.Max(0, Math.Min(360, viewport.Width - DrawerInset * 2)),
        Math.Max(0, viewport.Height - DrawerInset * 2));
    protected override Size MeasureContent(Size availableSize)
    {
        var viewport = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 1200,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 720);
        content.Measure(viewport);
        if (drawer.IsVisible) drawer.Measure(DrawerSize(viewport));
        return viewport;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        content.Arrange(Bounds);
        if (drawer.IsVisible)
        {
            var size = DrawerSize(finalSize);
            drawer.Arrange(new Rect(Bounds.Right - DrawerInset - size.Width + (1 - amount) * 28,
                Bounds.Y + DrawerInset, size.Width, size.Height));
        }
        return finalSize;
    }
    protected override void OnDispose() { clock.Stop(); base.OnDispose(); }
}
