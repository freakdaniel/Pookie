using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class AvatarGroup : Panel, IVisualTreeHost
{
    internal const int AvatarSize = 48, Overlap = 12, TopInset = 18, GroupHeight = 70;

    internal AvatarGroup()
    {
        Height = GroupHeight;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    internal void AddAvatar(FrameworkElement image, string name, Action open)
    {
        var ring = new Border().Width(AvatarSize).Height(AvatarSize).CornerRadius(AvatarSize / 2)
            .Background(PopupAppearance.AppBackground).BorderThickness(0).Padding(3).Child(image);
        var button = TrackButtons.Icon(ring, open, TrackButtons.Text, AvatarSize).Hint(name, group: this);
        if (button is MaterialTrackButton material) material.SuppressStateLayer();
        Add(new Avatar(button));
    }

    protected override Size MeasureContent(Size availableSize)
    {
        foreach (var child in ChildrenList) child.Measure(new(AvatarSize, AvatarSize));
        return new(Count == 0 ? 0 : AvatarSize + (Count - 1) * (AvatarSize - Overlap), GroupHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var stride = Count > 1 ? Math.Min(AvatarSize - Overlap, Math.Max(0, (finalSize.Width - AvatarSize) / (Count - 1))) : 0;
        for (var index = 0; index < Count; index++)
            ChildrenList[index].Arrange(new(Bounds.X + index * stride, Bounds.Y + TopInset, AvatarSize, AvatarSize));
        return finalSize;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        // Keep the same overlap order while an avatar rises.
        for (var index = Count - 1; index >= 0; index--)
            if (!visitor(ChildrenList[index])) return false;
        return true;
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled || !Bounds.Contains(point)) return null;
        for (var index = 0; index < Count; index++)
            if (ChildrenList[index] is UIElement child && child.HitTest(point) is { } hit) return hit;
        return this;
    }

    private sealed class Avatar : ContentControl
    {
        internal readonly Button Button;
        private readonly AnimationClock lift = new(TimeSpan.FromMilliseconds(470), Easing.Spring(300, 17));
        private double offset, target;
        private bool pressed;

        internal Avatar(Button button)
        {
            Button = button;
            Content = button;
            Padding = new Thickness(0); BorderThickness = 0; CornerRadius = 0;
            Background = Color.Transparent;
            button.MouseEnter += Refresh;
            button.MouseLeave += Refresh;
            button.GotFocus += Refresh;
            button.LostFocus += Refresh;
            button.MouseDown += args => { if (args.Button == MouseButton.Left) { pressed = true; Refresh(); } };
            button.MouseUp += args => { if (args.Button == MouseButton.Left) { pressed = false; Refresh(); } };
            lift.CompletedCallback = () =>
            {
                offset = target;
                InvalidateVisual();
            };
        }

        private void Refresh()
        {
            var active = pressed || Button.IsMouseOver || Button.IsFocused && KeyboardFocusFeedback.IsVisible(Button);
            var next = active ? -AvatarSize * .3 : 0;
            if (target == next) return;
            lift.Stop(); target = next;
            var from = offset;
            lift.TickCallback = amount => { offset = from + (next - from) * amount; InvalidateVisual(); };
            lift.Start();
            InvalidateVisual();
        }

        protected override bool TryGetChildTransform(out Matrix3x2 transform)
        {
            transform = Matrix3x2.CreateTranslation(0, (float)offset);
            return !transform.IsIdentity;
        }

        private bool ContainsCircle(Point point)
        {
            var dx = point.X - Bounds.Center.X;
            var dy = point.Y - Bounds.Center.Y;
            return dx * dx + dy * dy <= AvatarSize * AvatarSize / 4d;
        }

        protected override UIElement? OnHitTest(Point point)
        {
            if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled) return null;
            var moved = new Point(point.X, point.Y - offset);
            if (ContainsCircle(moved)) return Button.HitTest(moved);
            // Retain the resting hit area while lifting away from the pointer.
            return Button.IsMouseOver && ContainsCircle(point) ? Button : null;
        }

        protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
        {
            base.OnVisualRootChanged(oldRoot, newRoot);
            if (newRoot != null) return;
            lift.Stop(); pressed = false; offset = target = 0;
        }

        protected override void OnDispose() { lift.Stop(); base.OnDispose(); }
    }
}
