using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A resizable glass surface for content cards and native multiline editors.
internal class GlassCard : ContentControl
{
    private readonly GlassSheenRaster sheen = new();
    private readonly GlassGeometry edgeGeometry = new();
    private readonly BoxShadowRaster shadow = new(6, 2, Color.FromArgb(90, 0, 0, 0));
    private readonly Color? underlay;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private double emphasis, from, target;
    private bool emphasized;
    private LiveGlassBackdrop? popupBackdrop;
    internal long PopupBackdropPaintVersion { get; set; }
    internal void DrawLiveGlass(IGraphicsContext context) => popupBackdrop?.Draw(this, context, presented: true);
    internal void UseLiveBackdrop(LiveGlassBackdrop backdrop)
    { popupBackdrop?.Unregister(this); popupBackdrop = backdrop; backdrop.Register(this); InvalidateVisual(); }
    internal bool HoverEnabled { get; init; } = true;
    internal bool ShadowEnabled { get; init; } = true;
    internal bool ReflectionEnabled { get; init; } = true;

    internal GlassCard(Color? underlay = null)
    {
        this.underlay = underlay;
        CornerRadius = 20; BorderThickness = 0; Background = GlassSheen.Surface;
        Transitions = [Transition.Create(Control.BackgroundProperty, 180)];
        motion.TickCallback = value => { emphasis = from + (target - from) * value; InvalidateVisual(); };
        MouseEnter += Refresh; MouseLeave += Refresh;
    }

    internal GlassCard Child(FrameworkElement content) { Content = content; return this; }
    internal void SetEmphasized(bool value) { emphasized = value; Refresh(); }
    private void Refresh()
    {
        var next = emphasized ? 1 : HoverEnabled && IsMouseOver ? .45 : 0;
        if (next == target) return;
        motion.Stop(); from = emphasis; target = next;
        if (FindVisualRoot() is Window) motion.Start(); else { emphasis = next; InvalidateVisual(); }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (ShadowEnabled) shadow.Draw(context, Bounds, CornerRadius, (byte)(65 + emphasis * 25) / 90d);
        if (popupBackdrop != null && !WindowPresentation.IsRecording(context))
            popupBackdrop.Draw(this, context, presented: false);
        if (popupBackdrop == null)
        {
            // Drawers keep the shared translucent finish without leaking underlying text.
            if (underlay is { } color) context.FillRoundedRectangle(Bounds, CornerRadius, CornerRadius, color);
            base.OnRender(context);
        }
        RenderGlassBackground(context);
        var corners = new CornerRadius(CornerRadius);
        if (ReflectionEnabled) sheen.Draw(context, Bounds, corners, emphasis, GetDpi() / 96d);
        else GlassSheen.DrawEdge(context, Bounds, corners, edgeGeometry, emphasis);
    }

    protected virtual void RenderGlassBackground(IGraphicsContext context) { }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { motion.Stop(); emphasis = target; sheen.Dispose(); shadow.Dispose(); }
    }

    protected override void OnDispose() { popupBackdrop?.Unregister(this); motion.Stop(); sheen.Dispose(); shadow.Dispose(); base.OnDispose(); }
}
