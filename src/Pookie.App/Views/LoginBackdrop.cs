using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class LoginBackdropShade : FrameworkElement
{
    private static readonly RadialGradientBrush Vignette = new(new Point(.5, .5), new Point(.5, .5), .7, .7,
        [new GradientStop(0, Color.FromArgb(55, 18, 18, 18)),
         new GradientStop(.55, Color.FromArgb(15, 18, 18, 18)),
         new GradientStop(1, Color.FromArgb(140, 18, 18, 18))], units: GradientUnits.ObjectBoundingBox);

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);
    protected override void OnRender(IGraphicsContext context)
    {
        context.FillRectangle(Bounds, Color.FromArgb(175, 18, 18, 18));
        context.FillRectangle(Bounds, Vignette);
    }
}
