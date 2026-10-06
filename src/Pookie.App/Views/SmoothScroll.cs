using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class SmoothScroll : IDisposable
{
    private readonly ScrollViewer viewer;
    private readonly AnimationClock motion;
    private double target, last, velocity;
    private long timestamp;
    private bool running, applying;

    public SmoothScroll(ScrollViewer viewer)
    {
        this.viewer = viewer;
        motion = new AnimationClock(TimeSpan.FromSeconds(1), Easing.Linear)
        {
            RepeatCount = -1,
            TickCallback = _ => Advance()
        };
        viewer.MouseWheel += OnWheel;
        viewer.ScrollChanged += OnChanged;
        viewer.MouseDown += OnPointerDown;
    }

    internal static double Maximum(ScrollViewer viewer)
    {
        // A page can contain several independently composed item grids. Only its own bar
        // describes the page extent; descending into the content finds a grid's bar first.
        double maximum = 0;
        ((IVisualTreeHost)viewer).VisitChildren(element =>
        {
            if (element is not ScrollBar { Orientation: Orientation.Vertical } bar) return true;
            maximum = bar.IsVisible ? bar.Maximum : 0;
            return false;
        });
        return maximum;
    }

    internal bool Scroll(double delta)
    {
        var next = Math.Clamp((running ? target : viewer.VerticalOffset) + delta, 0, Maximum(viewer));
        if (Math.Abs(next - viewer.VerticalOffset) < .5) { Stop(); return false; }
        target = next;
        if (!running)
        {
            last = viewer.VerticalOffset; velocity = 0;
            timestamp = Stopwatch.GetTimestamp(); running = true; motion.Start();
        }
        return true;
    }

    private void Advance()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Min(.05, Stopwatch.GetElapsedTime(timestamp, now).TotalSeconds);
        timestamp = now;
        if (dt <= 0) return;
        target = Math.Clamp(target, 0, Maximum(viewer));
        // Exact critically damped spring. Retargeting preserves velocity; repeated wheel
        // events no longer restart a steep easing curve and jerk the first few frames.
        const double frequency = 22;
        var distance = viewer.VerticalOffset - target;
        var tangent = velocity + frequency * distance;
        var decay = Math.Exp(-frequency * dt);
        var position = target + (distance + tangent * dt) * decay;
        velocity = (velocity - frequency * tangent * dt) * decay;
        if (Math.Abs(position - target) < .2 && Math.Abs(velocity) < 3)
        { position = target; Stop(); }
        applying = true;
        try
        {
            viewer.SetScrollOffsets(viewer.HorizontalOffset, Math.Clamp(position, 0, Maximum(viewer)));
            last = viewer.VerticalOffset;
        }
        finally { applying = false; }
    }

    internal void CorrectLayoutOffset(double delta)
    {
        applying = true;
        try
        {
            var before = viewer.VerticalOffset;
            viewer.SetScrollOffsets(viewer.HorizontalOffset, before + delta);
            target += viewer.VerticalOffset - before;
            last = viewer.VerticalOffset;
        }
        finally { applying = false; }
    }

    private void OnWheel(MouseWheelEventArgs args)
    {
        if (!args.Handled && args.Delta.Y != 0 && Scroll(-args.Delta.Y * ThemeManager.DefaultMetrics.ScrollWheelStep))
            args.Handled = true;
    }

    private void OnPointerDown(MouseEventArgs _) => Stop();
    private void OnChanged()
    {
        // Scrollbar dragging, keyboard movement and history restoration stay immediate.
        if (running && !applying && Math.Abs(viewer.VerticalOffset - last) > 1) Stop();
    }

    internal void Stop() { motion.Stop(); running = false; velocity = 0; }
    public void Dispose()
    {
        Stop(); viewer.MouseWheel -= OnWheel; viewer.ScrollChanged -= OnChanged; viewer.MouseDown -= OnPointerDown;
    }
}
