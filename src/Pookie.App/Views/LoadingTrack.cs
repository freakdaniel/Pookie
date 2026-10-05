using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Matches ThinSlider's four-DIP rail and seven-DIP inset, so readiness does not move the bar.
internal sealed class LoadingTrack : Control
{
    internal static readonly Color RailColor = Color.FromArgb(38, 255, 255, 255);
    private static readonly GradientStop[] Stops = [
        new(0, RailColor),
        new(0.5, Color.FromArgb(150, 190, 190, 190)),
        new(1, RailColor)];
    private readonly AnimationClock clock;
    internal double Phase { get; private set; }
    internal bool Running => clock.IsRunning;

    public LoadingTrack()
    {
        IsHitTestVisible = false;
        clock = new AnimationClock(TimeSpan.FromMilliseconds(1250), Easing.Linear)
        {
            RepeatCount = -1,
            TickCallback = value => { Phase = value; InvalidateVisual(); }
        };
    }

    public void SetLoading(bool loading)
    {
        if (loading && !clock.IsRunning) { Phase = 0; clock.Start(); }
        else if (!loading) clock.Stop();
        InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize) => new(0, 12);

    protected override void OnRender(IGraphicsContext context)
    {
        var rail = new Rect(Bounds.X + 7, Bounds.Y + (Bounds.Height - 4) / 2, Math.Max(0, Bounds.Width - 14), 4);
        if (rail.Width <= 0) return;
        var band = rail.Width * 0.55;
        var start = rail.X - band + Phase * (rail.Width + band);
        context.FillRoundedRectangle(rail, 2, 2,
            new LinearGradientBrush(new Point(start, rail.Y), new Point(start + band, rail.Y), Stops));
    }

    protected override void OnDispose()
    {
        clock.Stop();
        base.OnDispose();
    }
}
