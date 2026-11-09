using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Pookie.App;

internal sealed class PlayerBackdrop : Control
{
    private readonly AnimationClock clock;
    private const int RasterRows = 64;
    private WriteableBitmap? rasterSource;
    private IImage? rasterImage;
    private IGraphicsFactory? rasterFactory;
    private PlayerGradient rasterPalette;
    private double reveal = 1;
    private float[] grain = [];
    private Vector3[] columnColors = [];
    private PlayerGradient from = PlayerGradient.FromPalette(PlayerPalette.Neutral);
    internal PlayerGradient CurrentGradient { get; private set; } = PlayerGradient.FromPalette(PlayerPalette.Neutral);
    internal PlayerPalette Current => CurrentGradient.ToPalette();
    internal PlayerPalette Target { get; private set; } = PlayerPalette.Neutral;
    internal bool Running => clock.IsRunning;
    internal double BlendAmount => clock.Progress;
    internal bool ReuseRasterForResize { get; set; }
    internal event Action? PaletteChanged;
    internal double Reveal
    {
        get => reveal;
        set { if (reveal != value) { reveal = value; InvalidateVisual(); } }
    }

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

    protected override void OnRender(IGraphicsContext context) => RenderRegion(context, Bounds);

    // Reuse the exact player brush and coordinates for surfaces joined to the bar.
    internal void RenderRegion(IGraphicsContext context, Rect region)
    {
        if (reveal <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = Math.Max(context.DpiScale, GetDpi() / 96d);
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        var reuse = ReuseRasterForResize && rasterImage != null && rasterFactory == factory;
        var changed = !reuse && (rasterImage == null || rasterImage.PixelWidth != width || rasterFactory != factory);
        if (changed)
        {
            ClearRaster();
            rasterSource = new WriteableBitmap(width, RasterRows, clear: false, hasAlpha: false);
            rasterImage = ((IImageSource)rasterSource).CreateImage(factory);
            rasterFactory = factory;
            if (width > columnColors.Length)
            {
                var capacity = (width + 255) / 256 * 256;
                columnColors = new Vector3[capacity];
                grain = new float[capacity * RasterRows];
                for (var y = 0; y < RasterRows; y++)
                for (var x = 0; x < capacity; x++) grain[y * capacity + x] = (float)PlayerGradient.Noise(x, y);
            }
        }
        // Bake the actual intermediate colour into one opaque image. Its final
        // frame uses exactly the same drawing/compositing path as every earlier
        // frame, with no opacity-dependent endpoint or completion handoff.
        // Keep one texel per physical column so interpolation cannot erase
        // the dither. Only 64 rows are uploaded, then tiled vertically.
        if (!reuse && (changed || rasterPalette != CurrentGradient))
        {
            using var write = rasterSource!.LockForWrite();
            FillRaster(CurrentGradient, columnColors.AsSpan(0, width), write.PixelsBgra32, grain);
            rasterPalette = CurrentGradient;
        }
        context.FillRectangle(region, new ImageBrush(rasterImage!, new Rect(0, 0, rasterImage!.PixelWidth, RasterRows),
            new Rect(Bounds.X, Bounds.Y, Bounds.Width, RasterRows / scale), TileMode.TileY, opacity: reveal));
    }

    internal static byte[] CreateRaster(PlayerPalette palette, int width) => CreateRaster(PlayerGradient.FromPalette(palette), width);

    internal static byte[] CreateRaster(PlayerGradient gradient, int width)
    {
        var pixels = new byte[checked(width * RasterRows * 4)];
        FillRaster(gradient, new Vector3[width], pixels);
        return pixels;
    }

    private static void FillRaster(PlayerGradient gradient, Span<Vector3> colors, Span<byte> pixels, ReadOnlySpan<float> grain = default)
    {
        var width = colors.Length;
        var grainWidth = grain.Length / RasterRows;
        for (var x = 0; x < width; x++) colors[x] = gradient.Sample((x + .5) / width);
        var words = MemoryMarshal.Cast<byte, uint>(pixels);
        for (var y = 0; y < RasterRows; y++)
        {
            var row = words.Slice(y * width, width);
            var noiseRow = grain.Slice(y * grainWidth, grainWidth);
            for (var x = 0; x < width; x++)
            {
                var color = colors[x];
                var noise = noiseRow.IsEmpty ? PlayerGradient.Noise(x, y) : noiseRow[x];
                row[x] = 0xff000000u | (uint)(byte)(color.X + noise) << 16 |
                    (uint)(byte)(color.Y + noise) << 8 | (byte)(color.Z + noise);
            }
        }
    }

    private void ClearRaster()
    {
        rasterImage?.Dispose(); rasterImage = null;
        rasterSource?.Dispose(); rasterSource = null;
        rasterFactory = null;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) ClearRaster();
    }

    protected override void OnDispose()
    {
        clock.Stop();
        ClearRaster();
        base.OnDispose();
    }
}
