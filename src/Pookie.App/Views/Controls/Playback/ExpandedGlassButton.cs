using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class ExpandedGlassButton(CoverGlassBackdrop? backdrop, bool paintSurface = true) : Button
{
    private readonly GlassSheenRaster sheen = new();
    private readonly GlassGeometry geometry = new(), focusGeometry = new();
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private bool active, feedback;
    private bool? connectedStart;
    private double layer, selection, fromLayer, fromSelection, targetLayer, targetSelection;
    internal bool IsConnected => connectedStart.HasValue;

    internal CornerRadius GlassCorners
    {
        get
        {
            var outer = Height / 2;
            var inner = 7 + (outer - 7) * selection;
            return connectedStart is { } start ? start ? new(outer, inner, inner, outer) : new(inner, outer, outer, inner) : new(outer);
        }
    }

    internal ExpandedGlassButton Initialize(FrameworkElement content)
    {
        Background = BorderBrush = Color.Transparent; BorderThickness = 0; Padding = new Thickness(0);
        Content = content.Center(); KeyboardFocusFeedback.Attach(this);
        if (backdrop != null) backdrop.Changed += InvalidateVisual;
        motion.TickCallback = value =>
        {
            layer = fromLayer + (targetLayer - fromLayer) * value;
            selection = fromSelection + (targetSelection - fromSelection) * value; InvalidateVisual();
        };
        Refresh(); return this;
    }

    internal void SetActive(bool value) { active = value; Refresh(); }
    internal void Connect(bool start) { connectedStart = start; feedback = true; InvalidateVisual(); }

    private void Refresh()
    {
        Cursor = IsEffectivelyEnabled ? CursorType.Hand : CursorType.Arrow;
        if (Content is UIElement glyph) glyph.Opacity = IsEffectivelyEnabled ? 1 : .38;
        var nextLayer = !IsEffectivelyEnabled ? 0 : IsPressed || KeyboardFocusFeedback.IsVisible(this) ? .12 : IsMouseOver ? .08 : 0;
        var nextSelection = active ? 1d : 0;
        if (nextLayer == targetLayer && nextSelection == targetSelection) return;
        motion.Stop(); fromLayer = layer; fromSelection = selection; targetLayer = nextLayer; targetSelection = nextSelection;
        if (FindVisualRoot() is Window) motion.Start();
        else { layer = nextLayer; selection = nextSelection; }
        InvalidateVisual();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == IsMouseOverProperty || property == IsPressedProperty || property == IsEnabledProperty ||
            property == KeyboardFocusFeedback.VisibleProperty) Refresh();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var corners = GlassCorners;
        var shape = paintSurface || layer > 0 ? geometry.Get(Bounds, corners) : null;
        if (paintSurface)
        {
            backdrop?.Draw(context, Bounds, corners, shape);
            var neutral = GlassSheen.ControlTint(backdrop?.Brightness(Bounds) ?? 0);
            var tint = neutral.Lerp(feedback ? Color.FromArgb((byte)Math.Min(230, neutral.A + 28), 75, 28, 44) :
                Color.FromArgb((byte)Math.Min(230, neutral.A + 28), 52, 52, 62), selection);
            context.FillPath(shape!, tint);
        }
        if (layer > 0) context.FillPath(shape!, Color.FromArgb((byte)Math.Round(layer * 255), 232, 232, 235));
        if (paintSurface) sheen.Draw(context, Bounds, corners, .35 + layer / .12 * .65, GetDpi() / 96d);
        if (IsEffectivelyEnabled && KeyboardFocusFeedback.IsVisible(this))
            context.DrawPath(focusGeometry.Get(new Rect(Bounds.X + 2, Bounds.Y + 2, Bounds.Width - 4, Bounds.Height - 4), corners), TrackButtons.OnSurface, 2);
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) sheen.Dispose();
    }

    protected override void OnDispose()
    {
        if (backdrop != null) backdrop.Changed -= InvalidateVisual;
        sheen.Dispose(); motion.Stop(); base.OnDispose();
    }
}

internal sealed class ExpandedGlassSurface : ContentControl
{
    private readonly GlassGeometry geometry = new();
    private readonly GlassSheenRaster sheen = new();
    private CoverGlassBackdrop? backdrop;
    internal ExpandedGlassSurface(CoverGlassBackdrop? backdrop = null)
    { if (backdrop != null) SetBackdrop(backdrop); }
    internal void SetBackdrop(CoverGlassBackdrop value)
    {
        if (backdrop != null) backdrop.Changed -= InvalidateVisual;
        backdrop = value; backdrop.Changed += InvalidateVisual; InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var corners = new CornerRadius(CornerRadius);
        backdrop?.Draw(context, Bounds, corners, geometry.Get(Bounds,corners));
        var tint = backdrop?.IsCard == true ? GlassSheen.LightControlTint(Background) :
            Background.A > 0 ? GlassSheen.AdaptTint(Background, backdrop?.Brightness(Bounds) ?? 0) :
            GlassSheen.ControlTint(backdrop?.Brightness(Bounds) ?? 0);
        context.FillRoundedRectangle(Bounds, CornerRadius, CornerRadius, tint);
        sheen.Draw(context, Bounds, corners, .35, GetDpi() / 96d);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        context.Save();
        try { context.SetClipRoundedRect(Bounds, CornerRadius, CornerRadius); base.RenderSubtree(context); }
        finally { context.Restore(); }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) sheen.Dispose();
    }

    protected override void OnDispose() { sheen.Dispose(); if (backdrop != null) backdrop.Changed -= InvalidateVisual; base.OnDispose(); }
}
