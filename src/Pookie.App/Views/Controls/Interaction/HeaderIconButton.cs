using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Compact connected actions, with the same glass finish as the search field.
internal sealed class HeaderIconButton : Button
{
    private readonly GlassGeometry geometry = new(), focusGeometry = new();
    private readonly bool start;
    private readonly GlassSheenRaster sheen = new();
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(150), Easing.CubicBezier(.2, 0, 0, 1));
    private double layer, from, target;

    internal HeaderIconButton(string icon, bool start)
    {
        this.start = start;
        Width = Height = 36; Padding = new Thickness(0); BorderThickness = 0;
        Background = Color.Transparent; BorderBrush = Color.Transparent; CornerRadius = 18;
        Content = Icons.View(icon, 20, TrackButtons.OnSurface).Center();
        KeyboardFocusFeedback.Attach(this);
        motion.TickCallback = value => { layer = from + (target - from) * value; InvalidateVisual(); };
        RefreshState();
    }

    private void RefreshState()
    {
        Cursor = IsEffectivelyEnabled ? CursorType.Hand : CursorType.Arrow;
        var next = !IsEffectivelyEnabled ? 0 : IsPressed || KeyboardFocusFeedback.IsVisible(this) ? .12 : IsMouseOver ? .08 : 0;
        if (target == next) return;
        motion.Stop(); from = layer; target = next;
        if (FindVisualRoot() is Window) motion.Start(); else layer = next;
        InvalidateVisual();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == IsMouseOverProperty || property == IsPressedProperty || property == IsEnabledProperty ||
            property == KeyboardFocusFeedback.VisibleProperty) RefreshState();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var corners = start ? new CornerRadius(18, 6, 6, 18) : new CornerRadius(6, 18, 18, 6);
        var shape = geometry.Get(Bounds, corners);
        context.FillPath(shape, GlassSheen.Surface);
        if (layer > 0) context.FillPath(shape, Color.FromArgb((byte)Math.Round(layer * 255), 232, 232, 235));
        sheen.Draw(context, Bounds, corners, layer / .12, GetDpi() / 96d);
        if (IsEffectivelyEnabled && KeyboardFocusFeedback.IsVisible(this))
            context.DrawPath(focusGeometry.Get(new Rect(Bounds.X + 2, Bounds.Y + 2, Bounds.Width - 4, Bounds.Height - 4),
                start ? new CornerRadius(16, 4, 4, 16) : new CornerRadius(4, 16, 16, 4)), TrackButtons.OnSurface, 2);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        // A single icon needs alpha on its draw, not another opacity surface.
        context.Save();
        try
        {
            if (!IsEffectivelyEnabled) context.GlobalAlpha *= .38f;
            base.RenderSubtree(context);
        }
        finally { context.Restore(); }
    }

    protected override void OnDispose() { sheen.Dispose(); motion.Stop(); base.OnDispose(); }
}
