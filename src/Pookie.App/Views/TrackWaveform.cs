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
    private double progress;
    private double? hoverPosition;
    private bool playing;
    private long colorTimestamp;
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
        samples = values; bars = []; emphasis = [];
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
        for (var index = 0; index < emphasis.Length; index++)
        {
            var target = TargetEmphasis(index);
            emphasis[index] += (target - emphasis[index]) * blend;
            if (Math.Abs(target - emphasis[index]) < .002) emphasis[index] = target;
            else settled = false;
        }
        if (settled) colorMotion.Stop();
        InvalidateVisual();
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
            for (var index = 0; index < count; index++) emphasis[index] = TargetEmphasis(index);
        }
        var pitch = bounds.Width / count;
        for (var index = 0; index < count; index++)
        {
            var height = Math.Max(1, Math.Floor(bars[index] * upper));
            var reflection = Math.Max(1, Math.Floor(bars[index] * (bounds.Height - upper - 1)));
            var x = bounds.X + index * pitch;
            var color = IdleColor.Lerp(PlayedColor, emphasis[index]);
            context.FillRectangle(new Rect(x, baseline - height, Math.Max(1, pitch - 1), height), color);
            context.FillRectangle(new Rect(x, baseline + 1, Math.Max(1, pitch - 1), reflection), Color.FromArgb(80, color.R, color.G, color.B));
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { colorMotion.Stop(); hoverPosition = null; }
    }
    protected override void OnDispose() { colorMotion.Stop(); base.OnDispose(); }
}
