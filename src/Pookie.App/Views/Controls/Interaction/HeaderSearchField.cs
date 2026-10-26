using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The native TextBox keeps selection, IME and clipboard support. The translucent
// surface sits over the shell's live blurred backdrop without a second capture.
internal sealed class HeaderSearchField : ContentControl
{
    private readonly GlassSurface surface;
    private readonly TextBox input;
    private readonly BoxShadowRaster shadow = new(6, 2, Color.FromArgb(90, 0, 0, 0));
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private double emphasis, from, target;

    internal HeaderSearchField(TextBox input, bool compact = false)
    {
        this.input = input;
        Height = compact ? 40 : 48; Padding = compact ? new Thickness(0) : new Thickness(6, 4); BorderThickness = 0; Background = Color.Transparent;
        input.Background(Color.Transparent).BorderThickness(0).Padding(0).Height(28).FontSize(compact ? 13 : 14).Foreground(TrackButtons.OnSurface);
        surface = new GlassSurface().Height(40).CornerRadius(20).Padding(16, 0)
            .Content(new Grid().Columns(compact ? "20,*" : "24,*").Rows("*").Spacing(compact ? 10 : 12).Children(
                Icons.View("magnifying-glass", compact ? 20 : 22, TrackButtons.OnSurface).Center().Column(0), input.CenterVertical().Column(1)));
        Content = surface;
        motion.TickCallback = value =>
        {
            emphasis = from + (target - from) * value; surface.Emphasis = emphasis;
            surface.InvalidateVisual(); InvalidateVisual();
        };
        MouseEnter += Refresh; MouseLeave += Refresh;
        MouseDown += args => { if (args.Button == MouseButton.Left) input.Focus(); };
        input.GotFocus += Refresh; input.LostFocus += Refresh;
    }

    private void Refresh()
    {
        var next = input.IsFocused ? 1 : IsMouseOver ? .45 : 0;
        if (target == next) return;
        motion.Stop(); from = emphasis; target = next;
        if (FindVisualRoot() is Window) motion.Start();
        else { emphasis = next; surface.Emphasis = next; surface.InvalidateVisual(); }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (surface.Bounds.Width <= 0) return;
        shadow.Draw(context, surface.Bounds, 20, (byte)(65 + emphasis * 25) / 90d);
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null) shadow.Dispose(); }

    protected override void OnDispose() { motion.Stop(); shadow.Dispose(); base.OnDispose(); }

    private sealed class GlassSurface : ContentControl
    {
        private readonly GlassSheenRaster sheen = new();
        internal double Emphasis { get; set; }

        protected override void RenderSubtree(IGraphicsContext context)
        {
            context.Save();
            try { context.SetClipRoundedRect(Bounds, 20, 20); base.RenderSubtree(context); }
            finally { context.Restore(); }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            context.FillRoundedRectangle(Bounds, 20, 20, Color.FromArgb((byte)(218 + Emphasis * 18), 49, 49, 55));
            sheen.Draw(context, Bounds, new CornerRadius(20), Emphasis, GetDpi() / 96d);
        }

        protected override void OnDispose() { sheen.Dispose(); base.OnDispose(); }
    }
}
