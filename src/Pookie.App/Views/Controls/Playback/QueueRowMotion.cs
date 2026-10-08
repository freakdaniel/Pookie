using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A queue edit has one timeline and one repaint request. Every retained row uses
// the same progress, including rows realized in a later layout pass.
internal sealed class QueueRowAnimation(Action redraw) : IDisposable
{
    private readonly HashSet<QueueRowMotion> rows = [];
    private readonly AnimationClock clock = new(TimeSpan.FromMilliseconds(460), Easing.Linear);
    internal double Progress { get; private set; }
    internal bool Contains(QueueRowMotion row) => rows.Contains(row);

    internal void Begin()
    {
        clock.Stop();
        foreach (var row in rows) row.FinishMotion();
        rows.Clear(); Progress = 0;
    }

    internal void Join(QueueRowMotion row)
    {
        if (!rows.Add(row)) return;
        if (clock.IsRunning) { row.Advance(Progress); return; }
        Progress = 0;
        clock.TickCallback = amount =>
        {
            Progress = amount;
            foreach (var moving in rows) moving.Advance(amount);
            redraw();
        };
        clock.CompletedCallback = () => rows.Clear();
        clock.Start();
    }

    internal void Remove(QueueRowMotion row)
    {
        rows.Remove(row);
        if (rows.Count == 0) { clock.Stop(); Progress = 0; }
    }

    internal static double Ease(double amount) => amount * amount * (3 - 2 * amount);
    public void Dispose() => Begin();
}

// Layout changes once; subsequent frames only translate the retained contents.
internal sealed class QueueRowMotion : ContentControl
{
    private readonly QueueRowAnimation animation;
    private double offset, from;
    private double? previousY;
    private bool entering;
    internal double VisualY => Bounds.Y + offset;
    internal bool Animating => animation.Contains(this);
    internal string? RetainedKey { get; set; }

    internal QueueRowMotion(QueueRowAnimation animation)
    {
        this.animation = animation;
        Background = Color.Transparent; BorderThickness = 0; Padding = new Thickness(0);
        SkipViewportCull = true;
    }

    internal void Retarget(double? paintedY, bool entering = false)
    {
        animation.Remove(this);
        offset = from = 0; previousY = paintedY;
        this.entering = paintedY == null && entering;
        Opacity = this.entering ? 0 : 1;
        if (this.entering) animation.Join(this);
    }

    internal void Advance(double amount)
    {
        // Smoothstep starts and ends at zero velocity. The old curve started
        // with a steep velocity, exaggerating even a single delayed first frame.
        offset = from * (1 - QueueRowAnimation.Ease(amount));
        if (entering) Opacity = QueueRowAnimation.Ease(Math.Clamp((amount - .18) / .82, 0, 1));
    }

    internal void FinishMotion() { offset = from = 0; previousY = null; entering = false; Opacity = 1; }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (previousY is { } oldY)
        {
            previousY = null;
            from = oldY - Bounds.Y;
            if (Math.Abs(from) > .5 && Math.Abs(from) < 800)
            {
                animation.Join(this);
                Advance(animation.Progress);
            }
            else from = offset = 0;
        }
        context.Save(); context.Translate(0, offset);
        base.RenderSubtree(context); context.Restore();
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled || Opacity < .05) return null;
        return Content is UIElement child ? child.HitTest(new Point(point.X, point.Y - offset)) : null;
    }

    protected override void OnDispose() { animation.Remove(this); base.OnDispose(); }
}
