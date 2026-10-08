using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class PlayerBackdrop : Control
{
    private readonly AnimationClock clock;
    private const int RasterRows = 64;
    private const int MaximumRasterWidth = 512;
    private WriteableBitmap? rasterSource;
    private IImage? rasterImage;
    private IGraphicsFactory? rasterFactory;
    private PlayerGradient rasterPalette;
    private float[] grain = [];
    private PlayerGradient from = PlayerGradient.FromPalette(PlayerPalette.Neutral);
    internal PlayerGradient CurrentGradient { get; private set; } = PlayerGradient.FromPalette(PlayerPalette.Neutral);
    internal PlayerPalette Current => CurrentGradient.ToPalette();
    internal PlayerPalette Target { get; private set; } = PlayerPalette.Neutral;
    internal bool Running => clock.IsRunning;
    internal double BlendAmount => clock.Progress;
    internal event Action? PaletteChanged;

    internal PlayerBackdrop()
    {
        IsHitTestVisible = false;
        clock = new AnimationClock(TimeSpan.FromMilliseconds(950), Easing.CubicBezier(.42, 0, .58, 1))
        {
            TickCallback = amount => { CurrentGradient = from.Lerp(PlayerGradient.FromPalette(Target), amount); RefreshPalette(); }
        };
    }

    internal void SetPalette(PlayerPalette palette)
    {
        if (Target == palette) return;
        clock.Stop();
        from = CurrentGradient;
        Target = palette;
        clock.Start();
    }

    internal void Reset()
    {
        clock.Stop();
        Target = PlayerPalette.Neutral;
        CurrentGradient = from = PlayerGradient.FromPalette(Target);
        RefreshPalette();
    }

    private void RefreshPalette()
    {
        InvalidateVisual();
        PaletteChanged?.Invoke();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var width = Math.Clamp((int)Math.Ceiling(Bounds.Width * context.DpiScale), 1, MaximumRasterWidth);
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        var changed = rasterImage == null || rasterImage.PixelWidth != width || rasterFactory != factory;
        if (changed)
        {
            ClearRaster();
            rasterSource = new WriteableBitmap(width, RasterRows, clear: false, hasAlpha: false);
            rasterImage = ((IImageSource)rasterSource).CreateImage(factory);
            rasterFactory = factory;
            grain = new float[width * RasterRows];
            for (var y = 0; y < RasterRows; y++)
            for (var x = 0; x < width; x++) grain[y * width + x] = (float)PlayerGradient.Noise(x, y);
        }
        // Bake the actual intermediate colour into one opaque image. Its final
        // frame uses exactly the same drawing/compositing path as every earlier
        // frame, with no opacity-dependent endpoint or completion handoff.
        // A bounded horizontal raster avoids full-window pixel uploads.
        if (changed || rasterPalette != CurrentGradient)
        {
            using var write = rasterSource!.LockForWrite();
            FillRaster(CurrentGradient, width, write.PixelsBgra32, grain);
            rasterPalette = CurrentGradient;
        }
        context.FillRectangle(Bounds, new ImageBrush(rasterImage!, new Rect(0, 0, width, RasterRows),
            new Rect(Bounds.X, Bounds.Y, Bounds.Width, RasterRows / context.DpiScale), TileMode.TileY));
    }

    internal static byte[] CreateRaster(PlayerPalette palette, int width) => CreateRaster(PlayerGradient.FromPalette(palette), width);

    internal static byte[] CreateRaster(PlayerGradient gradient, int width)
    {
        var pixels = new byte[checked(width * RasterRows * 4)];
        FillRaster(gradient, width, pixels);
        return pixels;
    }

    private static void FillRaster(PlayerGradient gradient, int width, Span<byte> pixels, ReadOnlySpan<float> grain = default)
    {
        for (var x = 0; x < width; x++)
        {
            var color = gradient.Sample((x + .5) / width);
            for (var y = 0; y < RasterRows; y++)
            {
                var sample = y * width + x;
                double noise = grain.IsEmpty ? PlayerGradient.Noise(x, y) : grain[sample];
                var offset = sample * 4;
                pixels[offset] = (byte)(color.Z + noise);
                pixels[offset + 1] = (byte)(color.Y + noise);
                pixels[offset + 2] = (byte)(color.X + noise);
                pixels[offset + 3] = 255;
            }
        }
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
