using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A compatibility render subtree does not inherit retained-scene viewport
// culling. Respect the actual scroll clip before entering expensive comment
// trees (text layout, vector rasterization and button state animations).
internal sealed class ViewportStackPanel : StackPanel
{
    internal Func<Rect>? Viewport { get; init; }
    internal bool ArrangeVisibleOnly { get; init; } = true;
    private readonly List<UIElement> visible = [];
    private readonly Dictionary<UIElement, List<(UIElement Element, bool TabStop)>> parked = [];

    protected override void ArrangeContent(Rect bounds)
    {
        if (Viewport == null || !ArrangeVisibleOnly) { base.ArrangeContent(bounds); return; }
        visible.Clear();
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

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var clip = context.GetClipBoundsLocal();
        if (Viewport != null) clip = clip is { } current ? current.Intersect(Viewport()) : Viewport();
        foreach (var child in Viewport == null || !ArrangeVisibleOnly ? Children.OfType<UIElement>() : visible)
            if (clip == null || child.Bounds.IntersectsWith(clip.Value)) child.Render(context);
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
