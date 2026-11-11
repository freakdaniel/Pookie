using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Artwork, tint and the edge use one contour. The tags themselves stay outside
// that contour, on the page background; no opaque patch covers the artwork.
internal sealed class MixHeroShape(FrameworkElement tags)
{
    internal bool Enabled { get; set; }
    private Rect previousBounds, previousTags;
    private PathGeometry? geometry;
    internal double NotchBottom { get; private set; }
    internal double NotchLeft { get; private set; }

    internal PathGeometry Get(Rect bounds)
    {
        var slot = tags.IsVisible ? tags.Bounds : default;
        if (geometry != null && bounds == previousBounds && slot == previousTags) return geometry;
        previousBounds = bounds; previousTags = slot;
        const double radius = 20, join = 14;
        var left = bounds.X; var right = bounds.Right; var top = bounds.Y; var bottom = bounds.Bottom;
        NotchLeft = Math.Clamp(slot.X - 16, left + radius * 2, right - radius * 2);
        NotchBottom = Math.Clamp(slot.Bottom + 12, top + join * 2, bottom - radius * 2);
        var shape = new PathGeometry();
        shape.MoveTo(left + radius, top);
        if (slot.Width > 0 && slot.Height > 0)
        {
            shape.LineTo(NotchLeft - join, top);
            shape.QuadTo(NotchLeft, top, NotchLeft, top + join);
            shape.LineTo(NotchLeft, NotchBottom - join);
            shape.QuadTo(NotchLeft, NotchBottom, NotchLeft + join, NotchBottom);
            shape.LineTo(right - radius, NotchBottom);
            shape.QuadTo(right, NotchBottom, right, NotchBottom + radius);
        }
        else
        {
            shape.LineTo(right - radius, top);
            shape.QuadTo(right, top, right, top + radius);
        }
        shape.LineTo(right, bottom - radius); shape.QuadTo(right, bottom, right - radius, bottom);
        shape.LineTo(left + radius, bottom); shape.QuadTo(left, bottom, left, bottom - radius);
        shape.LineTo(left, top + radius); shape.QuadTo(left, top, left + radius, top); shape.Close();
        shape.Freeze();
        return geometry = shape;
    }
}

internal sealed class MixHeroArtwork : Control
{
    private static readonly MewProperty<IImageSource?> SourceProperty = MewProperty<IImageSource?>.Register<MixHeroArtwork>(
        "Source", null, MewPropertyOptions.AffectsRender);
    private static readonly MewProperty<double> FadeProperty = MewProperty<double>.Register<MixHeroArtwork>(
        "Fade", 0, MewPropertyOptions.AffectsRender);
    private readonly MixHeroShape shape;
    private IImage? image;
    private IGraphicsFactory? factory;

    internal MixHeroArtwork(MixHeroShape shape, Image source)
    {
        this.shape = shape;
        SetBinding(SourceProperty, source, Image.SourceProperty);
        SetBinding(FadeProperty, source, UIElement.OpacityProperty);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == SourceProperty) ClearImage();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var source = GetValue(SourceProperty);
        var opacity = GetValue(FadeProperty);
        if (!shape.Enabled || source == null || opacity <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var graphics = Application.Current.GraphicsFactory;
        if (image == null || factory != graphics)
        {
            ClearImage(); factory = graphics; image = source.CreateImage(graphics);
        }
        var scale = Math.Max(Bounds.Width / image.PixelWidth, Bounds.Height / image.PixelHeight);
        var width = Bounds.Width / scale; var height = Bounds.Height / scale;
        var crop = new Rect((image.PixelWidth - width) / 2, (image.PixelHeight - height) / 2, width, height);
        context.FillPath(shape.Get(Bounds), new ImageBrush(image, crop, Bounds, TileMode.None, opacity));
    }

    private void ClearImage() { image?.Dispose(); image = null; factory = null; }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null) ClearImage(); }
    protected override void OnDispose() { ClearImage(); base.OnDispose(); }
}

internal sealed class MixHeroSurface(MixHeroShape shape, Color color, bool edge = false) : Control
{
    protected override void OnRender(IGraphicsContext context)
    {
        var path = shape.Get(Bounds);
        context.FillPath(path, color);
        if (edge)
            context.DrawPath(path, new Pen(new LinearGradientBrush(new Point(Bounds.X, Bounds.Y), new Point(Bounds.X, Bounds.Bottom),
                [new(0, Color.FromArgb(36, 255, 255, 255)), new(1, Color.FromArgb(6, 255, 255, 255))]), 1));
    }
}
