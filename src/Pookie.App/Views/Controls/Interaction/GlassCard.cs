using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A resizable glass surface for content cards and native multiline editors.
internal class GlassCard : ContentControl
{
    private readonly GlassSheenRaster sheen = new();
    private readonly Color? underlay;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private double emphasis, from, target;
    private bool emphasized;
    internal bool HoverEnabled { get; init; } = true;

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
        context.DrawBoxShadow(Bounds, CornerRadius, 6, Color.FromArgb((byte)(65 + emphasis * 25), 0, 0, 0), offsetY: 2);
        // Drawers keep the shared translucent finish without leaking underlying text.
        if (underlay is { } color) context.FillRoundedRectangle(Bounds, CornerRadius, CornerRadius, color);
        base.OnRender(context);
        RenderGlassBackground(context);
        sheen.Draw(context, Bounds, new CornerRadius(CornerRadius), emphasis, GetDpi() / 96d);
    }

    protected virtual void RenderGlassBackground(IGraphicsContext context) { }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { motion.Stop(); emphasis = target; sheen.Dispose(); }
    }

    protected override void OnDispose() { motion.Stop(); sheen.Dispose(); base.OnDispose(); }
}
