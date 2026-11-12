using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Standard perspective/blur transition. Static cards reuse one native-DPI
// capture while the pose changes; dynamic dialogs can refresh their content.
internal sealed class ModalDialog : Panel, IVisualTreeHost
{
    private readonly LiveGlassBackdrop? glass;
    private bool capturingBackdrop;
    internal bool HasGlassBackdrop => glass?.Ready == true;
    internal void CaptureLiveGlass(IGraphicsContext context) => glass?.CaptureLive(context);
    private readonly FrameworkElement content;
    private readonly Panel live;
    private readonly bool cacheTransition;
    private readonly Func<bool>? refreshTransition;
    private readonly double? transitionMaximumHeight;
    private long capturedBackdropVersion = -1;
    internal bool UsesCachedTransition => cacheTransition;
    internal int SnapshotCaptures { get; private set; }
    internal int SnapshotAllocations { get; private set; }
    internal long BackdropVersion => glass?.Version ?? 0;
    internal long OpticalPaints => glass?.OpticalPaints ?? 0;
    internal int OpticalAllocations => glass?.OpticalAllocations ?? 0;
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

    internal ModalDialog(FrameworkElement content, UIElement initialFocus, FrameworkElement? close = null,
        bool staticBackdrop = false, bool cacheTransition = false, Color? backdropTint = null,
        GlassCard? glassSurface = null, Func<bool>? refreshTransition = null,
        double? glassMaximumHeight = null, double glassVerticalOffset = 0, double? transitionMaximumHeight = null)
    {
        this.content = content; this.initialFocus = initialFocus; this.close = close; this.cacheTransition = cacheTransition;
        this.refreshTransition = refreshTransition;
        this.transitionMaximumHeight = transitionMaximumHeight;
        live = new Grid().Columns("*").Rows("*").Children(content);
        Add(live); if (close != null) Add(close); IsVisible = IsHitTestVisible = false;
        if ((glassSurface ?? content as GlassCard) is { } card)
        {
            Rect CaptureArea() => glassMaximumHeight is { } height
                ? new(Bounds.Center.X - card.Bounds.Width / 2, Bounds.Center.Y + glassVerticalOffset - height / 2,
                    card.Bounds.Width, height) : card.Bounds;
            glass = new LiveGlassBackdrop(this, RenderBehind, tint: backdropTint, captureArea: CaptureArea,
                staticBackdrop: staticBackdrop, resizing: card.ResizeInProgress);
            card.UseLiveBackdrop(glass);
        }
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

    // Build fonts, decoration rasters, surfaces and the blur pipeline before
    // the startup spinner begins. All graphics access remains on the UI thread.
    internal async Task WarmTransitionAsync(CancellationToken token)
    {
        if (!cacheTransition || IsVisible || FindVisualRoot() is not Window owner) return;
        var warmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void InFirstFrame()
        {
            owner.FrameRendered -= InFirstFrame;
            try { WarmTransition(owner.ClientSize); warmed.TrySetResult(); }
            catch (Exception error) { warmed.TrySetException(error); }
        }
        owner.FrameRendered += InFirstFrame;
        owner.InvalidateVisual();
        try { await warmed.Task.WaitAsync(token); }
        finally { owner.FrameRendered -= InFirstFrame; }
    }

    private void WarmTransition(Size viewport)
    {
        if (!cacheTransition || IsVisible || FindVisualRoot() is not Window owner) return;
        window = owner;
        var available = new Size(Math.Max(0, viewport.Width - 32), Math.Max(0, viewport.Height - 32));
        content.Measure(available);
        var width = Math.Min(content.DesiredSize.Width, available.Width);
        var height = Math.Min(content.DesiredSize.Height, available.Height);
        content.Arrange(new Rect((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height));
        CaptureCard();
    }

    internal void Show()
    {
        if (FindVisualRoot() is not Window source) return;
        if (IsOpen) return;
        window = source;
        glass?.Attach(source);
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
        if (!immediate && (stamp == null || cacheTransition) && contentAmount > 0 && content.Bounds.Width > 0) CaptureCard(force: true);
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
            if (IsOpen && pose == 1 && contentAmount == 1)
            {
                live.Opacity = 1;
                // Keep the prepared surface for closing; avoid a cold native
                // allocation just as the loader is about to become visible.
                if (!cacheTransition) { stamp?.Dispose(); stamp = null; }
                Opened?.Invoke();
            }
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
            if (IsOpen && !IsFocusWithin && !InAppPopup.HasFocusFor(this)) KeyboardFocusFeedback.Restore(initialFocus, triggerKeyboard);
        }
        finally { preparing = false; }
    }

    internal void InvalidateTransitionSnapshot() => capturedBackdropVersion = -1;

    private void CaptureCard(bool force = false)
    {
        if (window == null) return;
        // Match the live text's physical pixel grid. Downsampling a snapshot
        // or translating it by a fractional pixel shifts glyphs at the handoff.
        var dpi = window.DpiScale;
        var area = content.Bounds;
        if (transitionMaximumHeight is { } height)
            area = new Rect((window.ClientSize.Width - area.Width) / 2,
                (window.ClientSize.Height - height) / 2, area.Width, height);
        area = area.Inflate(16, 16);
        var left = Math.Floor(area.X * dpi) / dpi;
        var top = Math.Floor(area.Y * dpi) / dpi;
        var bounds = new Rect(left, top, Math.Ceiling(area.Right * dpi) / dpi - left,
            Math.Ceiling(area.Bottom * dpi) / dpi - top);
        var recapture = stamp == null || stamp.Bounds != bounds || Math.Abs(stamp.Scale - dpi) > .001;
        if (recapture) { stamp?.Dispose(); stamp = new(window.GraphicsFactory, bounds, dpi); SnapshotAllocations++; }
        if (force || recapture || !cacheTransition || refreshTransition?.Invoke() == true || capturedBackdropVersion != BackdropVersion)
        {
            stamp!.Capture(content.Render, Color.Transparent);
            capturedBackdropVersion = BackdropVersion;
            SnapshotCaptures++;
        }
        var blur = 12 * (1 - pose);
        // A cached transition only needs physical-pixel blur steps. Reusing
        // equal kernels avoids a fresh full-card filter for fractional changes.
        if (cacheTransition) blur = Math.Round(blur * dpi) / dpi;
        stamp!.Blur(blur);
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
        if (!capturingBackdrop) OverlayScrim.Draw(context, Bounds, backgroundAmount);
    }
    protected override void OnRenderOverlay(IGraphicsContext context)
    {
        if (capturingBackdrop || live.Opacity != 0 || stamp == null || contentAmount <= 0) return;
        context.BeginOpacity(contentAmount);
        try
        {
            DrawPerspective(context, stamp.Image, stamp.ImageBounds, content.Bounds.Center, pose, Bounds);
        }
        finally { context.EndOpacity(); }
    }

    internal static void DrawPerspective(IGraphicsContext context, IImage image, Rect bounds, Point center, double pose, Rect viewport)
    {
        if (pose >= 1)
        {
            context.DrawImage(image, bounds);
            return;
        }
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
    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (capturingBackdrop) return true;
        foreach (var child in Children)
            if (!visitor(child)) return false;
        return true;
    }

    private void RenderBehind(IGraphicsContext context, Element root)
    {
        // Replay the page without capturing this dialog into its own glass.
        capturingBackdrop = true;
        try { root.Render(context); }
        finally { capturingBackdrop = false; }
    }

    private void Detach(bool restoreFocus)
    {
        IsVisible = IsHitTestVisible = false;
        glass?.Attach(null);
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
    protected override void OnDispose() { Close(true); glass?.Dispose(); DismissRequested = Presented = Closed = Opened = null; base.OnDispose(); }
}
