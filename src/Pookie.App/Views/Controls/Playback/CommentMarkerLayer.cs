using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Timed avatars keep their real buttons for input. The framework owns their
// bitmap cache and invalidates it when a descendant changes; fading reuses it.
internal sealed class CommentMarkerLayer : Grid
{
    internal CommentMarkerLayer() { CacheMode = new BitmapCache(); Opacity = 0; }
    private readonly AnimationClock entrance = new(TimeSpan.FromMilliseconds(280), Easing.CubicBezier(.2, 0, 0, 1));
    private double reveal;
    internal double Reveal => reveal;

    internal void RevealMarkers()
    {
        if (reveal > 0 || entrance.IsRunning) return;
        entrance.TickCallback = t => { reveal = t; Opacity = t; };
        entrance.Start();
    }
    internal void ResetReveal() { entrance.Stop(); reveal = 0; Opacity = 0; }

    internal void RefreshSnapshot() { InvalidateVisual(); }
    internal void Interact() { InvalidateVisual(); }

    protected override UIElement? OnHitTest(Point point)
    {
        if (reveal < .1 || !IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled || !Bounds.Contains(point)) return null;
        for (var i = Children.Count - 1; i >= 0; i--)
            if (Children[i] is UIElement child && child.HitTest(point) is { } hit) return hit;
        // Empty parts of the avatar layer belong to the waveform underneath.
        return null;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null) { ResetReveal(); } }
    protected override void OnDispose() { entrance.Stop(); base.OnDispose(); }
}
