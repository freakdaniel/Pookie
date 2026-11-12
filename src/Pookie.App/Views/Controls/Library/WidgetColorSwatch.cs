using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class WidgetColorSwatch : Control
{
    internal Color Color { get; private set; }
    internal bool Selected { get; private set; }
    internal WidgetColorSwatch() { Width = Height = 28; IsHitTestVisible = false; }
    internal void Set(Color color, bool selected) { Color = color; Selected = selected; InvalidateVisual(); }
    protected override void OnRender(IGraphicsContext context)
    {
        var disk = Bounds.Deflate(new Thickness(4));
        context.FillRoundedRectangle(disk, 10, 10, Color);
        context.DrawRoundedRectangle(disk, 10, 10, Aprillz.MewUI.Color.FromArgb(45, 255, 255, 255), 1);
        if (Selected) context.DrawRoundedRectangle(Bounds.Deflate(new Thickness(1)), 13, 13, Aprillz.MewUI.Color.White, 1.5);
    }
}
