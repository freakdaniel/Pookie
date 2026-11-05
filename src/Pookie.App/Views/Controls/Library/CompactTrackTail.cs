using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Reserve the expanded strip once. Only paint transforms change during hover;
// the title, virtualized row height and scrollbar never participate in motion.
internal sealed class CompactTrackTail : Grid
{
    internal const double FullActionsThreshold = 420;
    internal const double StatisticsWidth = 48, CompactWidth = 88, FullWidth = 176;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly Motion likeMotion, summaryMotion, actionsMotion;
    private readonly InkBorder summary, actions;
    private readonly bool miniature;
    private double amount, target;
    internal double Reveal => amount;
    internal bool Animating => motion.IsRunning;

    internal CompactTrackTail(Button like, InkBorder summary, InkBorder actions, bool miniature)
    {
        this.summary = summary; this.actions = actions; this.miniature = miniature;
        this.Columns("*").Rows("*");
        Width = miniature ? 48 : CompactWidth;
        likeMotion = new(like.Left().CenterVertical());
        summaryMotion = new(summary.Right().CenterVertical());
        actionsMotion = new(actions.Right().CenterVertical());
        Add(likeMotion); Add(summaryMotion); Add(actionsMotion);
        SizeChanged += _ => Paint();
        motion.CompletedCallback = () => { amount = target; Paint(); };
        Paint();
    }

    internal void SetExpanded(bool expanded, bool animate)
    {
        var next = expanded && !miniature ? 1d : 0d;
        if (animate && target == next) return;
        motion.Stop(); target = next;
        if (!animate || FindVisualRoot() is not Window) { amount = next; Paint(); return; }
        var from = amount;
        Paint();
        motion.TickCallback = t => { amount = from + (next - from) * t; Paint(); };
        motion.Start();
    }

    private void Paint()
    {
        likeMotion.Move(Math.Max(0, ActualWidth - CompactWidth) * (1 - amount), 0);
        summaryMotion.Move(0, -5 * amount);
        actionsMotion.Move(12 * (1 - amount), 0);
        summary.Strength = 1 - amount; actions.Strength = amount;
        summary.IsHitTestVisible = target < .5;
        // A moving-in action must not steal a click aimed at the resting like.
        actions.IsHitTestVisible = target > .5 && amount >= .98;
    }

    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }

    private sealed class Motion : ContentControl
    {
        private Point offset;
        internal Motion(FrameworkElement child)
        { Content = child; Padding = new Thickness(0); BorderThickness = 0; CornerRadius = 0; Background = Color.Transparent; }
        internal void Move(double x, double y)
        { var next = new Point(x, y); if (offset == next) return; offset = next; InvalidateVisual(); }
        protected override bool TryGetChildTransform(out Matrix3x2 transform)
        { transform = Matrix3x2.CreateTranslation((float)offset.X, (float)offset.Y); return !transform.IsIdentity; }
        protected override UIElement? OnHitTest(Point point) => IsVisible && IsHitTestVisible && IsEffectivelyEnabled && Content is UIElement child
            ? child.HitTest(new Point(point.X - offset.X, point.Y - offset.Y)) : null;
    }
}
