using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// One visual transform moves the inputs and their labels without changing layout.
internal sealed class ShareTimeFields : ContentControl
{
    internal static readonly Color ErrorSurface = Color.FromRgb(81, 41, 47);
    internal static readonly Color ErrorText = Color.FromRgb(255, 180, 171);
    private readonly TextBox[] fields;
    private readonly AnimationClock color = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly AnimationClock shake = new(TimeSpan.FromMilliseconds(380), Easing.Linear);
    internal bool Invalid { get; private set; }
    internal double Offset { get; private set; }
    internal bool Animating => color.IsRunning || shake.IsRunning;

    internal ShareTimeFields(Grid content, params TextBox[] fields)
    {
        this.fields = fields; Content = content;
        FontSize = 24;
        Background = Color.Transparent; BorderThickness = 0; Padding = new Thickness(0);
        shake.TickCallback = amount =>
        { Offset = 5 * Math.Sin(amount * Math.PI * 6) * Math.Pow(1 - amount, 2); InvalidateVisual(); };
        shake.CompletedCallback = () => { Offset = 0; InvalidateVisual(); };
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // TextBox has no text alignment option. Center the measured digits while
        // retaining enough trailing space for its native caret and selection.
        foreach (var field in fields)
        {
            var textWidth = MeasureEngineText(field.Text.AsSpan()).Width;
            var padding = new Thickness(Math.Max(8, (field.Width - textWidth) / 2), 8, 8, 8);
            if (field.Padding != padding) field.Padding = padding;
        }
        return base.ArrangeOverride(finalSize);
    }

    internal void SetInvalid(bool invalid, bool animate = true)
    {
        if (Invalid == invalid && animate) return;
        Invalid = invalid; color.Stop();
        var background = invalid ? ErrorSurface : TrackButtons.TonalSurface;
        var foreground = invalid ? ErrorText : TrackButtons.OnSurface;
        var fromBackground = fields[0].Background; var fromForeground = fields[0].Foreground;
        void Present(double amount)
        {
            foreach (var field in fields)
            { field.Background = fromBackground.Lerp(background, amount); field.Foreground = fromForeground.Lerp(foreground, amount); }
        }
        if (!animate || FindVisualRoot() is not Window) Present(1);
        else { color.TickCallback = Present; color.Start(); }
        shake.Stop(); Offset = 0; InvalidateVisual();
        if (invalid && animate && FindVisualRoot() is Window) shake.Start();
    }

    protected override bool TryGetChildTransform(out Matrix3x2 transform)
    { transform = Matrix3x2.CreateTranslation((float)Offset, 0); return !transform.IsIdentity; }
    protected override UIElement? OnHitTest(Point point) => base.OnHitTest(new Point(point.X - Offset, point.Y));
    protected override void OnDispose() { color.Stop(); shake.Stop(); base.OnDispose(); }
}
