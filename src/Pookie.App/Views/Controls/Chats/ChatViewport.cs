using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Synchronize scroll offsets at the paint boundary without re-entering measurement.
internal sealed class ChatViewport(FrameworkElement content) : Panel
{
    internal Action? BeforePaint;
    internal ChatViewport Initialize() { Add(content); return this; }
    protected override Size MeasureContent(Size availableSize)
    { content.Measure(availableSize); return content.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height);
        // The measure constraint and final pixel-rounded bounds can differ at
        // fractional DPI. Remeasuring here makes the virtualized viewport
        // oscillate between them and continuously invalidate the window layout.
        content.Arrange(bounds);
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    { BeforePaint?.Invoke(); base.RenderSubtree(context); }
}
