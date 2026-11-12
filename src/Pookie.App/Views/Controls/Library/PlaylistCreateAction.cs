using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class PlaylistCreateAction : Panel
{
    private const double FullHeight = 62;
    private readonly Button button;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(280), Easing.CubicBezier(.2, 0, 0, 1));
    internal double Progress { get; private set; }
    internal bool Expanded { get; private set; }
    internal bool Animating => motion.IsRunning;
    internal double TargetHeight => Expanded ? FullHeight : 0;
    internal double CurrentHeight => FullHeight * Progress;
    internal PlaylistCreateAction(Button button)
    { this.button = button; Add(button); ClipToBounds = true; IsVisible = false; button.Opacity = 0; }
    internal void Reveal(bool visible, bool animate = true)
    {
        if (visible == Expanded && animate) return;
        motion.Stop(); Expanded = visible; var from = Progress; var target = visible ? 1d : 0d;
        IsHitTestVisible = visible; IsVisible = visible || Progress > 0;
        void Present(double amount) { Progress = from + (target - from) * amount; button.Opacity = Progress; InvalidateMeasure(); }
        if (!animate || FindVisualRoot() is not Window) { Present(1); IsVisible = visible; return; }
        motion.TickCallback = Present;
        motion.CompletedCallback = () => { Present(1); IsVisible = visible; };
        motion.Start(); InvalidateMeasure();
    }
    protected override Size MeasureContent(Size availableSize)
    { button.Measure(new(availableSize.Width, 44)); return new(availableSize.Width, CurrentHeight); }
    protected override Size ArrangeOverride(Size finalSize)
    { button.Arrange(new(Bounds.X, Bounds.Y + 18 + 8 * (1 - Progress), finalSize.Width, 44)); return finalSize; }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
