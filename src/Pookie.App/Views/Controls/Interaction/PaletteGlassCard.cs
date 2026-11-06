using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class PaletteGlassCard : GlassCard
{
    private readonly PlayerBackdrop gradient = new();
    internal PlayerPalette Palette => gradient.Target;

    internal PaletteGlassCard() : base(PopupAppearance.Surface)
    {
        gradient.PaletteChanged += InvalidateVisual;
    }
    internal void SetPalette(PlayerPalette palette) => gradient.SetPalette(palette);
    internal void ResetPalette() => gradient.Reset();
    protected override void RenderGlassBackground(IGraphicsContext context)
    {
        if (gradient.Bounds != Bounds) gradient.Arrange(Bounds);
        context.Save();
        try
        {
            context.SetClipRoundedRect(Bounds, CornerRadius, CornerRadius);
            gradient.RenderRegion(context, Bounds);
        }
        finally { context.Restore(); }
    }
    protected override void OnDispose()
    {
        gradient.PaletteChanged -= InvalidateVisual; gradient.Dispose(); base.OnDispose();
    }
}
