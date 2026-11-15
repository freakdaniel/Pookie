using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// A virtualized list inside a document with other content (hero, sidebar, tabs).
// The outer ScrollViewer owns scrolling; the inner list receives only its finite
// visible window while this host reports the complete logical document height.
internal sealed class ViewportItemsHost(ItemsControl items, Func<Rect> viewport,
    Func<MouseWheelEventArgs, bool> scrollWheel, Action<double> correctOffset,
    Action<double>? configureWidth = null, double trailingGap = 0) : Panel
{
    private double extent;
    private bool refreshQueued;
    private bool applyingOffset;
    private bool arranged;
    private double requestedOffset;
    private ScrollViewer? viewer;
    internal ItemsControl Items => items;
    internal bool IsReady => arranged && !refreshQueued && !IsMeasureDirty && !IsArrangeDirty &&
        Math.Abs(Math.Max(0, LogicalExtent() - (items.ItemsSource.Count > 0 ? trailingGap : 0)) - extent) < .5;
    internal ViewportItemsHost Initialize() { Add(items); items.ScrollChanged += OnScrollChanged; return this; }

    private void OnWheel(MouseWheelEventArgs args)
    { if (!args.Handled) args.Handled = scrollWheel(args); }

    private void OnScrollChanged()
    {
        if (applyingOffset || !arranged || items.IsMeasureDirty || IsMeasureDirty) return;
        CarryOffsetCorrection();
    }

    private void CarryOffsetCorrection()
    {
        if (viewer == null || FindVisualRoot() is not Window) return;
        var correction = viewer.VerticalOffset - requestedOffset;
        if (Math.Abs(correction) < .5) return;
        requestedOffset = viewer.VerticalOffset;
        correctOffset(correction);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1000;
        var height = viewport().Height > 0 ? viewport().Height : 840;
        configureWidth?.Invoke(width);
        items.Measure(new Size(width, height));
        if (viewer == null && items.FindVisualChild<ScrollViewer>() is ScrollViewer inner)
        { viewer = inner; viewer.MouseWheel += OnWheel; }
        extent = Math.Max(0, LogicalExtent() - (items.ItemsSource.Count > 0 ? trailingGap : 0));
        return new Size(width, extent);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var area = viewport();
        var height = Math.Max(1, area.Height);
        var offset = Math.Clamp(area.Y - Bounds.Y, 0, Math.Max(0, extent - height));
        requestedOffset = offset;
        applyingOffset = true;
        try
        {
            viewer?.SetScrollOffsets(0, offset);
            items.Arrange(new Rect(Bounds.X, Bounds.Y + offset, finalSize.Width, height));
        }
        finally { applyingOffset = false; }
        // Variable-height rows can refine the anchor during that arrange pass.
        // Carry its correction to the document owner instead of losing it when
        // the next outer arrange supplies the previous logical offset again.
        if (arranged) CarryOffsetCorrection();
        arranged = true;
        if (viewer is { } scroll)
        {
            scroll.CornerRadius = 0;
            ((IVisualTreeHost)scroll).VisitChildren(child =>
            {
                if (child is ScrollBar { Orientation: Orientation.Vertical } bar)
                { bar.Opacity = 0; bar.IsHitTestVisible = false; }
                return true;
            });
            var logical = Math.Max(0, LogicalExtent() - (items.ItemsSource.Count > 0 ? trailingGap : 0));
            if (Math.Abs(logical - extent) > .5 && !refreshQueued)
            {
                refreshQueued = true;
                Application.Current.Dispatcher!.BeginInvoke(() =>
                {
                    refreshQueued = false;
                    if (FindVisualRoot() is Window) InvalidateMeasure();
                });
            }
        }
        return finalSize;
    }

    private double LogicalExtent()
    {
        if (viewer == null) return items.DesiredSize.Height;
        if (items.ItemsSource.Count == 0) return 0;
        // A short variable-height list's preferred height is only an estimate.
        // Once its last container is realized, use its actual padded boundary.
        if (items.TryGetContainer(items.ItemsSource.Count - 1, out var last) && last.ActualHeight > 0)
            return Math.Max(0, last.Bounds.Bottom + items.ItemPadding.Bottom - viewer.Bounds.Y + viewer.VerticalOffset);
        var maximum = SmoothScroll.Maximum(viewer);
        var height = maximum + viewer.ViewportHeight;
        return maximum > 0 ? height : Math.Min(height, items.DesiredSize.Height);
    }

    protected override void OnDispose()
    {
        items.ScrollChanged -= OnScrollChanged;
        if (viewer != null) viewer.MouseWheel -= OnWheel;
        base.OnDispose();
    }
}
