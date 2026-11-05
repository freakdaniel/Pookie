using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Logical rows stay attached for ownership, measurement and selection. The public
// visual-tree contract exposes only realized rows to scene and backdrop captures.
internal sealed class ViewportStackPanel : StackPanel, IVisualTreeHost
{
    internal Func<Rect>? Viewport { get; init; }
    internal bool ArrangeVisibleOnly { get; init; } = true;
    private readonly List<UIElement> visible = [];
    private readonly Dictionary<UIElement, List<(UIElement Element, bool TabStop)>> parked = [];
    private bool arranged;

    protected override void ArrangeContent(Rect bounds)
    {
        if (Viewport == null || !ArrangeVisibleOnly) { base.ArrangeContent(bounds); return; }
        visible.Clear();
        arranged = true;
        var viewport = Viewport();
        var area = bounds.Deflate(Padding);
        var y = area.Y;
        var first = true;
        foreach (var child in Children.OfType<UIElement>())
        {
            if (!child.IsVisible) continue;
            if (!first) y += Spacing;
            var row = new Rect(area.X, y, area.Width, child.DesiredSize.Height);
            // Only arrange the viewport and a small prefetch band. Moving a
            // 500-comment panel must not move thousands of descendant controls.
            if (row.Bottom >= viewport.Y - 96 && row.Y <= viewport.Bottom + 96)
            {
                RestoreTabStops(child);
                child.Arrange(row);
                visible.Add(child);
            }
            else if (!parked.ContainsKey(child))
            {
                var tabStops = new List<(UIElement Element, bool TabStop)>();
                VisualTree.Visit(child, element =>
                {
                    if (element is UIElement { Focusable: true } target)
                    { tabStops.Add((target, target.IsTabStop)); target.IsTabStop = false; }
                });
                parked.Add(child, tabStops);
            }
            y += child.DesiredSize.Height;
            first = false;
        }
    }

    protected override void OnChildRemoved(Element child)
    {
        if (child is UIElement removed) visible.Remove(removed);
        if (child is UIElement element) RestoreTabStops(element);
        base.OnChildRemoved(child);
    }

    private void RestoreTabStops(UIElement row)
    {
        if (!parked.Remove(row, out var tabStops)) return;
        foreach (var (element, tabStop) in tabStops) element.IsTabStop = tabStop;
    }

    internal bool TryGetLayoutBounds(Element descendant, out Rect bounds)
    {
        var row = descendant;
        while (row.Parent != null && row.Parent != this) row = row.Parent;
        var area = Bounds.Deflate(Padding);
        var y = area.Y;
        foreach (var child in Children.OfType<UIElement>())
        {
            if (!child.IsVisible) continue;
            if (ReferenceEquals(child, row))
            { bounds = new Rect(area.X, y, area.Width, child.DesiredSize.Height); return true; }
            y += child.DesiredSize.Height + Spacing;
        }
        bounds = default; return false;
    }

    protected override ChildClip? GetChildClip(UIElement child) =>
        Viewport != null ? new ChildClip(Viewport()) : base.GetChildClip(child);

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        // Root attachment/detachment must reach every owned row, including those
        // which have not been realized yet. Panel's logical tree remains complete.
        if (!arranged || Viewport == null || !ArrangeVisibleOnly)
        {
            foreach (var child in ChildrenList) if (!visitor(child)) return false;
        }
        else foreach (var child in visible) if (!visitor(child)) return false;
        return true;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        arranged = false;
        base.OnVisualRootChanged(oldRoot, newRoot);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (Viewport == null || !ArrangeVisibleOnly) return base.OnHitTest(point);
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled || !Bounds.Contains(point) || !Viewport().Contains(point)) return null;
        for (var i = visible.Count - 1; i >= 0; i--)
            if (visible[i].HitTest(point) is { } hit) return hit;
        return this;
    }
}
