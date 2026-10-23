using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Apply an animated ink strength without a viewport-sized opacity group.
internal sealed class InkBorder : ContentControl
{
    internal static readonly MewProperty<double> StrengthProperty = MewProperty<double>.Register<InkBorder>("Strength", 1, MewPropertyOptions.AffectsRender);
    internal double Strength { get => GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    internal InkBorder() { Padding = new Thickness(0); BorderThickness = 0; BorderBrush = Color.Transparent; }

    protected override void OnRender(IGraphicsContext context)
    {
        if (Strength <= 0) return;
        context.Save();
        try { context.GlobalAlpha *= (float)Strength; base.OnRender(context); }
        finally { context.Restore(); }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (Strength <= 0) return;
        context.Save();
        try { context.GlobalAlpha *= (float)Strength; base.RenderSubtree(context); }
        finally { context.Restore(); }
    }

    protected override UIElement? OnHitTest(Point point) => Strength <= 0 ? null : base.OnHitTest(point);
}
