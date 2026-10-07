using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The player background varies horizontally, so covering the edges with matching
// translucent pixels fades into the actual artwork palette rather than a flat color.
internal sealed class QueueEdgeFade : Control
{
    private readonly PlayerBackdrop backdrop;
    private ImageSource? source;
    private IImage? image;
    private PlayerGradient gradient;
    private int width;
    private double origin, span;
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
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width * context.DpiScale));
        if (image == null || gradient != backdrop.CurrentGradient || width != pixelWidth || origin != Bounds.X || span != backdrop.ActualWidth)
        {
            ClearRaster(); gradient = backdrop.CurrentGradient; width = pixelWidth; origin = Bounds.X; span = backdrop.ActualWidth;
            const int rows = RasterRows;
            var pixels = new byte[width * rows * 4];
            for (var x = 0; x < width; x++)
            {
                var position = Math.Clamp((origin - backdrop.Bounds.X + (x + .5) / context.DpiScale) / Math.Max(1, span), 0, 1);
                var color = gradient.Sample(position);
                for (var y = 0; y < rows; y++)
                {
                    var i = (y * width + x) * 4;
                    var noise = PlayerGradient.Noise((int)Math.Round((origin - backdrop.Bounds.X) * context.DpiScale) + x, y);
                    pixels[i] = (byte)(color.Z + noise); pixels[i + 1] = (byte)(color.Y + noise);
                    pixels[i + 2] = (byte)(color.X + noise); pixels[i + 3] = alpha[y];
                }
            }
            source = ImageSource.FromBgraPixels(width, rows, pixels);
            image = source.CreateImage(Application.Current.GraphicsFactory);
        }
        var height = Math.Min(FadeHeight, Bounds.Height / 2);
        context.DrawImage(image, new Rect(Bounds.X, Bounds.Y, Bounds.Width, height));
        context.Save();
        context.Translate(0, 2 * Bounds.Bottom - height); context.Scale(1, -1);
        context.DrawImage(image, new Rect(Bounds.X, Bounds.Bottom - height, Bounds.Width, height));
        context.Restore();
    }

    private void ClearRaster() { image?.Dispose(); image = null; source?.Dispose(); source = null; }
    protected override void OnDispose()
    {
        backdrop.PaletteChanged -= InvalidateVisual;
        ClearRaster(); base.OnDispose();
    }
}
