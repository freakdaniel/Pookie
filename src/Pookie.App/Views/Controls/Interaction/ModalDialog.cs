using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Only the small dialog is captured while it tips. The page stays live behind
// the same animated black scrim used by the chat video preview.
internal sealed class ModalDialog : Panel
{
    private readonly FrameworkElement content;
    private readonly Panel live;
    private readonly UIElement initialFocus;
    private readonly FrameworkElement? close;
    private readonly AnimationClock overlay = new(TimeSpan.FromMilliseconds(400), Easing.CubicBezier(.215, .61, .355, 1));
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(400), Easing.Linear);
    private readonly AnimationClock tilt = new(TimeSpan.FromMilliseconds(650), OpeningSpring);
    private Window? window;
    private UIElement? trigger;
    private bool triggerKeyboard;
    private DialogSnapshot? stamp;
    private double backgroundAmount, contentAmount, pose;
    private bool watching, preparing;
    internal bool IsOpen { get; private set; }
    internal bool Running => overlay.IsRunning || fade.IsRunning || tilt.IsRunning;
    internal double Reveal => contentAmount;
    internal double BackdropReveal => backgroundAmount;
    internal double Pose => pose;
    internal bool HasSnapshots => stamp != null;
    internal Rect SurfaceBounds => content.Bounds;
    internal void RenderGlassBackdrop(IGraphicsContext context) => OverlayScrim.Draw(context, Bounds, backgroundAmount);
    internal Action? DismissRequested;
    internal Action? Presented, Closed;
    internal Action? Opened;

    internal ModalDialog(FrameworkElement content, UIElement initialFocus, FrameworkElement? close = null)
    {
        this.content = content; this.initialFocus = initialFocus; this.close = close;
        live = new Grid().Columns("*").Rows("*").Children(content);
        Add(live); if (close != null) Add(close); IsVisible = IsHitTestVisible = false;
        MouseDown += args =>
        {
            if (!IsOpen || args.Handled) return;
            args.Handled = true;
            var point = args.GetPosition(window!);
            if (args.Button == MouseButton.Left && close?.Bounds.Contains(point) != true && !SurfaceBounds.Contains(Unproject(point))) Dismiss();
        };
        MouseWheel += args => { if (IsOpen) args.Handled = true; };
    }

    // k=150, c=25, m=1 gives roots -10 and -15. Use physical time so the
    // overdamped spring settles before its last frame instead of snapping there.
    private static double OpeningSpring(double t) =>
        (1 - 3 * Math.Exp(-6.5 * t) + 2 * Math.Exp(-9.75 * t)) / (1 - 3 * Math.Exp(-6.5) + 2 * Math.Exp(-9.75));

    internal void Show()
    {
        if (FindVisualRoot() is not Window source) return;
        if (IsOpen) return;
        window = source;
        if (source.FocusManager.FocusedElement is { } focused && !IsAncestorOf(focused))
        {
            // A menu command may open the dialog before the menu detaches.
            // Return to the menu's trigger, rather than its disappearing row.
            for (var ancestor = focused.Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor is InAppPopup { FocusReturnTarget: { } owner }) { focused = owner; ancestor = owner; }
            trigger = focused;
            triggerKeyboard = focused is Control control && KeyboardFocusFeedback.IsVisible(control);
        }
        IsVisible = IsHitTestVisible = IsOpen = true;
        if (close != null) close.Opacity = backgroundAmount;
        live.Opacity = pose == 1 && contentAmount == 1 ? 1 : 0;
        if (!watching) { source.FrameRendered += Prepare; watching = true; }
        Animate(1); KeyboardFocusFeedback.Restore(initialFocus, triggerKeyboard); InvalidateMeasure();
    }

    internal void Close(bool immediate = false)
    {
        if (!IsOpen && !immediate) return;
        if (!immediate && stamp == null && contentAmount > 0 && content.Bounds.Width > 0) CaptureCard();
        IsOpen = IsHitTestVisible = false;
        if (IsFocusWithin) window?.FocusManager.ClearFocus();
        // The exit image must be ready before hiding the live card.
        if (immediate || stamp != null) live.Opacity = 0;
        if (immediate)
        {
            overlay.Stop(); fade.Stop(); tilt.Stop();
            backgroundAmount = contentAmount = pose = 0; Detach(restoreFocus: false);
        }
        else Animate(0);
    }

    private void Dismiss() { if (DismissRequested != null) DismissRequested(); else Close(); }
    private void Animate(double target)
    {
        Start(overlay, backgroundAmount, target, 400, Easing.CubicBezier(.215, .61, .355, 1), value => backgroundAmount = value);
        Start(fade, contentAmount, target, target == 1 ? 400 : 200,
            target == 1 ? Easing.CubicBezier(.215, .61, .355, 1) : Easing.CubicBezier(.33, .17, .67, .5), value => contentAmount = value);
        Start(tilt, pose, target, target == 1 ? 650 : 200,
            target == 1 ? OpeningSpring : Easing.CubicBezier(.33, .17, .67, .5), value => pose = value);
    }
    private void Start(AnimationClock clock, double from, double target, double milliseconds, Func<double, double> ease, Action<double> set)
    {
        clock.Stop(); clock.EasingFunction = ease;
        clock.Duration = TimeSpan.FromMilliseconds(milliseconds * Math.Max(.15, Math.Abs(target - from)));
        clock.TickCallback = t => { set(from + (target - from) * t); if (close != null) close.Opacity = backgroundAmount; InvalidateVisual(); };
        clock.CompletedCallback = () =>
        {
            set(target);
            if (IsOpen && target == 1 && ReferenceEquals(clock, fade)) Presented?.Invoke();
            if (IsOpen && pose == 1 && contentAmount == 1) { live.Opacity = 1; stamp?.Dispose(); stamp = null; Opened?.Invoke(); }
            if (!IsOpen && backgroundAmount == 0 && contentAmount == 0 && pose == 0) Detach(restoreFocus: true);
            InvalidateVisual();
        };
        clock.Start();
    }

    // Call before page-level shortcuts. Let other keys reach the focused control.
    internal bool HandleKey(KeyEventArgs args)
    {
        if (!IsOpen || args.Handled) return false;
        if (args.Key == Key.Escape) { args.Handled = true; Dismiss(); }
        else if (args.Key == Key.Tab)
        {
            var stops = FocusStops();
            if (stops.Count > 0)
            {
                var index = stops.IndexOf(window!.FocusManager.FocusedElement!);
                var direction = args.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                stops[(index + direction + stops.Count) % stops.Count].Focus();
            }
            args.Handled = true;
        }
        return true;
    }
    private List<UIElement> FocusStops()
    {
        var result = new List<UIElement>();
        VisualTree.Visit(this, element =>
        {
            if (element is UIElement { Focusable: true, IsTabStop: true, IsVisible: true, IsEffectivelyEnabled: true } stop)
            {
                for (var parent = stop.Parent; parent != this; parent = parent?.Parent)
                    if (parent == null || parent is UIElement { IsVisible: false } or UIElement { IsHitTestVisible: false }) return;
                result.Add(stop);
            }
        });
        return result;
    }

    private void Prepare()
    {
        if (preparing || !IsVisible || window == null || Bounds.Width <= 0 || content.Bounds.Width <= 0) return;
        preparing = true;
        try
        {
            // Closing owns a frozen exit image. Recapturing after Close() can
            // replace it with already hidden/disposed native content.
            if (IsOpen && contentAmount > 0 && live.Opacity == 0)
            {
                CaptureCard();
                live.Opacity = 0;
                InvalidateVisual();
            }
            if (IsOpen && !IsFocusWithin) KeyboardFocusFeedback.Restore(initialFocus, triggerKeyboard);
        }
        finally { preparing = false; }
    }

    private void CaptureCard()
    {
        if (window == null) return;
        var bounds = content.Bounds.Inflate(16, 16);
        var dpi = Math.Min(window.DpiScale, 1400 / Math.Max(bounds.Width, bounds.Height));
        if (stamp == null || stamp.Bounds != bounds || Math.Abs(stamp.Scale - dpi) > .001)
        { stamp?.Dispose(); stamp = new(window.GraphicsFactory, bounds, dpi); }
        stamp.Capture(content.Render, Color.Transparent);
        stamp.Blur(12 * (1 - pose));
    }

    protected override Size MeasureContent(Size availableSize)
    {
        live.Measure(new(Math.Max(0, availableSize.Width - 32), Math.Max(0, availableSize.Height - 32)));
        close?.Measure(new(40, 40));
        return default;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Min(live.DesiredSize.Width, Math.Max(0, finalSize.Width - 32));
        var height = Math.Min(live.DesiredSize.Height, Math.Max(0, finalSize.Height - 32));
        live.Arrange(new(Bounds.X + (finalSize.Width - width) / 2, Bounds.Y + (finalSize.Height - height) / 2, width, height));
        close?.Arrange(new(Bounds.Right - 64, Bounds.Y + 12, 40, 40));
        return finalSize;
    }
    protected override void OnRender(IGraphicsContext context)
    {
        OverlayScrim.Draw(context, Bounds, backgroundAmount);
    }
    protected override void OnRenderOverlay(IGraphicsContext context)
    {
        if (live.Opacity != 0 || stamp == null || contentAmount <= 0) return;
        context.BeginOpacity(contentAmount);
        try
        {
            DrawPerspective(context, stamp.Image, stamp.ImageBounds, content.Bounds.Center, pose, Bounds);
        }
        finally { context.EndOpacity(); }
    }

    internal static void DrawPerspective(IGraphicsContext context, IImage image, Rect bounds, Point center, double pose, Rect viewport)
    {
        var scale = .8 + .2 * pose; var angle = -Math.PI / 9 * (1 - pose);
        var sine = Math.Sin(angle); var cosine = Math.Cos(angle);
        var dpi = context.DpiScale;
        double Y(double y) => center.Y + scale * (y - center.Y) * cosine / (1 - scale * (y - center.Y) * sine / 500);
        const double strip = 3;
        for (var top = bounds.Y; top < bounds.Bottom; top += strip)
        {
            var bottom = Math.Min(bounds.Bottom, top + strip);
            // Adjacent clips share an exact physical pixel boundary. Draw with
            // overscan inside them so sampling/snapping cannot expose hairline gaps.
            // The clips also prevent repeated alpha blending at the joins.
            var clipTop = Math.Round(Y(top) * dpi) / dpi;
            var clipBottom = Math.Round(Y(bottom) * dpi) / dpi;
            if (clipBottom <= clipTop) continue;
            var depth = 1 - scale * ((top + bottom) / 2 - center.Y) * sine / 500;
            var padding = 2 * depth * depth / (dpi * scale * cosine);
            var sampleTop = Math.Max(bounds.Y, top - padding);
            var sampleBottom = Math.Min(bounds.Bottom, bottom + padding);
            var left = center.X + scale * (bounds.X - center.X) / depth;
            var width = scale * bounds.Width / depth;
            context.Save();
            try
            {
                context.SetClip(new Rect(viewport.X, clipTop, viewport.Width, clipBottom - clipTop));
                context.DrawImage(image, new(left, Y(sampleTop), width, Y(sampleBottom) - Y(sampleTop)),
                    new(0, (sampleTop - bounds.Y) / bounds.Height * image.PixelHeight, image.PixelWidth, (sampleBottom - sampleTop) / bounds.Height * image.PixelHeight));
            }
            finally { context.Restore(); }
        }
    }
    private Point Unproject(Point point)
    {
        var scale = .8 + .2 * pose; var angle = -Math.PI / 9 * (1 - pose); var center = content.Bounds.Center;
        var dy = point.Y - center.Y;
        var y = dy / (scale * (Math.Cos(angle) + dy * Math.Sin(angle) / 500));
        var x = (point.X - center.X) * (1 - scale * y * Math.Sin(angle) / 500) / scale;
        return new(center.X + x, center.Y + y);
    }
    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsOpen || !Bounds.Contains(point)) return null;
        if (close?.HitTest(point) is { } hit) return hit;
        return content.HitTest(Unproject(point)) ?? this;
    }
    private void Detach(bool restoreFocus)
    {
        IsVisible = IsHitTestVisible = false;
        if (watching && window != null) { window.FrameRendered -= Prepare; watching = false; }
        stamp?.Dispose(); stamp = null;
        Closed?.Invoke();
        if (restoreFocus && window is { IsActive: true } && window.FocusManager.FocusedElement == null &&
            trigger is { IsEffectivelyEnabled: true, IsVisible: true } && trigger.FindVisualRoot() == window)
        {
            var visible = true;
            for (var ancestor = trigger.Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor is UIElement { IsVisible: false }) { visible = false; break; }
            if (visible) KeyboardFocusFeedback.Restore(trigger, triggerKeyboard);
        }
        trigger = null;
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) Close(true);
    }
    protected override void OnDispose() { Close(true); DismissRequested = Presented = Closed = Opened = null; base.OnDispose(); }
}
