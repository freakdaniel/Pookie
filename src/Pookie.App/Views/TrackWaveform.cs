using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed class TrackWaveform : Control
{
    private float[] samples = [];
    private float[] bars = [];
    private double progress;
    private double? hoverPosition;
    public event Action<double>? SeekRequested;
    public bool HasSamples => samples.Length > 0;
    public double Progress
    {
        get => progress;
        set { progress = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    public TrackWaveform() { Background = Color.Transparent; }
    public void SetSamples(float[] values) { samples = values; bars = []; InvalidateVisual(); }
    protected override Size MeasureContent(Size availableSize) => new(0, 68);
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        hoverPosition = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1);
        InvalidateVisual();
    }
    protected override void OnMouseLeave() { base.OnMouseLeave(); hoverPosition = null; InvalidateVisual(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButton.Left) return;
        SeekRequested?.Invoke(Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1));
        e.Handled = true;
    }
    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var baseline = bounds.Y + bounds.Height * .69;
        if (samples.Length == 0)
        {
            context.FillRectangle(new Rect(bounds.X, baseline, bounds.Width, 1), Color.FromRgb(65, 65, 65));
            return;
        }
        var count = Math.Max(1, (int)(bounds.Width / 3));
        if (bars.Length != count) bars = WaveformData.Resample(samples, count);
        for (var index = 0; index < count; index++)
        {
            var height = Math.Max(1, bars[index] * bounds.Height * .64);
            var x = bounds.X + index * bounds.Width / count;
            var fraction = (index + .5) / count;
            var color = fraction <= progress ? Color.FromRgb(235, 235, 235) :
                hoverPosition is { } hover && fraction <= hover ? Color.FromRgb(196, 196, 196) : Color.FromRgb(131, 131, 131);
            context.FillRectangle(new Rect(x, baseline - height, Math.Max(1, bounds.Width / count - 1), height), color);
            context.FillRectangle(new Rect(x, baseline + 2, Math.Max(1, bounds.Width / count - 1), height * .35), Color.FromArgb(80, color.R, color.G, color.B));
        }
    }
}
