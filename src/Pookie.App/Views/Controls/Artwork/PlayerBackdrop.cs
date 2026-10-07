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
    private PlayerGradient rasterPalette;
    private ImageSource? targetSource;
    private IImage? targetImage;
    private PlayerPalette targetPalette;
    private double blendAmount;
    private PlayerGradient from = PlayerGradient.FromPalette(PlayerPalette.Neutral);
    internal PlayerGradient CurrentGradient { get; private set; } = PlayerGradient.FromPalette(PlayerPalette.Neutral);
    internal PlayerPalette Current => CurrentGradient.ToPalette();
    internal PlayerPalette Target { get; private set; } = PlayerPalette.Neutral;
    internal bool Running => clock.IsRunning;
    internal event Action? PaletteChanged;

    internal PlayerBackdrop()
    {
        IsHitTestVisible = false;
        clock = new AnimationClock(TimeSpan.FromMilliseconds(950), Easing.CubicBezier(.42, 0, .58, 1))
        {
            TickCallback = amount => { blendAmount = amount; CurrentGradient = from.Lerp(PlayerGradient.FromPalette(Target), amount); RefreshPalette(); },
            CompletedCallback = () => { CurrentGradient = PlayerGradient.FromPalette(Target); RefreshPalette(); }
        };
    }

    internal void SetPalette(PlayerPalette palette)
    {
        if (Target == palette) return;
        clock.Stop();
        from = CurrentGradient;
        Target = palette;
        blendAmount = 0;
        clock.Start();
    }

    internal void Reset()
    {
        clock.Stop();
        Target = PlayerPalette.Neutral;
        CurrentGradient = from = PlayerGradient.FromPalette(Target);
        ClearRaster();
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
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * context.DpiScale));
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        var basePalette = Running ? from : CurrentGradient;
        if (rasterImage == null || rasterImage.PixelWidth != width || rasterPalette != basePalette || rasterFactory != factory)
        {
            ClearRaster();
            rasterSource = ImageSource.FromBgraPixels(width, RasterRows, CreateRaster(basePalette, width), hasAlpha: false);
            rasterImage = rasterSource.CreateImage(factory);
            rasterPalette = basePalette;
            rasterFactory = factory;
        }
        DrawRaster(context, rasterImage, width);
        if (!Running || blendAmount <= 0) return;
        // Blend two retained textures instead of generating/uploading a full-width
        // dithered texture on every animation tick. The grain stays stationary.
        if (targetImage == null || targetPalette != Target)
        {
            targetImage?.Dispose(); targetSource?.Dispose();
            targetSource = ImageSource.FromBgraPixels(width, RasterRows, CreateRaster(Target, width), hasAlpha: false);
            targetImage = targetSource.CreateImage(factory);
            targetPalette = Target;
        }
        DrawRaster(context, targetImage, width, blendAmount);
    }

    private void DrawRaster(IGraphicsContext context, IImage image, int width, double opacity = 1) =>
        context.FillRectangle(Bounds, new ImageBrush(image, new Rect(0, 0, width, RasterRows),
            new Rect(Bounds.X, Bounds.Y, Bounds.Width, RasterRows / context.DpiScale), TileMode.TileY, opacity));

    internal static byte[] CreateRaster(PlayerPalette palette, int width) => CreateRaster(PlayerGradient.FromPalette(palette), width);

    internal static byte[] CreateRaster(PlayerGradient gradient, int width)
    {
        var pixels = new byte[checked(width * RasterRows * 4)];
        for (var x = 0; x < width; x++)
        {
            var color = gradient.Sample((x + .5) / width);
            for (var y = 0; y < RasterRows; y++)
            {
                var noise = PlayerGradient.Noise(x, y);
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(color.Z + noise);
                pixels[offset + 1] = (byte)(color.Y + noise);
                pixels[offset + 2] = (byte)(color.X + noise);
                pixels[offset + 3] = 255;
            }
        }
        return pixels;
    }

    private void ClearRaster()
    {
        rasterImage?.Dispose(); rasterImage = null;
        rasterSource?.Dispose(); rasterSource = null;
        targetImage?.Dispose(); targetImage = null;
        targetSource?.Dispose(); targetSource = null;
        rasterFactory = null;
    }

    protected override void OnDispose()
    {
        clock.Stop();
        ClearRaster();
        base.OnDispose();
    }
}
