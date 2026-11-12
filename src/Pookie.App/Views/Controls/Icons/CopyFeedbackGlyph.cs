using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Fixed-size ink: copying and its delayed reversal never move the button.
internal sealed class CopyFeedbackGlyph : Panel
{
    private const double GlyphSize = 20;
    private readonly Image copy = Icons.View("copy", GlyphSize, Color.White), check = Icons.View("check", GlyphSize, Color.White);
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly DispatcherTimer hold = new(TimeSpan.FromSeconds(2));
    private double progress, from, target;
    internal bool Confirmed { get; private set; }
    internal bool Animating => motion.IsRunning;
    internal double Progress => progress;
    internal Action? Reverted { get; init; }

    internal CopyFeedbackGlyph()
    {
        Width = Height = GlyphSize;
        IsHitTestVisible = false;
        copy.Width = copy.Height = check.Width = check.Height = double.NaN;
        Add(copy); Add(check);
        motion.TickCallback = amount => { progress = from + (target - from) * amount; InvalidateArrange(); };
        hold.Tick += () => { Reset(true); Reverted?.Invoke(); };
    }

    internal void ShowCopied()
    {
        hold.Stop(); Set(true, true); hold.Start();
    }

    internal void Reset(bool animate = false) { hold.Stop(); Set(false, animate); }

    private void Set(bool confirmed, bool animate)
    {
        Confirmed = confirmed;
        motion.Stop(); from = progress; target = confirmed ? 1 : 0;
        if (animate && from != target && FindVisualRoot() is Window) motion.Start();
        else { progress = target; InvalidateArrange(); }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        copy.Measure(new(GlyphSize, GlyphSize)); check.Measure(new(GlyphSize, GlyphSize));
        return new(GlyphSize, GlyphSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        void Place(Image image, double opacity)
        {
            var size = GlyphSize * (.75 + .25 * opacity);
            image.Opacity = opacity;
            image.Arrange(new(Bounds.X + (finalSize.Width - size) / 2, Bounds.Y + (finalSize.Height - size) / 2, size, size));
        }
        Place(copy, 1 - progress); Place(check, progress);
        return finalSize;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) Reset();
    }

    protected override void OnDispose() { hold.Dispose(); motion.Stop(); base.OnDispose(); }
}
