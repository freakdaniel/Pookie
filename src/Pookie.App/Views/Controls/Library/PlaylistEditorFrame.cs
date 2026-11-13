using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class PlaylistEditorFrame(FrameworkElement surface) : Panel
{
    internal const double PreferredWidth = 540, MaximumHeight = 680;
    internal static readonly Func<double, double> MotionEasing = Easing.CubicBezier(.4, 0, .2, 1);
    private readonly AnimationClock resize = new(TimeSpan.FromMilliseconds(300), MotionEasing);
    private double height = 360;
    internal double TargetHeight { get; private set; } = 360;
    internal bool Resizing => resize.IsRunning;
    internal void Initialize() => Add(surface);
    internal void Fit(double target, bool animate)
    {
        if (animate && TargetHeight == target && (resize.IsRunning || height == target)) return;
        resize.Stop(); TargetHeight = target;
        void Apply(double value) { height = value; InvalidateMeasure(); }
        if (!animate || FindVisualRoot() is not Window) { Apply(target); return; }
        var from = height;
        resize.TickCallback = t => Apply(from + (target - from) * t);
        resize.CompletedCallback = () => Apply(target);
        resize.Start();
    }
    internal void Freeze() => resize.Stop();
    protected override Size MeasureContent(Size availableSize)
    {
        var size = new Size(Math.Min(PreferredWidth, availableSize.Width), Math.Min(height, availableSize.Height));
        surface.Measure(size);
        return size;
    }
    protected override Size ArrangeOverride(Size finalSize)
    { surface.Arrange(Bounds); return finalSize; }
    protected override void OnDispose() { resize.Stop(); base.OnDispose(); }
}
