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
    private readonly Func<MouseWheelEventArgs, bool> scroll;
    private readonly Func<double> trailingSpace;
    private readonly Func<double> maximumContentWidth;
    private readonly Dictionary<ScrollViewer, (Action Changed, Action<MouseWheelEventArgs> Wheel)> handlers = [];
    private ScrollViewer? inner;
    private double prefix, tail, extent, collapse;
    private bool applyingOffset;
    private bool layoutFrameQueued;
    internal bool IsReady => ReferenceEquals(inner, current()) && !IsMeasureDirty && !IsArrangeDirty &&
        Math.Abs(extent - ContentExtent()) < .5;

    internal ShellPageScroll(FrameworkElement content, ScrollViewer owner, Func<ScrollViewer?> current,
        Func<MouseWheelEventArgs, bool> scroll, Func<double> trailingSpace, Func<double> maximumContentWidth)
    {
        this.content = content; this.owner = owner; this.current = current; this.scroll = scroll; this.trailingSpace = trailingSpace;
        this.maximumContentWidth = maximumContentWidth;
        // Keep the native thumb, track and hit testing below the glass header.
        // The content viewport still spans the header for the scroll-under effect.
        if (VerticalBar(owner) is { } bar) bar.Margin = new Thickness(0, ShellHeader.HeaderHeight, 0, 0);
        Add(content);
        owner.SizeChanged += OnViewportChanged;
    }

    private void OnViewportChanged(SizeChangedEventArgs _) => InvalidateMeasure();

    internal void Refresh(bool scheduleFrame = true)
    {
        var layoutChanged = false;
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
                    args.Handled = scroll(args);
                }
                viewer.ScrollChanged += Changed; viewer.MouseWheel += Wheel;
                handlers.Add(viewer, (Changed, Wheel));
            }
            layoutChanged = true;
        }
        if (inner != null)
        {
            inner.CornerRadius = 0;
            prefix = Math.Max(0, inner.Bounds.Y - content.Bounds.Y);
            tail = Math.Max(0, content.Bounds.Bottom - inner.Bounds.Bottom);
            if (VerticalBar(inner) is { } bar) { bar.Opacity = 0; bar.IsHitTestVisible = false; }
        }
        var nextCollapse = inner == null ? 0 : Math.Min(owner.VerticalOffset, prefix);
        if (Math.Abs(collapse - nextCollapse) > .001) { collapse = nextCollapse; layoutChanged = true; }
        var next = ContentExtent();
        if (Math.Abs(extent - next) > .5) { extent = next; layoutChanged = true; }
        // A descendant can request another arrange while an ancestor is still
        // completing this pass. The ancestor then clears its own dirty flag.
        // Reissue that pending request at the frame boundary, including when
        // the content extent has not changed and no animation will wake us.
        if (!layoutChanged && !IsMeasureDirty && !IsArrangeDirty) return;
        if (layoutChanged) InvalidateMeasure();
        else if (IsMeasureDirty) owner.InvalidateMeasure();
        else owner.InvalidateArrange();
        // When called after rendering, commit the updated extent on a new
        // dispatcher turn even if playback and other animations are idle.
        if (!scheduleFrame || layoutFrameQueued) return;
        layoutFrameQueued = true;
        Application.Current.Dispatcher!.BeginInvoke(() =>
        {
            layoutFrameQueued = false;
            if (FindVisualRoot() is Window window) window.InvalidateVisual();
        });
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
        content.Measure(new Size(Math.Min(availableSize.Width, maximumContentWidth()), height + collapse));
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
        // ScrollViewer rounds its offset to physical pixels. Reapplying a
        // subpixel residual on every arrange dirties the same viewport again
        // and sends popup-dismiss notifications even though nothing moved.
        var pixel = 96d / GetDpi();
        if (inner != null && (Math.Abs(inner.VerticalOffset - desired) > pixel + .001 || inner.HorizontalOffset != 0))
        {
            applyingOffset = true;
            try { inner.SetScrollOffsets(0, desired); }
            finally { applyingOffset = false; }
        }
        // Measure only in MeasureContent. At fractional DPI the final rounded
        // width can differ from the constraint and remeasurement here oscillates.
        // Refresh reconciles the heading/viewport changes at the frame boundary.
        // Arrange to the same width used for measurement. Updating Width
        // after the native resize laid out the old slot and dirtied the tree again.
        var width = Math.Min(finalSize.Width, maximumContentWidth());
        content.Arrange(new Rect(Bounds.X + (finalSize.Width - width) / 2, Bounds.Y + offset - collapse - endShift,
            width, owner.ActualHeight + collapse));
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
