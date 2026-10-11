using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Home;

namespace Pookie.App;

internal sealed class WaveBackdrop : Control
{
    private readonly AnimationClock clock = new(TimeSpan.FromSeconds(16), Easing.Linear) { RepeatCount = -1 };
    private readonly AnimationClock colorClock = new(TimeSpan.FromMilliseconds(900), Easing.CubicBezier(.42, 0, .58, 1));
    private PlayerGradient colors = PaletteFor(WaveMode.Balance), colorFrom, colorTarget = PaletteFor(WaveMode.Balance);
    private WriteableBitmap? bitmap;
    private IImage? image;
    private IGraphicsFactory? factory;
    private double phase, phaseFrom;
    private long frame;
    private bool dirty = true;
    private WaveMode mode;
    internal bool Running => clock.IsRunning;
    internal bool ChangingColor => colorClock.IsRunning;
    internal PlayerGradient Colors => colors;
    internal WaveBackdrop()
    {
        IsHitTestVisible = false;
        clock.TickCallback = value =>
        {
            var now = Environment.TickCount64;
            if (now - frame < 50) return;
            frame = now; phase = (phaseFrom + value * Math.PI * 2) % (Math.PI * 2); dirty = true; InvalidateVisual();
        };
        colorClock.TickCallback = value => { colors = colorFrom.Lerp(colorTarget, value); dirty = true; InvalidateVisual(); };
    }
    private static PlayerGradient PaletteFor(WaveMode mode) => PlayerGradient.FromPalette(mode switch {
        WaveMode.Favorites => new(Color.FromRgb(180, 45, 100), Color.FromRgb(100, 48, 170), Color.FromRgb(220, 90, 60)),
        WaveMode.Discoveries => new(Color.FromRgb(30, 140, 170), Color.FromRgb(90, 50, 210), Color.FromRgb(130, 40, 175)),
        _ => new PlayerPalette(Color.FromRgb(140, 45, 210), Color.FromRgb(200, 45, 110), Color.FromRgb(40, 115, 170)) });
    internal void SetActive(bool active, WaveMode next, bool visible = true)
    {
        if (mode != next)
        {
            colorClock.Stop(); mode = next; colorFrom = colors; colorTarget = PaletteFor(next);
            if (visible) colorClock.Start();
            else { colors = colorTarget; dirty = true; InvalidateVisual(); }
        }
        if (!visible && colorClock.IsRunning)
        { colorClock.Stop(); colors = colorTarget; dirty = true; InvalidateVisual(); }
        if (active && !clock.IsRunning) { phaseFrom = phase; clock.Start(); }
        else if (!active) clock.Stop();
    }
    protected override void OnRender(IGraphicsContext context)
    {
        var graphics = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        if (image == null || factory != graphics)
        {
            image?.Dispose(); bitmap?.Dispose();
            bitmap = new WriteableBitmap(160, 64, clear: false, hasAlpha: false);
            image = ((IImageSource)bitmap).CreateImage(graphics); factory = graphics; dirty = true;
        }
        if (dirty)
        {
            using var write = bitmap!.LockForWrite();
            var pixels = write.PixelsBgra32;
            var palette = new[] { colors.Start, colors.Middle, colors.End };
            for (var y = 0; y < 64; y++)
            for (var x = 0; x < 160; x++)
            {
                double r = 21, g = 20, b = 28;
                for (var i = 0; i < 3; i++)
                {
                    var dx = x / 160d - (.3 + i * .2 + Math.Sin(phase + i * 2) * .1);
                    var dy = y / 64d - (.5 + Math.Cos(phase + i * 2) * .22);
                    var glow = Math.Exp(-(dx * dx * 12 + dy * dy * 3)) * .55;
                    r += palette[i].X * glow; g += palette[i].Y * glow; b += palette[i].Z * glow;
                }
                // Keep the centre readable beneath the primary action.
                var shade = 1 - .25 * Math.Exp(-Math.Pow((x / 160d - .5) * 5, 2));
                var offset = (y * 160 + x) * 4;
                pixels[offset] = (byte)Math.Min(255, b * shade);
                pixels[offset + 1] = (byte)Math.Min(255, g * shade);
                pixels[offset + 2] = (byte)Math.Min(255, r * shade); pixels[offset + 3] = 255;
            }
            dirty = false;
        }
        context.FillRectangle(Bounds, new ImageBrush(image!, new Rect(0, 0, 160, 64), Bounds));
    }
    protected override void OnDispose() { clock.Stop(); colorClock.Stop(); image?.Dispose(); bitmap?.Dispose(); base.OnDispose(); }
}
