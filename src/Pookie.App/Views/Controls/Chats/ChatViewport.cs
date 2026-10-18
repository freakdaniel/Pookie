using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Virtualized heights and scroll metrics must converge before painting, rather
// than correcting an already visible frame in Window.FrameRendered.
internal sealed class ChatViewport(FrameworkElement content) : Panel
{
    internal Func<bool>? SynchronizeScroll;
    internal Action? BeforePaint;
    internal ChatViewport Initialize() { Add(content); return this; }
    protected override Size MeasureContent(Size availableSize)
    { content.Measure(availableSize); return content.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height);
        for (var pass = 0; pass < 20; pass++)
        {
            content.Measure(finalSize); content.Arrange(bounds);
            if (SynchronizeScroll?.Invoke() != true) break;
            // Realized rows may have replaced estimated heights during arrange.
            // Refresh the viewer's extent before accepting a clamped offset.
            content.InvalidateMeasure();
        }
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    { BeforePaint?.Invoke(); base.RenderSubtree(context); }
}
