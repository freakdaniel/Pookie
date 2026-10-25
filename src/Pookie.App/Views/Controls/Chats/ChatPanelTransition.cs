using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Keep the outgoing section alive while the incoming section slides into place.
internal sealed class ChatPanelTransition : Panel
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.4, 0, .2, 1));
    private FrameworkElement? current, previous;
    private double amount = 1, previousOpacity;
    private int direction = 1;
    internal bool Animating => motion.IsRunning;
    internal void Reset()
    {
        motion.Stop(); Clear(); current = previous = null; amount = 1; InvalidateMeasure();
    }
    internal void Show(FrameworkElement content, int slideDirection = 1)
    {
        if (ReferenceEquals(content, current)) return;
        motion.Stop();
        if (previous != null) Remove(previous);
        previous = current; previousOpacity = previous?.Opacity ?? 1; current = content; direction = slideDirection >= 0 ? 1 : -1;
        Add(content); amount = previous == null ? 1 : 0; content.Opacity = amount;
        motion.TickCallback = t => { amount = t; content.Opacity = t; if (previous != null) previous.Opacity = previousOpacity * (1 - t); InvalidateArrange(); };
        motion.CompletedCallback = () => { amount = 1; content.Opacity = 1; if (previous != null) { Remove(previous); previous = null; } InvalidateMeasure(); };
        if (previous != null) motion.Start();
        InvalidateMeasure();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        current?.Measure(availableSize); previous?.Measure(availableSize);
        return current?.DesiredSize ?? Size.Empty;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        current?.Arrange(new Rect(Bounds.X + direction * 24 * (1 - amount), Bounds.Y, finalSize.Width, finalSize.Height));
        previous?.Arrange(new Rect(Bounds.X - direction * 24 * amount, Bounds.Y, finalSize.Width, Math.Max(finalSize.Height, previous.DesiredSize.Height)));
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    {
        context.Save(); try { context.SetClip(Bounds); base.RenderSubtree(context); } finally { context.Restore(); }
    }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
