using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class LyricsWaveDots : Control
{
    private readonly AnimationClock wave = new(TimeSpan.FromMilliseconds(1200), Easing.Linear) { RepeatCount = -1 };
    private readonly AnimationClock amplitude = new(TimeSpan.FromMilliseconds(320), Easing.CubicBezier(.2, 0, 0, 1));
    private double phase, phaseFrom, amount, amountFrom, amountTo;
    private bool running;
    internal double Phase => phase;
    internal const double Diameter = 14;
    internal double WaveAmount => amount;
    internal double VerticalOffset(int index) => Math.Sin(phase * Math.PI * 2 - index * .8) * 7 * amount;
    public LyricsWaveDots()
    {
        IsHitTestVisible = false;
        wave.TickCallback = value => { phase = (phaseFrom + value) % 1; InvalidateVisual(); };
        amplitude.TickCallback = value => { amount = amountFrom + (amountTo - amountFrom) * value; InvalidateVisual(); };
    }
    internal void SetActive(bool active)
    {
        if (running == active) return;
        running = active;
        amplitude.Stop(); amountFrom = amount; amountTo = active ? 1 : 0;
        if (active) { phaseFrom = phase; wave.Start(); } else wave.Stop();
        amplitude.Start();
    }
    internal void Reset()
    {
        if (!wave.IsRunning && !amplitude.IsRunning && phase == 0 && amount == 0) return;
        wave.Stop(); amplitude.Stop(); running = false;
        phase = phaseFrom = amount = amountFrom = amountTo = 0;
        InvalidateVisual();
    }
    protected override void OnRender(IGraphicsContext context)
    {
        var center = Bounds.X + Bounds.Width / 2;
        for (var i = 0; i < 3; i++)
        {
            var y = Bounds.Y + Bounds.Height / 2 - VerticalOffset(i);
            context.FillEllipse(new Rect(center + (i - 1) * 24 - Diameter / 2, y - Diameter / 2, Diameter, Diameter), Color.White);
        }
    }
    protected override void OnDispose() { Reset(); base.OnDispose(); }
}
