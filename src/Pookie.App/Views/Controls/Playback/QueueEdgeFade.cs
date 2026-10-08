using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Bake the same intermediate gradient as the backdrop while keeping mask alpha
// constant. Cross-fading translucent masks would pulse their combined alpha.
internal sealed class QueueEdgeFade : Control
{
    private readonly PlayerBackdrop backdrop;
    private WriteableBitmap? source;
    private IImage? image;
    private IGraphicsFactory? factory;
    private PlayerGradient gradient;
    private float[] grain = [];
    private int width;
    private double origin, span, dpi;
    internal const double FadeHeight = 28;
    private const int RasterRows = 32;
    private static readonly byte[] alpha = Enumerable.Range(0, RasterRows).Select(y =>
    {
        var fade = 1 - y / (RasterRows - 1d);
        return (byte)Math.Round(255 * fade * fade * (3 - 2 * fade));
    }).ToArray();
    internal PlayerPalette RenderedPalette => gradient.ToPalette();
    internal PlayerGradient RenderedGradient => gradient;

    internal QueueEdgeFade(PlayerBackdrop backdrop)
    {
        this.backdrop = backdrop;
        backdrop.PaletteChanged += InvalidateVisual;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width * context.DpiScale));
        var graphics = Application.Current.GraphicsFactory;
        var relativeX = Bounds.X - backdrop.Bounds.X;
        var geometryChanged = width != pixelWidth || origin != relativeX || span != backdrop.ActualWidth || dpi != context.DpiScale;
        if (image == null || width != pixelWidth || factory != graphics)
        {
            ClearRaster();
            source = new WriteableBitmap(pixelWidth, RasterRows, clear: false);
            image = ((IImageSource)source).CreateImage(graphics);
            factory = graphics;
            geometryChanged = true;
        }
        width = pixelWidth; origin = relativeX; span = backdrop.ActualWidth; dpi = context.DpiScale;
        if (geometryChanged)
        {
            grain = new float[width * RasterRows];
            for (var y = 0; y < RasterRows; y++)
            for (var x = 0; x < width; x++)
                grain[y * width + x] = (float)PlayerGradient.Noise((int)Math.Round(origin * dpi) + x, y);
        }
        if (geometryChanged || gradient != backdrop.CurrentGradient)
        {
            gradient = backdrop.CurrentGradient;
            using var write = source!.LockForWrite();
            var pixels = write.PixelsBgra32;
            for (var x = 0; x < width; x++)
            {
                var color = gradient.Sample((origin + (x + .5) / dpi) / Math.Max(1, span));
                for (var y = 0; y < RasterRows; y++)
                {
                    var sample = y * width + x;
                    double noise = grain[sample];
                    var i = sample * 4;
                    pixels[i] = (byte)(color.Z + noise); pixels[i + 1] = (byte)(color.Y + noise);
                    pixels[i + 2] = (byte)(color.X + noise); pixels[i + 3] = alpha[y];
                }
            }
        }
        var height = Math.Min(FadeHeight, Bounds.Height / 2);
        context.DrawImage(image!, new Rect(Bounds.X, Bounds.Y, Bounds.Width, height));
        context.Save();
        context.Translate(0, 2 * Bounds.Bottom - height); context.Scale(1, -1);
        context.DrawImage(image!, new Rect(Bounds.X, Bounds.Bottom - height, Bounds.Width, height));
        context.Restore();
    }

    private void ClearRaster() { image?.Dispose(); image = null; source?.Dispose(); source = null; }
    protected override void OnDispose()
    {
        backdrop.PaletteChanged -= InvalidateVisual;
        ClearRaster(); base.OnDispose();
    }
}
