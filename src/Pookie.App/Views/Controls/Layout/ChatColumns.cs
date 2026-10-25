using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class ChatColumns(FrameworkElement inbox, FrameworkElement thread, Action<bool> compactChanged) : Panel
{
    private bool selected, compact, panelOpen, panelVisible, docked;
    private double reveal;
    private readonly AnimationClock panelAnimation = new(TimeSpan.FromMilliseconds(280), Easing.CubicBezier(.4, 0, .2, 1));
    private double sidebar;
    private FrameworkElement? details;
    private readonly Border dimmer = new Border().Background(Color.FromArgb(80, 0, 0, 0)).BorderThickness(0).CornerRadius(0).Padding(0);
    internal Action? CloseDetails;
    internal bool Animating => panelAnimation.IsRunning;
    internal double Reveal => reveal;
    // Let the history scrollbar use the gap beside docked details while the
    // reading column and floating controls keep their original position.
    internal double ScrollGutter => docked ? 16 * reveal : 0;
    internal bool Selected { get => selected; set { if (selected == value) return; selected = value; InvalidateMeasure(); } }
    internal bool DetailsOpen
    {
        get => panelOpen;
        set
        {
            if (panelOpen == value) return;
            panelOpen = value; panelVisible = true; if (details != null) details.IsHitTestVisible = value && reveal > .7; dimmer.IsHitTestVisible = value; var from = reveal; var to = value ? 1d : 0d;
            panelAnimation.Stop();
            if (details != null) details.Opacity = reveal;
            dimmer.Opacity = reveal;
            panelAnimation.TickCallback = t => { reveal = from + (to - from) * t; if (details != null) { details.Opacity = reveal; details.IsHitTestVisible = panelOpen && reveal > .7; } dimmer.Opacity = reveal; InvalidateMeasure(); };
            panelAnimation.CompletedCallback = () => { reveal = to; panelVisible = panelOpen; InvalidateMeasure(); };
            panelAnimation.Start(); InvalidateMeasure();
        }
    }
    internal void ResetDetails()
    {
        panelAnimation.Stop(); panelOpen = panelVisible = false; reveal = 0;
        if (details != null) { details.IsVisible = false; details.IsHitTestVisible = false; details.Opacity = 0; }
        dimmer.IsVisible = dimmer.IsHitTestVisible = false; InvalidateMeasure();
    }
    internal ChatColumns Initialize() { Add(inbox); Add(thread); return this; }
    internal void SetDetails(FrameworkElement element)
    {
        details = element; Add(dimmer); Add(element);
        dimmer.OnMouseDown(e => { if (e.Button == MouseButton.Left) { e.Handled = true; CloseDetails?.Invoke(); } });
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1000;
        var narrow = width < 760;
        if (compact != narrow) { compact = narrow; compactChanged(compact); }
        sidebar = Math.Clamp(width * .29, 260, 320);
        var center = compact ? width : Math.Max(0, width - sidebar - 16);
        docked = panelVisible && center - 336 >= 560;
        inbox.IsVisible = !compact || !selected;
        thread.IsVisible = !compact || selected;
        if (inbox.IsVisible) inbox.Measure(new Size(compact ? width : sidebar, availableSize.Height));
        if (thread.IsVisible) thread.Measure(new Size(Math.Max(0, center - (docked ? 320 * reveal : 0)), availableSize.Height));
        dimmer.IsVisible = panelVisible && !docked;
        if (dimmer.IsVisible) dimmer.Measure(new Size(width, availableSize.Height));
        if (details != null) { details.IsVisible = panelVisible; if (panelVisible) details.Measure(new Size(Math.Min(320, center), availableSize.Height)); }
        return new Size(width, double.IsFinite(availableSize.Height) ? availableSize.Height : 600);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var start = compact ? Bounds.X : Bounds.X + sidebar + 16;
        var center = compact ? finalSize.Width : Math.Max(0, finalSize.Width - sidebar - 16);
        var room = Math.Max(0, center - (docked ? 320 * reveal : 0));
        if (inbox.IsVisible) inbox.Arrange(new Rect(Bounds.X, Bounds.Y, compact ? finalSize.Width : sidebar, finalSize.Height));
        if (thread.IsVisible) thread.Arrange(new Rect(start, Bounds.Y, room, finalSize.Height));
        if (dimmer.IsVisible) dimmer.Arrange(new Rect(start, Bounds.Y, center, finalSize.Height));
        if (panelVisible && details != null) details.Arrange(new Rect(Bounds.Right - Math.Min(320, center) + (Math.Min(320, center) + 16) * (1 - reveal), Bounds.Y, Math.Min(320, center), finalSize.Height));
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    {
        context.Save();
        try { context.SetClip(Bounds); base.RenderSubtree(context); }
        finally { context.Restore(); }
    }
    protected override void OnDispose() { panelAnimation.Stop(); base.OnDispose(); }
}
