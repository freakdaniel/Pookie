using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class ChatColumns(FrameworkElement inbox, FrameworkElement thread, Action<bool> compactChanged) : Panel
{
    internal const double SidebarWidth = 360;
    internal const double ColumnGap = 16;
    internal const double ReadingWidth = 696;
    private const double CompactWidth = 760;
    private bool selected, compact, panelOpen, panelVisible, docked;
    private double reveal;
    private readonly AnimationClock panelAnimation = new(TimeSpan.FromMilliseconds(280), Easing.CubicBezier(.4, 0, .2, 1));
    private FrameworkElement? details;
    private readonly Border outsideClickLayer = new Border().Background(Color.Transparent).BorderThickness(0).CornerRadius(0).Padding(0);
    internal Action? CloseDetails;
    internal FrameworkElement? OverlayHeader;
    internal bool Animating => panelAnimation.IsRunning;
    internal double Reveal => reveal;
    // Let the history scrollbar use the gap beside docked details while the
    // reading column and floating controls keep their original position.
    internal double ScrollGutter => docked ? ColumnGap * reveal : 0;
    internal bool Selected { get => selected; set { if (selected == value) return; selected = value; InvalidateMeasure(); } }
    internal bool DetailsOpen
    {
        get => panelOpen;
        set
        {
            if (panelOpen == value) return;
            panelOpen = value; panelVisible = true; if (details != null) details.IsHitTestVisible = value && reveal > .7; outsideClickLayer.IsHitTestVisible = value; var from = reveal; var to = value ? 1d : 0d;
            panelAnimation.Stop();
            if (details != null) details.Opacity = reveal;
            panelAnimation.TickCallback = t => { reveal = from + (to - from) * t; if (details != null) { details.Opacity = reveal; details.IsHitTestVisible = panelOpen && reveal > .7; } InvalidateMeasure(); };
            panelAnimation.CompletedCallback = () => { reveal = to; panelVisible = panelOpen; InvalidateMeasure(); };
            panelAnimation.Start(); InvalidateMeasure();
        }
    }
    internal void ResetDetails()
    {
        panelAnimation.Stop(); panelOpen = panelVisible = false; reveal = 0;
        if (details != null) { details.IsVisible = false; details.IsHitTestVisible = false; details.Opacity = 0; }
        outsideClickLayer.IsVisible = outsideClickLayer.IsHitTestVisible = false; InvalidateMeasure();
    }
    internal ChatColumns Initialize() { Add(inbox); Add(thread); return this; }
    internal void SetDetails(FrameworkElement element)
    {
        details = element; Add(outsideClickLayer); Add(element);
        outsideClickLayer.OnMouseDown(e => { if (e.Button == MouseButton.Left) { e.Handled = true; CloseDetails?.Invoke(); } });
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1000;
        var narrow = width < CompactWidth;
        if (compact != narrow) { compact = narrow; compactChanged(compact); }
        var center = compact ? width : Math.Max(0, width - SidebarWidth - ColumnGap);
        // Dock only when the complete reading column fits. Otherwise the drawer
        // overlays an unchanged conversation instead of narrowing its messages.
        docked = panelVisible && !compact && center - SidebarWidth - ColumnGap >= ReadingWidth;
        inbox.IsVisible = !compact || !selected;
        thread.IsVisible = !compact || selected;
        if (inbox.IsVisible) inbox.Measure(new Size(compact ? width : SidebarWidth, availableSize.Height));
        if (thread.IsVisible) thread.Measure(new Size(Math.Max(0, center - (docked ? SidebarWidth * reveal : 0)), availableSize.Height));
        outsideClickLayer.IsVisible = panelVisible && !docked;
        if (outsideClickLayer.IsVisible) outsideClickLayer.Measure(new Size(width, availableSize.Height));
        if (details != null) { details.IsVisible = panelVisible; if (panelVisible) details.Measure(new Size(Math.Min(SidebarWidth, center), availableSize.Height)); }
        return new Size(width, double.IsFinite(availableSize.Height) ? availableSize.Height : 600);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var start = compact ? Bounds.X : Bounds.X + SidebarWidth + ColumnGap;
        var center = compact ? finalSize.Width : Math.Max(0, finalSize.Width - SidebarWidth - ColumnGap);
        var room = Math.Max(0, center - (docked ? SidebarWidth * reveal : 0));
        if (inbox.IsVisible) inbox.Arrange(new Rect(Bounds.X, Bounds.Y, compact ? finalSize.Width : SidebarWidth, finalSize.Height));
        if (thread.IsVisible) thread.Arrange(new Rect(start, Bounds.Y, room, finalSize.Height));
        if (outsideClickLayer.IsVisible) outsideClickLayer.Arrange(new Rect(start, Bounds.Y, center, finalSize.Height));
        var detailsWidth = Math.Min(SidebarWidth, center);
        if (panelVisible && details != null) details.Arrange(new Rect(Bounds.Right - detailsWidth + (detailsWidth + ColumnGap) * (1 - reveal), Bounds.Y, detailsWidth, finalSize.Height));
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    {
        context.Save();
        try { context.SetClip(Bounds); base.RenderSubtree(context); }
        finally { context.Restore(); }
    }
    protected override UIElement? OnHitTest(Point point)
    {
        // The transparent outside-click layer dismisses from the history, but
        // must not turn a click on the already-open profile header into Close.
        // Search uses a separate overlapping subtree, so route through thread.
        if (outsideClickLayer.IsVisible && OverlayHeader is { } header && header.Bounds.Contains(point))
        {
            if (details?.HitTest(point) is { } detailsHit) return detailsHit;
            if (thread.HitTest(point) is { } headerHit) return headerHit;
        }
        return base.OnHitTest(point);
    }
    protected override void OnDispose() { panelAnimation.Stop(); base.OnDispose(); }
}
