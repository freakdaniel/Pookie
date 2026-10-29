using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed class TrackWaveform : Control
{
    private static readonly Color IdleColor = Color.FromRgb(131, 131, 131);
    private static readonly Color PlayedColor = Color.FromRgb(235, 235, 235);
    private readonly AnimationClock colorMotion;
    private float[] samples = [], bars = [];
    private double[] emphasis = [];
    private byte[] renderedColors = [];
    private double progress;
    private double? hoverPosition;
    private bool playing;
    private bool onArtwork;
    internal bool OnArtwork
    {
        get => onArtwork;
        set { if (onArtwork == value) return; onArtwork = value; rasterDirty = true; InvalidateVisual(); }
    }
    private long colorTimestamp;
    private WriteableBitmap? raster;
    private IImage? image;
    private IGraphicsFactory? factory;
    private bool rasterDirty = true;
    private Size rasterSize;
    private double rasterScale;
    public event Action<double>? SeekRequested;
    public bool HasSamples => samples.Length > 0;
    public double Progress
    {
        get => progress;
        set
        {
            var next = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (progress == next) return;
            progress = next;
            AnimateColors();
        }
    }
    public bool IsPlaying
    {
        get => playing;
        set { if (playing == value) return; playing = value; AnimateColors(); }
    }

    public TrackWaveform()
    {
        Background = Color.Transparent;
        Cursor = CursorType.Hand;
        colorMotion = new AnimationClock(TimeSpan.FromSeconds(1), Easing.Linear)
        {
            RepeatCount = -1,
            TickCallback = _ => AdvanceColors()
        };
    }

    public void SetSamples(float[] values)
    {
        colorMotion.Stop();
        samples = values; bars = []; emphasis = []; renderedColors = [];
        rasterDirty = true;
        // A recycled row must not inherit the previous track's hover or colors.
        hoverPosition = null;
        InvalidateVisual();
    }

    private double TargetEmphasis(int index)
    {
        var active = playing || hoverPosition != null ? .14 : 0;
        var played = Math.Clamp(progress * bars.Length - index, 0, 1);
        var hover = hoverPosition is { } point && (index + .5) / bars.Length <= point ? .625 : 0;
        return Math.Max(active + (1 - active) * played, hover);
    }

    private void AnimateColors()
    {
        if (bars.Length > 0 && !colorMotion.IsRunning)
        {
            colorTimestamp = Stopwatch.GetTimestamp();
            colorMotion.Start();
        }
        InvalidateVisual();
    }

    private void AdvanceColors()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Min(.05, Stopwatch.GetElapsedTime(colorTimestamp, now).TotalSeconds);
        colorTimestamp = now;
        // Retarget from the displayed color without restarting the clock. Frequent
        // mouse moves and playback ticks preserve the continuity of the transition.
        var blend = 1 - Math.Exp(-dt / .065);
        var settled = true;
        var changed = false;
        for (var index = 0; index < emphasis.Length; index++)
        {
            var target = TargetEmphasis(index);
            var previous = BarColor(index).R;
            emphasis[index] += (target - emphasis[index]) * blend;
            if (Math.Abs(target - emphasis[index]) < .002) emphasis[index] = target;
            else settled = false;
            changed |= previous != BarColor(index).R;
        }
        if (settled) colorMotion.Stop();
        if (changed) InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize) => new(0, 68);
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        hoverPosition = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1);
        AnimateColors();
    }
    protected override void OnMouseLeave()
    {
        base.OnMouseLeave(); hoverPosition = null; AnimateColors();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButton.Left) return;
        var fraction = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1);
        Progress = fraction;
        SeekRequested?.Invoke(fraction);
        e.Handled = true;
    }
    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var upper = bounds.Height * .7;
        var baseline = bounds.Y + upper;
        if (samples.Length == 0)
        {
            context.FillRectangle(new Rect(bounds.X, baseline, bounds.Width, 1), Color.FromRgb(65, 65, 65));
            return;
        }
        var count = Math.Clamp((int)Math.Ceiling(bounds.Width / 3), 1, 20000);
        if (bars.Length != count)
        {
            bars = WaveformData.Resample(samples, count);
            emphasis = new double[count];
            renderedColors = new byte[count];
            for (var index = 0; index < count; index++) emphasis[index] = TargetEmphasis(index);
            rasterDirty = true;
        }
        var scale = GetDpi() / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        var graphics = Application.Current.GraphicsFactory;
        if (raster == null || raster.PixelWidth != width || raster.PixelHeight != pixelHeight || factory != graphics)
        {
            ReleaseRaster(); factory = graphics;
            raster = new WriteableBitmap(width, pixelHeight, clear: false);
            image = OperatingSystem.IsWindows()
                ? BitmapImage.CreatePremultipliedView(graphics, raster)
                : BitmapImage.Create(graphics, raster);
        }
        if (rasterSize != bounds.Size || rasterScale != scale) rasterDirty = true;
        rasterSize = bounds.Size; rasterScale = scale;
        var colorsChanged = rasterDirty;
        for (var index = 0; !colorsChanged && index < count; index++) colorsChanged = renderedColors[index] != BarColor(index).R;
        if (colorsChanged)
        {
            using var write = raster.LockForWrite();
            var pixels = write.PixelsBgra32;
            if (rasterDirty) pixels.Clear();
            DrawBars(pixels, width, pixelHeight, scale);
            rasterDirty = false;
        }
        context.DrawImage(image!, new Rect(bounds.X, bounds.Y, width / scale, pixelHeight / scale));
    }

    private void DrawBars(Span<byte> pixels, int width, int pixelHeight, double scale)
    {
        var upper = Bounds.Height * .7;
        var pitch = Bounds.Width / bars.Length;
        var barWidth = Math.Max(1, pitch - 1);
        var baseline = upper * scale;
        var count = bars.Length;
        for (var index = 0; index < count; index++)
        {
            var color = BarColor(index);
            if (!rasterDirty && renderedColors[index] == color.R) continue;
            renderedColors[index] = color.R;
            var height = Math.Max(1, Math.Floor(bars[index] * upper));
            var reflection = Math.Max(1, Math.Floor(bars[index] * (Bounds.Height - upper - 1)));
            var x = index * pitch * scale;
            FillBar(pixels, width, pixelHeight, x, baseline - height * scale, x + barWidth * scale, baseline, color);
            FillBar(pixels, width, pixelHeight, x, baseline + scale, x + barWidth * scale, baseline + (1 + reflection) * scale,
                Color.FromArgb(80, color.R, color.G, color.B));
        }
    }

    private Color BarColor(int index) => (onArtwork ? Color.FromRgb(188, 188, 188) : IdleColor).Lerp(PlayedColor, emphasis[index]);

    private static void FillBar(Span<byte> pixels, int width, int height, double left, double top, double right, double bottom, Color color)
    {
        for (var x = Math.Max(0, (int)Math.Floor(left)); x < Math.Min(width, (int)Math.Ceiling(right)); x++)
        {
            var coverage = Math.Min(x + 1, right) - Math.Max(x, left);
            for (var y = Math.Max(0, (int)Math.Floor(top)); y < Math.Min(height, (int)Math.Ceiling(bottom)); y++)
            {
                var alpha = (byte)Math.Round(color.A * coverage * (Math.Min(y + 1, bottom) - Math.Max(y, top)));
                var offset = (y * width + x) * 4;
                // Windows uploads premultiplied pixels. Convert only the bars
                // being painted instead of scanning the entire waveform again.
                if (OperatingSystem.IsWindows())
                {
                    pixels[offset] = (byte)((color.B * alpha + 127) / 255);
                    pixels[offset + 1] = (byte)((color.G * alpha + 127) / 255);
                    pixels[offset + 2] = (byte)((color.R * alpha + 127) / 255);
                }
                else
                {
                    pixels[offset] = color.B; pixels[offset + 1] = color.G; pixels[offset + 2] = color.R;
                }
                pixels[offset + 3] = alpha;
            }
        }
    }

    private void ReleaseRaster()
    {
        image?.Dispose(); image = null;
        raster?.Dispose(); raster = null;
        factory = null; rasterDirty = true;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { colorMotion.Stop(); hoverPosition = null; ReleaseRaster(); }
    }
    protected override void OnDispose() { colorMotion.Stop(); ReleaseRaster(); base.OnDispose(); }
}
