using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class ChatMessageMotion : ContentControl
{
    private readonly AnimationClock clock = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private double offset;
    internal ChatMessageMotion()
    {
        Background = Color.Transparent; BorderThickness = 0; Padding = new Thickness(0);
        clock.TickCallback = value => { offset = 16 * (1 - value); Opacity = value; InvalidateVisual(); };
        clock.CompletedCallback = Finish;
    }
    internal void Reveal() { Finish(); offset = 16; Opacity = 0; clock.Start(); }
    internal void Finish() { clock.Stop(); offset = 0; Opacity = 1; }
    protected override void RenderSubtree(IGraphicsContext context)
    { context.Save(); context.Translate(0, offset); base.RenderSubtree(context); context.Restore(); }
    protected override UIElement? OnHitTest(Point point) => Opacity < .1 ? null : base.OnHitTest(new Point(point.X, point.Y - offset));
    protected override void OnDispose() { clock.Stop(); base.OnDispose(); }
}
