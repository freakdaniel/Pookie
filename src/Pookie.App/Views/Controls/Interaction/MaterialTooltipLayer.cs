using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Material 3 plain tooltip: inverse surface, body-small, extra-small corners,
// 8/4 padding and an anchor-relative scale/fade. Paint never changes page layout.
internal sealed class MaterialTooltipLayer : Panel, IOverlayService
{
    internal static readonly Color Surface = Color.FromRgb(226, 226, 230);
    internal static readonly Color Ink = Color.FromRgb(47, 47, 51);
    internal const double Gap = 4, AvatarGap = 8, Radius = 4, MaximumWidth = 200;
    internal const int HoverDelayMs = 500;
    private readonly Window window;
    private readonly TextBlock label;
    internal readonly Border Card;
    private readonly DispatcherTimer delay = new(TimeSpan.FromMilliseconds(HoverDelayMs));
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(150), Easing.Linear);
    private readonly AnimationClock groupMotion = new(TimeSpan.FromMilliseconds(400), Easing.Spring(400, 29));
    private readonly List<(ScrollViewer Scroll, Action Handler)> scrolls = [];
    private FrameworkElement? owner, pending;
    private WeakReference<FrameworkElement>? dismissed;
    private bool keyboard, pendingKeyboard, observing, attached, disposed;
    private double amount;
    private Rect surfaceBounds;
    private Rect placedAnchor;
    private Size placedWindow;
    private Point origin;
    private Rect groupFrom;
    private double groupProgress = 1, placedGroupProgress;
    internal bool IsOpen { get; private set; }
    internal double Reveal => amount;
    internal Rect SurfaceBounds => surfaceBounds;
    internal Point TransformOrigin => origin;
    internal FrameworkElement? Owner => owner;
    internal string Text => label.Text;
    internal bool Running => delay.IsEnabled || motion.IsRunning || groupMotion.IsRunning;

    internal MaterialTooltipLayer(Window window)
    {
        this.window = window;
        label = new TextBlock().FontSize(12).FontWeight(FontWeight.Normal).Foreground(Ink).TextWrapping(TextWrapping.Wrap);
        Card = new Border().Background(Surface).BorderThickness(0).CornerRadius(Radius).ClipToBounds().Padding(8, 4)
            .MinWidth(40).MinHeight(24).MaxWidth(MaximumWidth).Child(label);
        Add(Card); Card.Opacity = 0; IsHitTestVisible = false; IsVisible = false;
        delay.Tick += ShowPending;
        window.OverlayLayer.RegisterService(this);
        window.Deactivated += Deactivated;
        window.PreviewKeyDown += PreviewKey;
        window.MouseDown += PointerDown;
        window.MouseWheel += Wheel;
    }

    internal void Request(FrameworkElement target, bool keyboard)
    {
        if (disposed || !Eligible(target) || dismissed?.TryGetTarget(out var blocked) == true && blocked == target) return;
        if (owner == target && attached)
        {
            CancelPending();
            this.keyboard = keyboard;
            var reopening = !IsOpen;
            IsOpen = true;
            Refresh(target);
            if (reopening) { Observe(); WatchScrolls(target); Animate(1); }
            return;
        }
        var group = TooltipHints.GroupFor(target);
        if (group != null && attached && owner != null && ReferenceEquals(group, TooltipHints.GroupFor(owner)))
        {
            RetargetGroup(target, keyboard);
            return;
        }
        var warm = attached;
        Hide();
        pending = target; pendingKeyboard = keyboard;
        Observe();
        if (group != null || keyboard && !attached) { ShowPending(); return; }
        delay.Interval = TimeSpan.FromMilliseconds(warm ? 100 : HoverDelayMs);
        delay.Start();
    }
    private void RetargetGroup(FrameworkElement target, bool keyboard)
    {
        CancelPending(); groupMotion.Stop();
        groupFrom = surfaceBounds; groupProgress = 0;
        owner = target; this.keyboard = keyboard;
        label.Text = TooltipHints.TextFor(target)!;
        var reopening = !IsOpen;
        IsOpen = true;
        ClearScrolls(); WatchScrolls(target); Observe();
        groupMotion.TickCallback = progress => { groupProgress = progress; InvalidateVisual(); };
        groupMotion.CompletedCallback = () => { groupProgress = 1; InvalidateVisual(); };
        groupMotion.Start();
        InvalidateMeasure();
        if (reopening) Animate(1);
    }
    internal void Leave(FrameworkElement target, bool lostFocus = false)
    {
        if (dismissed?.TryGetTarget(out var blocked) == true && blocked == target) dismissed = null;
        if (pending == target && (lostFocus ? pendingKeyboard : !pendingKeyboard)) CancelPending();
        if (owner == target && (lostFocus ? keyboard : !keyboard)) Hide();
    }
    internal void Refresh(FrameworkElement target)
    {
        if (pending == target && TooltipHints.TextFor(target) == null) CancelPending();
        if (owner != target || !IsOpen) return;
        if (!Eligible(target)) { Hide(); return; }
        var text = TooltipHints.TextFor(target)!;
        if (label.Text == text) return;
        label.Text = text; InvalidateMeasure();
    }
    internal void Dismiss(FrameworkElement? target = null)
    {
        if (target != null && target != owner && target != pending) return;
        if ((target ?? pending ?? owner) is { } blocked) dismissed = new(blocked);
        Hide();
    }
    private bool Eligible(FrameworkElement target)
    {
        if (!window.IsActive || target.FindVisualRoot() != window || !target.IsEffectivelyEnabled || TooltipHints.TextFor(target) == null) return false;
        for (Element? element = target; element != null; element = element.Parent)
            if (element is UIElement { IsVisible: false }) return false;
        var bounds = VisibleAnchor(target);
        return bounds.Width > 0 && bounds.Height > 0;
    }
    private Rect VisibleAnchor(FrameworkElement target)
    {
        var origin = target.TranslatePoint(default, window);
        var bounds = new Rect(origin.X, origin.Y, target.ActualWidth, target.ActualHeight).Intersect(new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height));
        for (var parent = target.Parent; parent != null; parent = parent.Parent)
            if (parent is ScrollViewer scroll) bounds = bounds.Intersect(scroll.Bounds);
        return bounds;
    }
    private void ShowPending()
    {
        delay.Stop();
        var next = pending;
        if (next == null || !Eligible(next) || !(pendingKeyboard ? next.IsFocused : next.IsMouseOver)) { CancelPending(); return; }
        // Keep the old tooltip at its original position throughout dismissal.
        // A different anchor is presented only once that fade has completed.
        if (attached && owner != next) { delay.Interval = TimeSpan.FromMilliseconds(20); delay.Start(); return; }
        pending = null; owner = next; keyboard = pendingKeyboard;
        label.Text = TooltipHints.TextFor(next)!;
        // Retained rendering records this small card once and applies its opacity
        // as a group. An extra bitmap cache duplicates that capture and prevents
        // a secondary CPU surface from reading the first GPU-cached frame.
        if (!attached) { window.OverlayLayer.Add(this); attached = true; }
        IsVisible = true; IsOpen = true; Observe();
        WatchScrolls(next);
        InvalidateMeasure(); Animate(1);
    }
    private void WatchScrolls(FrameworkElement target)
    {
        foreach (var parent in Ancestors(target))
            if (parent is ScrollViewer scroll)
            {
                void Scrolled() => Dismiss();
                scroll.ScrollChanged += Scrolled; scrolls.Add((scroll, Scrolled));
            }
    }
    private static IEnumerable<Element> Ancestors(Element target)
    { for (var parent = target.Parent; parent != null; parent = parent.Parent) yield return parent; }
    private void CancelPending()
    {
        pending = null; delay.Stop();
        if (!IsOpen) Unobserve();
    }
    private void Hide(bool immediate = false)
    {
        CancelPending(); IsOpen = false; Unobserve(); groupMotion.Stop();
        ClearScrolls();
        if (immediate) { motion.Stop(); amount = Card.Opacity = 0; Detach(); }
        else if (attached) Animate(0);
    }
    private void ClearScrolls()
    {
        foreach (var (scroll, handler) in scrolls) scroll.ScrollChanged -= handler;
        scrolls.Clear();
    }
    private void Animate(double target)
    {
        motion.Stop(); var from = amount;
        motion.Duration = TimeSpan.FromMilliseconds((target == 1 ? 150 : 75) * Math.Max(.15, Math.Abs(target - from)));
        motion.EasingFunction = target == 1 ? Easing.CubicBezier(0, 0, .2, 1) : Easing.CubicBezier(.4, 0, 1, 1);
        motion.TickCallback = t => { amount = from + (target - from) * t; Card.Opacity = amount; InvalidateVisual(); };
        motion.CompletedCallback = () => { amount = Card.Opacity = target; if (!IsOpen) Detach(); };
        motion.Start();
    }
    private void Detach()
    {
        groupMotion.Stop(); groupProgress = 1;
        IsVisible = false;
        if (attached) { window.OverlayLayer.Remove(this); attached = false; }
        owner = null; label.Text = "";
    }
    private void Observe()
    {
        if (observing) return;
        observing = true; window.FrameRendered += CheckAnchor;
    }
    private void Unobserve()
    {
        if (!observing) return;
        observing = false; window.FrameRendered -= CheckAnchor;
    }
    private void CheckAnchor()
    {
        if (pending is { } next && (!Eligible(next) || !(pendingKeyboard ? next.IsFocused : next.IsMouseOver))) CancelPending();
        if (!IsOpen || owner == null) return;
        if (!Eligible(owner) || !(keyboard ? owner.IsFocused : owner.IsMouseOver)) { Hide(); return; }
        Refresh(owner);
        var old = surfaceBounds;
        Place();
        if (surfaceBounds != old) InvalidateVisual();
    }
    private void Place()
    {
        if (!IsOpen || owner == null) return;
        var area = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);
        if (area.Width <= 16 || area.Height <= 16) return;
        var anchor = VisibleAnchor(owner);
        if (placedAnchor == anchor && placedWindow == window.ClientSize && !Card.IsMeasureDirty && !Card.IsArrangeDirty && placedGroupProgress == groupProgress) return;
        placedAnchor = anchor; placedWindow = window.ClientSize; placedGroupProgress = groupProgress;
        Card.MaxWidth = Math.Max(1, Math.Min(MaximumWidth, area.Width - 16));
        Card.MaxHeight = Math.Max(1, area.Height - 16);
        Card.Measure(new(Math.Max(1, area.Width - 16), Math.Max(1, area.Height - 16)));
        var size = Card.DesiredSize;
        var x = Math.Clamp(anchor.X + (anchor.Width - size.Width) / 2, 8, Math.Max(8, area.Width - size.Width - 8));
        var gap = TooltipHints.GroupFor(owner) != null ? AvatarGap : Gap;
        var above = anchor.Y - size.Height - gap >= 8;
        var y = above ? anchor.Y - size.Height - gap : anchor.Bottom + gap;
        y = Math.Clamp(y, 8, Math.Max(8, area.Height - size.Height - 8));
        surfaceBounds = new(x, y, size.Width, size.Height);
        if (groupMotion.IsRunning)
        {
            var width = Math.Clamp(groupFrom.Width + (size.Width - groupFrom.Width) * groupProgress, Math.Min(40, area.Width - 16), area.Width - 16);
            var height = Math.Clamp(groupFrom.Height + (size.Height - groupFrom.Height) * groupProgress, Math.Min(24, area.Height - 16), area.Height - 16);
            x = Math.Clamp(groupFrom.X + (x - groupFrom.X) * groupProgress, 8, Math.Max(8, area.Width - width - 8));
            y = Math.Clamp(groupFrom.Y + (y - groupFrom.Y) * groupProgress, 8, Math.Max(8, area.Height - height - 8));
            surfaceBounds = new(x, y, width, height);
        }
        var inset = Math.Min(Radius, surfaceBounds.Width / 2);
        origin = new(Math.Clamp(anchor.X + anchor.Width / 2, x + inset, surfaceBounds.Right - inset), above ? surfaceBounds.Bottom : y);
        Card.Arrange(surfaceBounds);
    }
    protected override Size MeasureContent(Size availableSize) { Place(); return default; }
    protected override Size ArrangeOverride(Size finalSize) { Place(); if (!IsOpen) Card.Arrange(surfaceBounds); return finalSize; }
    protected override bool TryGetChildTransform(out Matrix3x2 transform)
    {
        transform = Matrix3x2.CreateScale((float)(.8 + .2 * amount), new Vector2((float)origin.X, (float)origin.Y));
        return !transform.IsIdentity;
    }
    protected override UIElement? OnHitTest(Point point) => null;
    private void Deactivated() { Hide(true); dismissed = null; }
    private void PointerDown(MouseEventArgs _) => Dismiss();
    private void Wheel(MouseWheelEventArgs _) => Dismiss();
    private void PreviewKey(KeyEventArgs args)
    {
        if (args.Handled) return;
        if (args.Key == Key.Escape && (IsOpen || pending != null))
        {
            // A hint that has not appeared must not consume the Escape intended
            // for the search field or menu under the pointer.
            var shown = IsOpen; Dismiss(); if (shown) args.Handled = true;
        }
        else if (args.Key is Key.Tab or Key.Enter or Key.Space) Dismiss();
    }
    protected override void OnDispose()
    {
        if (disposed) return;
        disposed = true; Hide(true); delay.Dispose(); motion.Stop(); groupMotion.Stop();
        window.Deactivated -= Deactivated; window.PreviewKeyDown -= PreviewKey;
        window.MouseDown -= PointerDown; window.MouseWheel -= Wheel;
        base.OnDispose();
    }
}
