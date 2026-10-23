using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// One page scrollbar moves the page heading out first, then hands the remaining
// offset to its existing virtualized list. Lists keep a finite viewport throughout.
internal sealed class ShellPageScroll : Panel
{
    private readonly FrameworkElement content;
    private readonly ScrollViewer owner;
    private readonly Func<ScrollViewer?> current;
    private readonly Func<double, bool> scroll;
    private readonly Func<double> trailingSpace;
    private readonly Dictionary<ScrollViewer, (Action Changed, Action<MouseWheelEventArgs> Wheel)> handlers = [];
    private ScrollViewer? inner;
    private double prefix, tail, extent, collapse;
    private bool applyingOffset;
    internal bool IsReady => ReferenceEquals(inner, current()) && !IsMeasureDirty && !IsArrangeDirty &&
        Math.Abs(extent - ContentExtent()) < .5;

    internal ShellPageScroll(FrameworkElement content, ScrollViewer owner, Func<ScrollViewer?> current,
        Func<double, bool> scroll, Func<double> trailingSpace)
    {
        this.content = content; this.owner = owner; this.current = current; this.scroll = scroll; this.trailingSpace = trailingSpace;
        // Keep the native thumb, track and hit testing below the glass header.
        // The content viewport still spans the header for the scroll-under effect.
        if (VerticalBar(owner) is { } bar) bar.Margin = new Thickness(0, ShellChromeGlass.HeaderHeight, 0, 0);
        Add(content);
        owner.SizeChanged += OnViewportChanged;
    }

    private void OnViewportChanged(SizeChangedEventArgs _) => InvalidateMeasure();

    internal void Refresh()
    {
        var selected = current();
        if (!ReferenceEquals(inner, selected))
        {
            inner = selected; prefix = tail = collapse = 0;
            if (inner != null && !handlers.ContainsKey(inner))
            {
                var viewer = inner;
                void Changed()
                {
                    if (applyingOffset || !ReferenceEquals(inner, viewer)) return;
                    var expected = Math.Clamp(owner.VerticalOffset - prefix, 0, SmoothScroll.Maximum(viewer));
                    if (Math.Abs(viewer.VerticalOffset - expected) > 1)
                        owner.SetScrollOffsets(0, prefix + viewer.VerticalOffset);
                }
                void Wheel(MouseWheelEventArgs args)
                {
                    if (args.Handled || args.Delta.Y == 0) return;
                    args.Handled = scroll(-args.Delta.Y * ThemeManager.DefaultMetrics.ScrollWheelStep);
                }
                viewer.ScrollChanged += Changed; viewer.MouseWheel += Wheel;
                handlers.Add(viewer, (Changed, Wheel));
            }
            InvalidateMeasure();
        }
        if (inner != null)
        {
            inner.CornerRadius = 0;
            prefix = Math.Max(0, inner.Bounds.Y - content.Bounds.Y);
            tail = Math.Max(0, content.Bounds.Bottom - inner.Bounds.Bottom);
            if (VerticalBar(inner) is { } bar) { bar.Opacity = 0; bar.IsHitTestVisible = false; }
        }
        var nextCollapse = inner == null ? 0 : Math.Min(owner.VerticalOffset, prefix);
        if (Math.Abs(collapse - nextCollapse) > .001) { collapse = nextCollapse; InvalidateMeasure(); }
        var next = ContentExtent();
        if (Math.Abs(extent - next) > .5) { extent = next; InvalidateMeasure(); }
    }

    private double ContentExtent()
    {
        if (inner == null) return owner.ActualHeight;
        var maximum = SmoothScroll.Maximum(inner);
        var height = maximum + inner.ViewportHeight;
        if (maximum <= 0 && inner.Content is FrameworkElement element)
            height = Math.Min(height, Math.Min(inner.DesiredSize.Height, element.DesiredSize.Height));
        var total = prefix + height + tail + trailingSpace();
        // Independent pixel rounding of the heading/list/footer can leave a
        // sub-DIP remainder on a page that otherwise fits its viewport.
        return total <= owner.ActualHeight + 1 ? owner.ActualHeight : total;
    }

    private static ScrollBar? VerticalBar(ScrollViewer viewer)
    {
        ScrollBar? result = null;
        ((IVisualTreeHost)viewer).VisitChildren(element =>
        {
            if (element is not ScrollBar { Orientation: Orientation.Vertical } bar) return true;
            result = bar; return false;
        });
        return result;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var height = owner.ActualHeight > 0 ? owner.ActualHeight : 840;
        content.Measure(new Size(availableSize.Width, height + collapse));
        return new Size(availableSize.Width, Math.Max(height, extent));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var offset = owner.VerticalOffset;
        var maximum = inner == null ? 0 : SmoothScroll.Maximum(inner);
        var desired = Math.Clamp(offset - prefix, 0, maximum);
        // The final breathing room scrolls with the page. Mid-list viewports
        // reach the player, so rows pass through the fade instead of a blank gutter.
        var endShift = inner == null ? 0 : Math.Max(0, offset - prefix - maximum);
        if (inner != null && (Math.Abs(inner.VerticalOffset - desired) > .001 || inner.HorizontalOffset != 0))
        {
            applyingOffset = true;
            try { inner.SetScrollOffsets(0, desired); }
            finally { applyingOffset = false; }
        }
        // Measure only in MeasureContent. At fractional DPI the final rounded
        // width can differ from the constraint and remeasurement here oscillates.
        // Refresh reconciles the heading/viewport changes at the frame boundary.
        content.Arrange(new Rect(Bounds.X, Bounds.Y + offset - collapse - endShift, finalSize.Width, owner.ActualHeight + collapse));
        return finalSize;
    }

    protected override void OnDispose()
    {
        owner.SizeChanged -= OnViewportChanged;
        foreach (var (viewer, handler) in handlers)
        { viewer.ScrollChanged -= handler.Changed; viewer.MouseWheel -= handler.Wheel; }
        base.OnDispose();
    }
}
