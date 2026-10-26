using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The input and its expanding results share one continuous surface.
internal sealed class ChatSearchPopup : Panel
{
    private readonly FrameworkElement header, results;
    private readonly AnimationClock reveal = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.4, 0, .2, 1));
    private readonly AnimationClock visibility = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.4, 0, .2, 1));
    private readonly GlassSheenRaster sheen = new();
    private readonly BoxShadowRaster shadow = new(8, 3, Color.FromArgb(65, 0, 0, 0));
    private double height, from, target;
    private bool searching, expanded;
    internal bool Animating => reveal.IsRunning || visibility.IsRunning;
    internal bool Expanded { get => expanded; set { if (expanded == value) return; expanded = value; InvalidateMeasure(); } }
    internal bool Searching
    {
        get => searching;
        set
        {
            if (searching == value) return;
            searching = value; visibility.Stop(); var opacity = Opacity;
            if (value) { IsVisible = true; IsHitTestVisible = true; Expanded = true; }
            else { IsHitTestVisible = false; Expanded = false; }
            visibility.TickCallback = t => Opacity = opacity + ((value ? 1 : 0) - opacity) * t;
            visibility.CompletedCallback = () => { if (!searching) IsVisible = false; };
            visibility.Start(); InvalidateMeasure();
        }
    }
    internal void Reset()
    {
        reveal.Stop(); visibility.Stop(); searching = expanded = false; height = target = 0; Opacity = 0; IsVisible = false; IsHitTestVisible = false; InvalidateMeasure();
    }
    internal ChatSearchPopup(FrameworkElement header, FrameworkElement results)
    {
        this.header = header; this.results = results; Add(header); Add(results);
        IsVisible = false; Opacity = 0; VerticalAlignment = VerticalAlignment.Top;
        reveal.TickCallback = t => { height = from + (target - from) * t; results.Opacity = target > 0 ? Math.Clamp(height / target, 0, 1) : 1 - t; InvalidateMeasure(); };
        reveal.CompletedCallback = () => { height = target; results.IsHitTestVisible = expanded; InvalidateMeasure(); };
    }
    protected override Size MeasureContent(Size availableSize)
    {
        header.Measure(new Size(Math.Max(0, availableSize.Width - 20), 40));
        results.Measure(new Size(Math.Max(0, availableSize.Width - 12), 277));
        var next = expanded ? Math.Min(286, results.DesiredSize.Height + 9) : 0;
        if (Math.Abs(next - target) > .5)
        {
            reveal.Stop(); from = height; target = next; results.IsHitTestVisible = false;
            if (FindVisualRoot() is Window) reveal.Start(); else height = next;
        }
        return new Size(availableSize.Width, 40 + height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        header.Arrange(new Rect(Bounds.X + 10, Bounds.Y, Math.Max(0, finalSize.Width - 20), 40));
        results.Arrange(new Rect(Bounds.X + 6, Bounds.Y + 48, Math.Max(0, finalSize.Width - 12), Math.Max(0, (reveal.IsRunning ? Math.Max(from,target) : target) - 9)));
        return finalSize;
    }
    protected override void OnRender(IGraphicsContext context)
    {
        shadow.Draw(context, Bounds, 21);
        context.FillRoundedRectangle(Bounds, 21, 21, Color.FromRgb(35,35,39));
        sheen.Draw(context, Bounds, new CornerRadius(21), .15, GetDpi()/96d);
        if (height > 1) context.FillRectangle(new Rect(Bounds.X + 10, Bounds.Y + 40, Math.Max(0, Bounds.Width - 20), .5), Color.FromArgb(26,255,255,255));
    }
    protected override void RenderSubtree(IGraphicsContext context)
    { context.Save(); try { context.SetClipRoundedRect(Bounds, 21,21); base.RenderSubtree(context); } finally { context.Restore(); } }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null) shadow.Dispose(); }
    protected override void OnDispose() { reveal.Stop(); visibility.Stop(); sheen.Dispose(); shadow.Dispose(); base.OnDispose(); }
}
