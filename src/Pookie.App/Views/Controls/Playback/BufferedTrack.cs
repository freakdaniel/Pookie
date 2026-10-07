using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Behind the slider: identical rail geometry, without a second input surface.
internal sealed class BufferedTrack : Control
{
    private static readonly Color BufferedColor = Color.FromArgb(72, 255, 255, 255);
    private readonly AnimationClock animation;
    private double from, target;
    internal double EndFraction { get; private set; }
    internal bool Animating => animation.IsRunning;

    public BufferedTrack()
    {
        IsHitTestVisible = false;
        animation = new AnimationClock(TimeSpan.FromMilliseconds(280), Easing.CubicBezier(.2, 0, 0, 1))
        {
            TickCallback = value => { EndFraction = from + (target - from) * value; InvalidateVisual(); }
        };
    }

    public void SetBuffer(double start, double end, double duration)
    {
        var valid = double.IsFinite(start) && double.IsFinite(end) && double.IsFinite(duration) &&
            start >= 0 && end >= start && duration > 0;
        var nextEnd = valid ? Math.Clamp(end / duration, 0, 1) : 0;
        if (target == nextEnd) return;
        animation.Stop();
        from = EndFraction;
        target = nextEnd;
        animation.Start();
    }

    public void Reset()
    {
        animation.Stop();
        from = target = EndFraction = 0;
        InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize) => new(0, 12);

    protected override void OnRender(IGraphicsContext context)
    {
        var rail = new Rect(Bounds.X + 7, Bounds.Y + (Bounds.Height - 4) / 2, Math.Max(0, Bounds.Width - 14), 4);
        if (rail.Width <= 0) return;
        context.FillRoundedRectangle(rail, 2, 2, LoadingTrack.RailColor);
        if (EndFraction > 0)
            context.FillRoundedRectangle(new Rect(rail.X, rail.Y, rail.Width * EndFraction, rail.Height), 2, 2, BufferedColor);
    }

    protected override void OnDispose()
    {
        animation.Stop();
        base.OnDispose();
    }
}
