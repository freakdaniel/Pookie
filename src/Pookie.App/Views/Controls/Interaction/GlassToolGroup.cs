using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A shared glass envelope keeps segmented tools visually joined to the search field.
internal sealed class GlassToolGroup : ContentControl
{
    private readonly GlassSheenRaster sheen = new();
    private readonly BoxShadowRaster shadow = new(6, 2, Color.FromArgb(90, 0, 0, 0));
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private double emphasis, from, target;

    internal GlassToolGroup(FrameworkElement content)
    {
        Height = 40; CornerRadius = 20; Padding = new Thickness(4); BorderThickness = 0;
        Background = Color.Transparent; Content = content;
        motion.TickCallback = value => { emphasis = from + (target - from) * value; InvalidateVisual(); };
        MouseEnter += Refresh; MouseLeave += Refresh;
    }

    private void Refresh()
    {
        var next = IsMouseOver ? .45 : 0;
        if (next == target) return;
        motion.Stop(); from = emphasis; target = next;
        if (FindVisualRoot() is Window) motion.Start(); else { emphasis = next; InvalidateVisual(); }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        shadow.Draw(context, Bounds, 20, (byte)(65 + emphasis * 25) / 90d);
        context.FillRoundedRectangle(Bounds, 20, 20, Color.FromArgb((byte)(218 + emphasis * 18), 49, 49, 55));
        sheen.Draw(context, Bounds, new CornerRadius(20), emphasis, GetDpi() / 96d);
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { motion.Stop(); emphasis = target; sheen.Dispose(); shadow.Dispose(); }
    }

    protected override void OnDispose() { motion.Stop(); sheen.Dispose(); shadow.Dispose(); base.OnDispose(); }
}
