using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A filled glass capsule keeps its pigment in the centre. Only the curved rim
// transmits the refracted backdrop, with separate key-light and counter-light
// reflections. This is not a uniform opacity applied to the entire button.
internal sealed class TintedGlassRaster : IDisposable
{
    private readonly GlassRimRaster patch = new();
    internal int Rasterizations => patch.Rasterizations;
    private readonly BoxShadowRaster shadow = new(6, 2, Color.FromArgb(85, 0, 0, 0));

    internal void DrawShadow(IGraphicsContext context, Rect bounds, double radius) =>
        shadow.Draw(context, bounds, radius, rasterScale: context.DpiScale);

    internal void Draw(IGraphicsContext context, Rect bounds, double radius, Color color, double minimumScale = 0)
    {
        patch.Draw(context, bounds, new CornerRadius(radius), minimumScale, color);
    }

    public void Dispose() { patch.Dispose(); shadow.Dispose(); }
}
