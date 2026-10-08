using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class LyricsSkeleton : Control
{
    private readonly AnimationClock pulse = new(TimeSpan.FromMilliseconds(1500), Easing.Linear) { RepeatCount = -1 };
    private double amount;
    public LyricsSkeleton()
    {
        IsHitTestVisible = false;
        pulse.TickCallback = value => { amount = value; InvalidateVisual(); };
    }
    public void SetActive(bool active) { if (active) pulse.Start(); else pulse.Stop(); }
    protected override void OnRender(IGraphicsContext context)
    {
        var alpha = (byte)(18 + 12 * (.5 - .5 * Math.Cos(amount * Math.PI * 2)));
        var center = Bounds.Y + Bounds.Height * .4;
        for (var i = -2; i <= 2; i++)
        {
            var width = Bounds.Width * (i % 2 == 0 ? .72 : .9);
            context.FillRoundedRectangle(new Rect(Bounds.X + (Bounds.Width - width) / 2, center + i * 84, width, 30),
                10, 10, Color.FromArgb(alpha, 255, 255, 255));
        }
    }
    protected override void OnDispose() { pulse.Stop(); base.OnDispose(); }
}
