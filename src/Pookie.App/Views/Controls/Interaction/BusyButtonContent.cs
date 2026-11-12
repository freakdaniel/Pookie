using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class BusyButtonContent(TextBlock label) : Panel
{
    private readonly LoadingSpinner spinner = new LoadingSpinner().Foreground(Color.White);
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private double amount, from, target;
    internal bool Busy { get; private set; }
    internal double Progress => amount;
    internal bool Animating => motion.IsRunning;
    internal bool SpinnerRunning => spinner.Running;
    internal BusyButtonContent Initialize()
    {
        IsHitTestVisible = false; spinner.Opacity = 0;
        Add(label); Add(spinner);
        motion.TickCallback = value => { amount = from + (target - from) * value; InvalidateMeasure(); };
        motion.CompletedCallback = () => { if (!Busy) spinner.IsActive = false; };
        return this;
    }
    internal void SetBusy(bool value, bool animate = true)
    {
        if (value == Busy && animate) return;
        Busy = value; motion.Stop(); from = amount; target = value ? 1 : 0;
        if (value) spinner.IsActive = true;
        if (animate && from != target && FindVisualRoot() is Window) motion.Start();
        else { amount = target; spinner.IsActive = value; InvalidateMeasure(); }
    }
    protected override Size MeasureContent(Size availableSize)
    {
        label.Measure(new(double.PositiveInfinity, 24)); spinner.Measure(new(18, 18));
        return new(label.DesiredSize.Width + 26 * amount, Math.Max(20, label.DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        label.Opacity = 1 - .18 * amount;
        label.Arrange(new(Bounds.X, Bounds.Y, label.DesiredSize.Width, finalSize.Height));
        var size = 18 * (.7 + .3 * amount);
        spinner.Opacity = amount;
        spinner.Arrange(new(Bounds.X + label.DesiredSize.Width + 8 + (18 - size) / 2,
            Bounds.Y + (finalSize.Height - size) / 2, size, size));
        return finalSize;
    }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
