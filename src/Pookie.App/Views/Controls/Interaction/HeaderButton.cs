using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

// Shared interaction layer for buttons on the header's glass cards.
internal sealed class HeaderButton : Button
{
    internal const double HoverOpacity = .08;
    private const double SelectedOpacity = .12;
    private readonly GlassGeometry geometry = new(), focusGeometry = new();
    private readonly CornerRadius corners, focusCorners;
    private readonly bool dimDisabledContent;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(150), Easing.CubicBezier(.2, 0, 0, 1));
    private (double Layer, Color Ink, Color Text) current, from, target;
    private SimpleSvgSource? glyph;
    private (TextBlock Label, Color Normal, Color Hover)? labelInk;
    private TransformDecorator? activationGlyph;
    private AnimationClock? activationMotion;
    private Color normalInk, hoverInk, selectedInk;
    private bool selected;

    internal HeaderButton(CornerRadius corners, bool dimDisabledContent = false)
    {
        this.corners = corners;
        this.dimDisabledContent = dimDisabledContent;
        focusCorners = new CornerRadius(corners.TopLeft - 2, corners.TopRight - 2, corners.BottomRight - 2, corners.BottomLeft - 2);
        Padding = new Thickness(0); BorderThickness = 0;
        Background = Color.Transparent; BorderBrush = Color.Transparent;
        CornerRadius = Math.Max(corners.TopLeft, corners.TopRight);
        KeyboardFocusFeedback.Attach(this);
        motion.TickCallback = value => Paint((from.Layer + (target.Layer - from.Layer) * value,
            from.Ink.Lerp(target.Ink, value), from.Text.Lerp(target.Text, value)));
        RefreshState();
    }

    internal void UseAnimatedInk(SimpleSvgSource icon, Color normal, Color hover, Color active,
        (TextBlock Label, Color Normal, Color Hover)? text = null)
    {
        glyph = icon; labelInk = text;
        normalInk = normal; hoverInk = hover; selectedInk = active;
        motion.Duration = TimeSpan.FromMilliseconds(200);
        RefreshState();
    }

    internal TransformDecorator WithActivationMotion(Image icon, Func<float, Matrix3x2> pose,
        int durationMs = 300, Func<double, double>? easing = null)
    {
        var visual = new TransformDecorator { Child = icon, IsHitTestVisible = false };
        activationGlyph = visual;
        activationMotion = new(TimeSpan.FromMilliseconds(durationMs), easing ?? Easing.CubicBezier(.3, 0, .2, 1))
        {
            TickCallback = progress => visual.Transform = progress is > 0 and < 1 ? pose((float)progress) : Matrix3x2.Identity
        };
        return visual;
    }

    internal void SetSelected(bool value)
    {
        if (selected == value) return;
        selected = value;
        if (!value) StopActivationMotion();
        RefreshState();
    }

    protected override void OnClick()
    {
        var wasSelected = selected;
        base.OnClick();
        if (!wasSelected && selected) activationMotion?.Start();
    }

    private void StopActivationMotion()
    {
        activationMotion?.Stop();
        if (activationGlyph != null) activationGlyph.Transform = Matrix3x2.Identity;
    }

    private void RefreshState()
    {
        var enabled = IsEffectivelyEnabled;
        if (dimDisabledContent && Content is UIElement ink) ink.Opacity = enabled ? 1 : .38;
        Cursor = enabled ? CursorType.Hand : CursorType.Arrow;
        var hovered = enabled && IsMouseOver;
        var layer = selected ? SelectedOpacity : hovered ? HoverOpacity : 0;
        if (enabled && (IsPressed || KeyboardFocusFeedback.IsVisible(this))) layer = selected ? .16 : SelectedOpacity;
        var text = labelInk is { } label ? selected ? selectedInk : hovered ? label.Hover : label.Normal : default;
        var next = (layer, selected ? selectedInk : hovered ? hoverInk : normalInk, text);
        if (target == next) return;
        motion.Stop(); from = current; target = next;
        if (FindVisualRoot() is Window) motion.Start(); else Paint(next);
    }

    private void Paint((double Layer, Color Ink, Color Text) state)
    {
        if (glyph != null && glyph.Tint != state.Ink) glyph.Tint = state.Ink;
        if (labelInk is { } label && label.Label.Foreground != state.Text) label.Label.Foreground = state.Text;
        var layerChanged = state.Layer != current.Layer;
        current = state;
        if (layerChanged) InvalidateVisual();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == IsMouseOverProperty || property == IsPressedProperty || property == IsEnabledProperty ||
            property == KeyboardFocusFeedback.VisibleProperty) RefreshState();
        if (property == KeyboardFocusFeedback.VisibleProperty) InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var focused = IsEffectivelyEnabled && KeyboardFocusFeedback.IsVisible(this);
        if (current.Layer <= 0 && !focused) return;
        var bounds = ShellHeader.StateLayerBounds(this, context.DpiScale);
        var cornerDelta = (bounds.Height - Height) / 2;
        if (current.Layer > 0)
            context.FillPath(geometry.Get(bounds, AdjustCorners(corners, cornerDelta)), TrackButtons.StateColor(Color.Transparent, TrackButtons.OnSurface, current.Layer));
        if (focused)
            context.DrawPath(focusGeometry.Get(new Rect(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4),
                AdjustCorners(focusCorners, cornerDelta)), TrackButtons.OnSurface, 2);
    }

    private static CornerRadius AdjustCorners(CornerRadius radius, double delta) =>
        new(radius.TopLeft + delta, radius.TopRight + delta, radius.BottomRight + delta, radius.BottomLeft + delta);

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) StopActivationMotion();
    }

    protected override void OnDispose() { motion.Stop(); StopActivationMotion(); base.OnDispose(); }
}
