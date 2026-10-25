using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class ChatComposerGlyph : Panel
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(150), Easing.CubicBezier(.25,.1,.25,1));
    private Image? current, previous;
    private string? name;
    private double amount = 1;
    internal ChatComposerGlyph() { Width = Height = 21; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center; }
    internal void Show(string icon)
    {
        if (name == icon) return;
        motion.Stop(); if (previous != null) Remove(previous);
        previous = current; current = Icons.View(icon,21,TrackButtons.OnPrimary); name = icon; Add(current);
        current.Width = current.Height = double.NaN;
        amount = previous == null || FindVisualRoot() is not Window ? 1 : 0;
        motion.TickCallback = t => { amount = t; InvalidateArrange(); };
        motion.CompletedCallback = () => { amount = 1; if (previous != null) { Remove(previous); previous = null; } InvalidateArrange(); };
        if (amount < 1) motion.Start(); else motion.CompletedCallback?.Invoke();
        InvalidateMeasure();
    }
    protected override Size MeasureContent(Size availableSize)
    { current?.Measure(new Size(21,21)); previous?.Measure(new Size(21,21)); return new Size(21,21); }
    protected override Size ArrangeOverride(Size finalSize)
    {
        void Place(Image? image, double scale, double opacity)
        {
            if (image == null) return;
            image.Opacity = opacity;
            image.Arrange(new Rect(Bounds.X + (finalSize.Width - 21*scale)/2,Bounds.Y + (finalSize.Height - 21*scale)/2,21*scale,21*scale));
        }
        Place(previous, 1 - .25*amount, 1 - amount); Place(current, .75 + .25*amount, amount);
        return finalSize;
    }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
