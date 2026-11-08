using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// The cover's viewport stays fixed. Artwork and its glass move together.
internal sealed class ArtworkHoverZoom : ContentControl
{
    private const float HoverScale = 1.04f;
    private readonly TransformDecorator visual;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(190), Easing.CubicBezier(.2, 0, 0, 1));
    private float scale = 1, from = 1, target = 1;

    internal ArtworkHoverZoom(FrameworkElement artwork)
    {
        Padding = new Thickness(0);
        BorderThickness = 0;
        Background = Color.Transparent;
        Content = visual = new TransformDecorator { Child = artwork };
        motion.TickCallback = progress => SetScale(from + (target - from) * (float)progress);
        motion.CompletedCallback = () => SetScale(target);
    }

    internal void Follow(UIElement owner)
    {
        owner.MouseEnter += () => Retarget(HoverScale);
        owner.MouseLeave += () => Retarget(1);
    }

    private void Retarget(float value)
    {
        if (target == value) return;
        motion.Stop();
        from = scale;
        target = value;
        if (FindVisualRoot() is Window) motion.Start();
        else SetScale(target);
    }

    private void SetScale(float value)
    {
        scale = value;
        visual.Transform = Matrix3x2.CreateScale(value);
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot != null) return;
        motion.Stop();
        target = 1;
        SetScale(1);
    }

    protected override void OnDispose()
    {
        motion.Stop();
        base.OnDispose();
    }
}
