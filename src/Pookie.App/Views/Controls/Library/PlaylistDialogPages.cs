using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Both pages share one card and its animated height. Retarget from the current
// position on reversal; neither page is re-created while entering a title.
internal sealed class PlaylistDialogPages : Panel
{
    private readonly Page list, form;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(260), Easing.CubicBezier(.2, 0, 0, 1));
    private double amount;
    internal bool Creating { get; private set; }
    internal bool Animating => motion.IsRunning;
    internal double ListHeight { get; set; } = PlaylistDialogContent.PreferredBodyHeight;
    internal double FormHeight { get; set; }
    internal Rect ListBounds => list.Bounds;
    internal Rect FormBounds => form.Bounds;
    internal bool ContentOverlaps => list.Opacity > .001 && form.Opacity > .001;
    internal PlaylistDialogPages(FrameworkElement list, FrameworkElement form)
    {
        this.list = new(list); this.form = new(form);
        Add(this.list); Add(this.form); ClipToBounds = true; Apply(0);
    }
    internal void Show(bool create, bool animate = true)
    {
        motion.Stop(); Creating = create;
        list.IsHitTestVisible = !create; form.IsHitTestVisible = create;
        var target = create ? 1d : 0d;
        if (!animate || FindVisualRoot() is not Window) { Apply(target); return; }
        var from = amount;
        list.IsVisible = form.IsVisible = true;
        Apply(from, moving: true);
        motion.Duration = TimeSpan.FromMilliseconds(260 * Math.Max(.25, Math.Abs(target - from)));
        motion.TickCallback = t => Apply(from + (target - from) * t, moving: true);
        motion.CompletedCallback = () => Apply(target);
        motion.Start();
    }
    private void Apply(double value, bool moving = false)
    {
        amount = value;
        // Fade through an empty card: the two pages never paint text/buttons
        // simultaneously, including when a transition is reversed midway.
        list.Opacity = Math.Clamp(1 - value * 2, 0, 1);
        form.Opacity = Math.Clamp(value * 2 - 1, 0, 1);
        list.IsHitTestVisible = !Creating && list.Opacity > .001;
        form.IsHitTestVisible = Creating && form.Opacity > .001;
        list.Move(-24 * value); form.Move(24 * (1 - value));
        list.IsVisible = moving || value < 1; form.IsVisible = moving || value > 0;
    }
    protected override Size MeasureContent(Size availableSize)
    {
        // The card changes height, not the pages' scroll viewports. Measuring
        // the outgoing list against the shrinking card invents an overflow bar.
        list.Measure(new(availableSize.Width, ListHeight));
        form.Measure(new(availableSize.Width, FormHeight));
        return availableSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        list.Arrange(new(Bounds.X, Bounds.Y, finalSize.Width, ListHeight));
        form.Arrange(new(Bounds.X, Bounds.Y, finalSize.Width, FormHeight));
        return finalSize;
    }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
    private sealed class Page : ContentControl
    {
        private double offset;
        internal Page(FrameworkElement child)
        { Content = child; Background = Color.Transparent; BorderThickness = 0; Padding = default; }
        internal void Move(double value) { offset = value; InvalidateVisual(); }
        protected override bool TryGetChildTransform(out Matrix3x2 transform)
        { transform = Matrix3x2.CreateTranslation((float)offset, 0); return !transform.IsIdentity; }
        protected override UIElement? OnHitTest(Point point) => base.OnHitTest(new(point.X - offset, point.Y));
    }
}
