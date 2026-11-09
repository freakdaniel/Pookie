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
    private long precisionTimestamp;
    private bool running, applying;
    private bool enabled = true;
    internal bool IsRunning => running;
    private bool precision;
    internal Action? UserScrolling;
    internal Action<double>? Advanced;
    internal Func<bool>? RestoringAnchor;
    internal bool Enabled
    {
        get => enabled;
        set { if (enabled == value) return; enabled = value; if (!value) Stop(); }
    }

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
        if (running && Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds > 250) Stop();
        return ScrollTo((running ? target : viewer.VerticalOffset) + delta);
    }

    internal bool ScrollWheel(double wheelDelta, bool startImmediately = false)
    {
        var delta = -wheelDelta * ThemeManager.DefaultMetrics.ScrollWheelStep;
        if (running && Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds > 250) Stop();
        var now = Stopwatch.GetTimestamp();
        var continuous = false;
        if (OperatingSystem.IsWindows())
        {
            if (Math.Abs(wheelDelta - Math.Round(wheelDelta)) > .00001) precisionTimestamp = now;
            continuous = precisionTimestamp != 0 && Stopwatch.GetElapsedTime(precisionTimestamp, now).TotalMilliseconds < 150;
        }
        // A reversed notch cancels the unconsumed distance and velocity from
        // the previous direction. Otherwise several notches are spent braking
        // toward an old destination before the page follows the new input.
        if (running && (delta * (target - viewer.VerticalOffset) < 0 || delta * velocity < 0)) Stop();
        precision = continuous;
        if (continuous) precisionTimestamp = now;
        UserScrolling?.Invoke();
        return ScrollTo((running ? target : viewer.VerticalOffset) + delta, startImmediately);
    }

    internal bool ScrollTo(double position, bool startImmediately = false)
    {
        // Do not retarget a spring which stopped receiving frames while the
        // window was inactive. Resume from the visible position and zero speed.
        if (running && Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds > 250) Stop();
        var next = Math.Clamp(position, 0, Maximum(viewer));
        if (Math.Abs(next - viewer.VerticalOffset) < .01) { Stop(); return false; }
        if (!Enabled) { Stop(); viewer.SetScrollOffsets(viewer.HorizontalOffset, next); return true; }
        target = next;
        if (!running)
        {
            last = viewer.VerticalOffset; velocity = 0;
            timestamp = Stopwatch.GetTimestamp(); running = true; motion.Start();
            // A resize may already have a frame queued. Give that frame a
            // small spring step as soon as the first wheel packet is routed.
            if (startImmediately) AdvancePosition(.012);
        }
        return true;
    }

    private void Advance()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(timestamp, now).TotalSeconds;
        if (elapsed > .25) { Stop(); return; }
        var dt = Math.Min(.05, elapsed);
        timestamp = now;
        if (dt <= 0) return;
        AdvancePosition(dt);
    }

    private void AdvancePosition(double dt)
    {
        target = Math.Clamp(target, 0, Maximum(viewer));
        // Exact critically damped spring. Retargeting preserves velocity; repeated wheel
        // events no longer restart a steep easing curve and jerk the first few frames.
        // Interpolate precision packets over frames without adding the long tail
        // used for mouse notches. Native touchpad inertia remains the input source.
        var frequency = precision ? 90 : 22;
        var distance = viewer.VerticalOffset - target;
        var tangent = velocity + frequency * distance;
        var decay = Math.Exp(-frequency * dt);
        var position = target + (distance + tangent * dt) * decay;
        velocity = (velocity - frequency * tangent * dt) * decay;
        if (Math.Abs(position - target) < .01 && Math.Abs(velocity) < 1)
        { position = target; Stop(); }
        applying = true;
        try
        {
            var before = viewer.VerticalOffset;
            viewer.SetScrollOffsets(viewer.HorizontalOffset, Math.Clamp(position, 0, Maximum(viewer)));
            last = viewer.VerticalOffset;
            Advanced?.Invoke(last - before);
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
        if (!args.Handled && args.Delta.Y != 0 && ScrollWheel(args.Delta.Y))
            args.Handled = true;
    }

    private void OnPointerDown(MouseEventArgs _) => Stop();
    private void OnChanged()
    {
        // Scrollbar dragging, keyboard movement and history restoration stay immediate.
        if (!running || applying || Math.Abs(viewer.VerticalOffset - last) <= 1) return;
        if (RestoringAnchor?.Invoke() == true)
        {
            // Virtualized history can reposition the same visible message while
            // prepending. Carry the destination with that coordinate change.
            target += viewer.VerticalOffset - last; last = viewer.VerticalOffset;
        }
        else Stop();
    }

    internal void Stop()
    { motion.Stop(); running = precision = false; velocity = 0; target = last = viewer.VerticalOffset; timestamp = precisionTimestamp = 0; }
    public void Dispose()
    {
        Stop(); viewer.MouseWheel -= OnWheel; viewer.ScrollChanged -= OnChanged; viewer.MouseDown -= OnPointerDown;
    }
}
