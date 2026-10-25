using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Home;
using System.Numerics;

namespace Pookie.App;

internal sealed class WaveBackdrop : Control
{
    private readonly AnimationClock clock = new(TimeSpan.FromSeconds(16), Easing.Linear) { RepeatCount = -1 };
    private readonly AnimationClock colorClock = new(TimeSpan.FromMilliseconds(900), Easing.CubicBezier(.42, 0, .58, 1));
    private PlayerGradient colors = PaletteFor(WaveMode.Balance), colorFrom, colorTarget = PaletteFor(WaveMode.Balance);
    private WriteableBitmap? bitmap;
    private IImage? image;
    private IGraphicsFactory? factory;
    private readonly Vector3[] samples = new Vector3[160 * 64];
    private byte[]? pixels;
    private bool rendering, ready, disposed;
    private long generation;
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
        if (disposed || FindVisualRoot() is Window { WindowState: WindowState.Minimized }) return;
        var graphics = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        var scale = Math.Max(context.DpiScale, GetDpi() / 96d);
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));
        if (image == null || factory != graphics || image.PixelWidth != width || image.PixelHeight != height)
        {
            generation++; ready = false;
            image?.Dispose(); bitmap?.Dispose();
            bitmap = new WriteableBitmap(width, height, clear: false, hasAlpha: false);
            image = ((IImageSource)bitmap).CreateImage(graphics); factory = graphics; dirty = true;
        }
        if (dirty && !rendering)
        {
            dirty = false; rendering = true;
            var revision = generation; var palette = colors; var position = phase;
            var output = pixels is { } reusable && reusable.Length == width*height*4 ? reusable : new byte[width*height*4]; pixels = null;
            var dispatcher = Application.Current.Dispatcher!;
            _ = Task.Run(() =>
            {
                FillSamples(samples,palette,position);
                FillPixels(output,width,height,samples);
                dispatcher.BeginInvoke(() =>
                {
                    rendering = false;
                    if (disposed) return;
                    if (revision == generation && bitmap != null)
                    {
                        using var write = bitmap.LockForWrite(); output.CopyTo(write.PixelsBgra32); ready = true; pixels = output;
                    }
                    InvalidateVisual();
                });
            });
        }
        if (ready) context.DrawImage(image!,Bounds);
        else context.FillRectangle(Bounds,Color.FromRgb(24,24,24));
    }
    private static void FillSamples(Span<Vector3> samples,PlayerGradient colors,double phase)
    {
        var palette = new[] { colors.Start,colors.Middle,colors.End };
        for(var y=0;y<64;y++)for(var x=0;x<160;x++)
        {
            double r=21,g=20,b=28;
            for(var i=0;i<3;i++)
            {
                var dx=x/160d-(.3+i*.2+Math.Sin(phase+i*2)*.1);
                var dy=y/64d-(.5+Math.Cos(phase+i*2)*.22);
                var glow=Math.Exp(-(dx*dx*12+dy*dy*3))*.55;
                r+=palette[i].X*glow;g+=palette[i].Y*glow;b+=palette[i].Z*glow;
            }
            var shade=1-.25*Math.Exp(-Math.Pow((x/160d-.5)*5,2));
            samples[y*160+x]=new((float)(r*shade),(float)(g*shade),(float)(b*shade));
        }
    }
    internal static void FillPixels(Span<byte> pixels, int width, int height, ReadOnlySpan<Vector3> samples)
    {
        for (var y = 0; y < height; y++)
        {
            var sy = Math.Clamp((y + .5) * 64 / height - .5, 0, 63);
            var top = (int)sy; var bottom = Math.Min(63, top + 1); var vertical = (float)(sy - top);
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp((x + .5) * 160 / width - .5, 0, 159);
                var left = (int)sx; var right = Math.Min(159, left + 1); var horizontal = (float)(sx - left);
                var color = Vector3.Lerp(Vector3.Lerp(samples[top * 160 + left], samples[top * 160 + right], horizontal),
                    Vector3.Lerp(samples[bottom * 160 + left], samples[bottom * 160 + right], horizontal), vertical);
                var noise = PlayerGradient.Noise(x, y); var offset = (y * width + x) * 4;
                pixels[offset] = (byte)Math.Clamp(color.Z + noise, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp(color.Y + noise, 0, 255);
                pixels[offset + 2] = (byte)Math.Clamp(color.X + noise, 0, 255); pixels[offset + 3] = 255;
            }
        }
    }
    protected override void OnDispose() { disposed = true; generation++; pixels = null; clock.Stop(); colorClock.Stop(); image?.Dispose(); bitmap?.Dispose(); base.OnDispose(); }
}
