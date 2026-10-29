using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Keep resized viewports anchored without re-entering measurement.
internal sealed class ChatViewport(FrameworkElement content, Color surface) : Panel
{
    private bool composerWasResizing;
    private const double FadeHeight = 12;
    private readonly LinearGradientBrush topFade = new(new Point(0,0), new Point(0,FadeHeight),
        [new(0,surface),new(1,Color.FromArgb(0,surface.R,surface.G,surface.B))]);
    private readonly LinearGradientBrush bottomFade = new(new Point(0,0), new Point(0,FadeHeight),
        [new(0,Color.FromArgb(0,surface.R,surface.G,surface.B)),new(1,surface)]);
    internal Action? BeforePaint;
    internal Func<bool>? SynchronizeScroll;
    internal Func<bool>? ComposerResizing;
    internal Func<bool>? Restoring;
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
    {
        // The composer changes the viewport every animation frame. Position the
        // realized rows at the corrected offset before painting that resize.
        // Normal virtualized extent refinement keeps its next-frame layout.
        var restoring = Restoring?.Invoke() == true;
        var changed=SynchronizeScroll?.Invoke()==true;
        // ScrollIntoView realizes a cached anchor lazily. Complete its bounded
        // arrange/offset refinements before this first restored frame is painted.
        if(restoring)
            for(var pass=0;pass<8 && changed;pass++)
            {
                // Restoring recycled variable-height rows can dirty measurement
                // after their first bind. Reconcile that extent before clamping
                // the saved offset; arrangement alone still sees the estimate.
                if(content.IsMeasureDirty)content.Measure(new Size(ActualWidth,ActualHeight));
                if(content.FindVisualChild<ScrollViewer>() is ScrollViewer viewer)viewer.InvalidateArrange();
                content.InvalidateArrange();
                content.Arrange(new Rect(Bounds.X,Bounds.Y,ActualWidth,ActualHeight));
                changed=SynchronizeScroll?.Invoke()==true;
            }
        if(changed)InvalidateArrange();
        var composerResizing = ComposerResizing?.Invoke() == true;
        // Completion stops the clock before the last resized frame is painted.
        // Apply its corrected bottom offset too, rather than painting the final
        // viewport against row positions from the preceding animation frame.
        if (composerResizing || composerWasResizing)
        {
            content.InvalidateArrange();
            content.Arrange(new Rect(Bounds.X, Bounds.Y, ActualWidth, ActualHeight));
        }
        composerWasResizing = composerResizing;
        BeforePaint?.Invoke(); base.RenderSubtree(context);
        // The conversation has a plain surface. Blend its clipping edge into that
        // surface so a single bottom pixel of an offscreen bubble cannot linger.
        if (content.FindVisualChild<ScrollViewer>() is ScrollViewer scroll)
        {
            if (scroll.VerticalOffset > 1)
                DrawFade(context, new Rect(Bounds.X, Bounds.Y, Bounds.Width, FadeHeight), topFade);
            if (SmoothScroll.Maximum(scroll) - scroll.VerticalOffset > 1)
                DrawFade(context, new Rect(Bounds.X, Bounds.Bottom - FadeHeight, Bounds.Width, FadeHeight), bottomFade);
        }
    }

    internal static void DrawFade(IGraphicsContext context, Rect bounds, LinearGradientBrush brush)
    {
        // A moving viewport changes only the transform, not its gradient-stop
        // collection. Keep native coverage rather than resampling a mask image.
        context.Save();
        try
        {
            context.Translate(bounds.X, bounds.Y);
            context.FillRectangle(new Rect(0,0,bounds.Width,bounds.Height), brush);
        }
        finally { context.Restore(); }
    }
}
