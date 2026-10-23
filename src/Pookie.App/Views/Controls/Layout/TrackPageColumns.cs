using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Both columns are measured against this pass's viewport. Updating grid definitions
// from SizeChanged uses yesterday's desired width and causes a resize feedback loop.
internal sealed class TrackPageColumns(FrameworkElement main, FrameworkElement side, bool hero = false, params FrameworkElement[] background) : Panel
{
    private double sideWidth, height;
    internal double HeroArtworkMinWidth { get; init; } = 280;
    internal double HeroArtworkMaxWidth { get; init; } = 400;
    internal Image BackgroundArtwork => background.OfType<Image>().First();

    // Only the layers physically behind the controls, never the cover column,
    // waveform, text or the controls themselves (which would recurse).
    internal void RenderGlassBackground(IGraphicsContext context)
    {
        context.FillRectangle(Bounds, Color.FromRgb(43, 43, 43));
        foreach (var layer in background) layer.Render(context);
    }

    internal TrackPageColumns Initialize()
    {
        foreach (var layer in background) Add(layer);
        Add(main); Add(side);
        return this;
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var clip = context.GetClipBoundsLocal();
        foreach (var child in Children)
            if (clip == null || child.Bounds.IntersectsWith(clip.Value)) child.Render(context);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1200;
        var scale = GetDpi() / 96d;
        sideWidth = Math.Round(Math.Clamp(Math.Round(width * .27), hero ? HeroArtworkMinWidth : 280, hero ? HeroArtworkMaxWidth : 400) * scale) / scale;
        var innerWidth = Math.Max(0, width - 48);
        // Measure wrapped text at the same physical-pixel width it will have
        // after arrange. Fractional-DPI constraints otherwise keep rebuilding
        // layouts at two widths as comments move through the viewport.
        var mainWidth = Math.Max(0, Math.Round((innerWidth - sideWidth - 32) * scale) / scale);
        if (hero) side.Width = side.Height = sideWidth;
        side.Measure(new Size(sideWidth, hero ? sideWidth : availableSize.Height));
        main.Measure(new Size(mainWidth, hero ? sideWidth : availableSize.Height));
        height = (hero ? sideWidth : Math.Max(main.DesiredSize.Height, side.DesiredSize.Height)) + (hero ? 48 : 0);
        foreach (var layer in background) layer.Measure(new Size(width, height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var layer in background) layer.Arrange(Bounds);
        var y = Bounds.Y + (hero ? 24 : 0);
        var scale = GetDpi() / 96d;
        var mainWidth = Math.Max(0, Math.Round((finalSize.Width - 48 - sideWidth - 32) * scale) / scale);
        main.Arrange(new Rect(Bounds.X + 24, y, mainWidth, height - (hero ? 48 : 0)));
        side.Arrange(new Rect(Bounds.X + 24 + mainWidth + 32, y, sideWidth, height - (hero ? 48 : 0)));
        return finalSize;
    }
}
