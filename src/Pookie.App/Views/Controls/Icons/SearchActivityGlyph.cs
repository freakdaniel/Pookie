using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep the search slot fixed while its ink folds into a loading ring and back.
internal sealed class SearchActivityGlyph : Panel
{
    private readonly Image icon;
    private readonly LoadingSpinner spinner = new LoadingSpinner().Foreground(Color.White);
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private double progress, from, target;
    internal bool Loading { get; private set; }
    internal bool Animating => motion.IsRunning;
    internal double Progress => progress;
    internal bool SpinnerRunning => spinner.Running;
    internal Color Ink { get; private set; }

    internal SearchActivityGlyph(Color resting)
    {
        Width = Height = 20; IsHitTestVisible = false;
        icon = Icons.View("magnifying-glass", 20, resting);
        icon.Width = icon.Height = double.NaN;
        Ink = resting; TrackButtons.SetGlyph(icon, "magnifying-glass", resting, false);
        spinner.Opacity = 0;
        Add(icon); Add(spinner);
        motion.TickCallback = amount => { progress = from + (target - from) * amount; InvalidateArrange(); };
        motion.CompletedCallback = () => { if (!Loading) spinner.IsActive = false; };
    }

    internal void SetInk(Color color) { Ink = color; TrackButtons.SetGlyph(icon, "magnifying-glass", color); }

    internal void SetLoading(bool value)
    {
        if (Loading == value) return;
        Loading = value; motion.Stop(); from = progress; target = value ? 1 : 0;
        if (value) spinner.IsActive = true;
        if (FindVisualRoot() is Window && from != target) motion.Start();
        else { progress = target; spinner.IsActive = value; InvalidateArrange(); }
    }

    protected override Size MeasureContent(Size availableSize)
    { icon.Measure(new(20, 20)); spinner.Measure(new(20, 20)); return new(20, 20); }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = 20 * (1 - .35 * progress);
        icon.Opacity = 1 - progress;
        icon.Arrange(new(Bounds.X + (20 - size) / 2 + 2 * progress, Bounds.Y + (20 - size) / 2, size, size));
        size = 18 * (.65 + .35 * progress);
        spinner.Opacity = progress;
        spinner.Arrange(new(Bounds.X + (20 - size) / 2, Bounds.Y + (20 - size) / 2, size, size));
        return finalSize;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { motion.Stop(); progress = target = 0; Loading = false; spinner.IsActive = false; }
    }

    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
