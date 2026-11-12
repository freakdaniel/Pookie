using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class WidgetHexField : ContentControl
{
    private readonly GlassSheenRaster sheen = new();
    internal CornerRadius Corners => new(20, 8, 8, 20);
    internal WidgetHexField(TextBox field)
    {
        Height = 40; Content = field;
        Background = GlassSheen.LightControlTint(TrackButtons.GlassSurface);
    }
    protected override void OnRender(IGraphicsContext context)
    {
        DrawBackgroundAndBorder(context, Bounds, Background, Color.Transparent, new Thickness(0), Corners);
        sheen.Draw(context, Bounds, Corners, IsFocusWithin ? .5 : .2, GetDpi() / 96d);
    }
    protected override void OnDispose() { sheen.Dispose(); base.OnDispose(); }
}
