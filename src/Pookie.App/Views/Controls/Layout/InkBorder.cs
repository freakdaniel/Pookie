using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Expose animated ink strength as scene opacity; retained rendering owns the blend.
internal sealed class InkBorder : ContentControl
{
    internal static readonly MewProperty<double> StrengthProperty = MewProperty<double>.Register<InkBorder>("Strength", 1, MewPropertyOptions.AffectsRender);
    internal double Strength { get => GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    internal InkBorder() { Padding = new Thickness(0); BorderThickness = 0; BorderBrush = Color.Transparent; }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == StrengthProperty) Opacity = Math.Clamp(Strength, 0, 1);
    }
    protected override UIElement? OnHitTest(Point point) => Strength <= 0 ? null : base.OnHitTest(point);
}
