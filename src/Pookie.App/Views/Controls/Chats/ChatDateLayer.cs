using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Virtualized rows cannot leave their scroll viewport. This layer continues the
// separator at the same horizontal position when it reaches the viewport edge.
internal sealed class ChatDateLayer : Panel
{
    private readonly Border badge;
    private readonly DispatcherTimer idle = new(TimeSpan.FromMilliseconds(1100));
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(180), Easing.Linear);
    private bool active, pinned;
    private double from, target, center, top;
    internal ChatDateLayer(Border badge)
    {
        this.badge = badge; Add(badge); IsHitTestVisible = false;
        badge.Opacity = 0; badge.IsVisible = false;
        idle.Tick += () => { idle.Stop(); active = false; Refresh(); };
        fade.TickCallback = value => badge.Opacity = from + (target - from) * value;
        fade.CompletedCallback = () => badge.IsVisible = target > 0;
    }
    internal void Pulse() { active = true; idle.Stop(); idle.Start(); Refresh(); }
    internal void Place(double center, double top, bool pinned)
    {
        if (Math.Abs(this.center - center) > .1 || Math.Abs(this.top - top) > .1)
        { this.center = center; this.top = top; InvalidateMeasure(); }
        if (this.pinned != pinned) { this.pinned = pinned; Refresh(); }
    }
    private void Refresh()
    {
        var next = active && pinned ? 1d : 0;
        if (next == target) return;
        fade.Stop(); from = badge.Opacity; target = next;
        if (target > 0) badge.IsVisible = true;
        fade.Start();
    }
    internal void ResetOverlay()
    { idle.Stop(); fade.Stop(); active = pinned = false; from = target = 0; badge.Opacity = 0; badge.IsVisible = false; }
    protected override Size MeasureContent(Size availableSize)
    {
        badge.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new(double.IsFinite(availableSize.Width) ? availableSize.Width : 0,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        badge.Arrange(new Rect(Bounds.X + center - badge.DesiredSize.Width / 2, Bounds.Y + top,
            badge.DesiredSize.Width, badge.DesiredSize.Height));
        return finalSize;
    }
    protected override void OnDispose() { ResetOverlay(); idle.Dispose(); base.OnDispose(); }
}
