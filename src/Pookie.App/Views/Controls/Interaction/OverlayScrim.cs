using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal static class OverlayScrim
{
    internal const byte NormalAlpha = 200;
    internal static void Draw(IGraphicsContext context, Rect bounds, double reveal, double expansion = 0)
    {
        var alpha = (byte)Math.Round((NormalAlpha + (255 - NormalAlpha) * Math.Clamp(expansion, 0, 1)) * Math.Clamp(reveal, 0, 1));
        context.FillRectangle(bounds, Color.FromArgb(alpha, 0, 0, 0));
    }
}
