using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class PlayerBackdrop : Control
{
    private readonly AnimationClock clock;
    private const int RasterRows = 64;
    private ImageSource? rasterSource;
    private IImage? rasterImage;
    private IGraphicsFactory? rasterFactory;
    private PlayerPalette rasterPalette;
    private PlayerPalette from = PlayerPalette.Neutral;
    internal PlayerPalette Current { get; private set; } = PlayerPalette.Neutral;
    internal PlayerPalette Target { get; private set; } = PlayerPalette.Neutral;
    internal bool Running => clock.IsRunning;

    internal PlayerBackdrop()
    {
        IsHitTestVisible = false;
        clock = new AnimationClock(TimeSpan.FromMilliseconds(950), Easing.CubicBezier(.22, 0, .18, 1))
        {
            TickCallback = amount => { Current = from.Lerp(Target, amount); InvalidateVisual(); },
            CompletedCallback = () => { Current = Target; InvalidateVisual(); }
        };
    }

    internal void SetPalette(PlayerPalette palette)
    {
        if (Target == palette) return;
        clock.Stop();
        from = Current;
        Target = palette;
        clock.Start();
    }

    internal void Reset()
    {
        clock.Stop();
        Current = Target = from = PlayerPalette.Neutral;
        ClearRaster();
        InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * context.DpiScale));
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        if (rasterImage == null || rasterImage.PixelWidth != width || rasterPalette != Current || rasterFactory != factory)
        {
            ClearRaster();
            rasterSource = ImageSource.FromBgraPixels(width, RasterRows, CreateRaster(Current, width), hasAlpha: false);
            rasterImage = rasterSource.CreateImage(factory);
            rasterPalette = Current;
            rasterFactory = factory;
        }
        // Tile vertically at physical pixel resolution: no enlarged grain on HiDPI displays.
        context.FillRectangle(Bounds, new ImageBrush(rasterImage,
            new Rect(0, 0, width, RasterRows),
            new Rect(Bounds.X, Bounds.Y, Bounds.Width, RasterRows / context.DpiScale), TileMode.TileY));
    }

    internal static byte[] CreateRaster(PlayerPalette palette, int width)
    {
        var pixels = new byte[checked(width * RasterRows * 4)];
        for (var x = 0; x < width; x++)
        {
            var position = (x + .5) / width;
            var left = position < .38 ? palette.Start : palette.Middle;
            var right = position < .38 ? palette.Middle : palette.End;
            var amount = position < .38 ? position / .38 : (position - .38) / .62;
            // Smooth the slope around colour stops, retaining fractional channels until quantization.
            amount = amount * amount * (3 - 2 * amount);
            var r = left.R + (right.R - left.R) * amount;
            var g = left.G + (right.G - left.G) * amount;
            var b = left.B + (right.B - left.B) * amount;
            for (var y = 0; y < RasterRows; y++)
            {
                // Stable, unbiased stochastic rounding breaks up 8-bit bands by at most one level.
                // The same noise in RGB keeps the texture neutral; it never flickers between frames.
                var hash = unchecked((uint)x * 0x9e3779b9u + (uint)y * 0x85ebca6bu);
                hash ^= hash >> 16; hash = unchecked(hash * 0x7feb352du);
                hash ^= hash >> 15; hash = unchecked(hash * 0x846ca68bu); hash ^= hash >> 16;
                var noise = (hash & 0xffffff) / 16777216d;
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(b + noise);
                pixels[offset + 1] = (byte)(g + noise);
                pixels[offset + 2] = (byte)(r + noise);
                pixels[offset + 3] = 255;
            }
        }
        return pixels;
    }

    private void ClearRaster()
    {
        rasterImage?.Dispose(); rasterImage = null;
        rasterSource?.Dispose(); rasterSource = null;
        rasterFactory = null;
    }

    protected override void OnDispose()
    {
        clock.Stop();
        ClearRaster();
        base.OnDispose();
    }
}
