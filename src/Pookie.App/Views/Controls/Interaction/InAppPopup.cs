using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Share the chat popup's actual compositor and close animation. No native popup
// window can show an opaque background before/after its animated contents.
internal class InAppPopup : Panel
{
    private static readonly ConditionalWeakTable<Window, List<InAppPopup>> open = new();
    private readonly List<(ScrollViewer Scroll, Action Handler)> scrollOwners = [];
    private ChatPopupLayer? layer;
    private Panel? host;
    private Window? window;
    private UIElement? owner;
    private InAppPopup? parentPopup;
    private bool observing, presented;
    private bool ownerKeyboard;
    internal bool IsOpen => layer?.IsOpen == true;
    internal UIElement? FocusReturnTarget => owner;
    internal double Reveal => layer?.Reveal ?? 0;
    internal Action? Dismissing;

    internal void InitializePopup(FrameworkElement content, Func<Rect> anchor, Func<MenuPlacement> placement)
    {
        layer = new ChatPopupLayer(content, anchor, () => host?.Bounds ?? Rect.Empty, placement: placement).Initialize();
        layer.Closed = Detach;
        Add(layer); IsVisible = false;
    }
    internal void ShowPopup(UIElement trigger, UIElement focusTarget)
    {
        if (trigger.FindVisualRoot() is not Window source || source.Content is not Panel root || layer == null) return;
        if (window != null && window != source) Close(true);
        window = source; owner = trigger; host = root;
        ownerKeyboard = trigger is Control control && KeyboardFocusFeedback.IsVisible(control);
        parentPopup = null;
        for (var ancestor = trigger.Parent; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor is InAppPopup parent) { parentPopup = parent; break; }
        var entries = open.GetValue(source, _ => []);
        if (parentPopup == null)
            foreach (var other in entries.ToArray()) if (!ReferenceEquals(other, this)) other.Close();
        if (Parent == null) { this.Row(0); root.Add(this); }
        if (!entries.Contains(this)) entries.Add(this);
        IsVisible = true; presented = false; layer.Show();
        if (!observing)
        {
            observing = true; source.FrameRendered += Observe; source.Deactivated += Deactivated;
            for (var ancestor = trigger.Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor is ScrollViewer scroll)
                {
                    var x = scroll.HorizontalOffset; var y = scroll.VerticalOffset;
                    var maximum = SmoothScroll.Maximum(scroll);
                    void Scrolled()
                    {
                        var nextMaximum = SmoothScroll.Maximum(scroll);
                        var moved = maximum == nextMaximum && (x != scroll.HorizontalOffset || y != scroll.VerticalOffset);
                        x = scroll.HorizontalOffset; y = scroll.VerticalOffset; maximum = nextMaximum;
                        // Focus/extent reconciliation during the opening layout
                        // is not a user scroll and must not dismiss a new menu.
                        if (presented && moved) Close(restoreFocus: false);
                    }
                    scroll.ScrollChanged += Scrolled; scrollOwners.Add((scroll, Scrolled));
                }
        }
        focusTarget.Focus();
    }
    internal void Close(bool immediate = false, bool restoreFocus = true)
    {
        if (layer == null || !IsOpen && !immediate) return;
        var hadFocus = HasFamilyFocus();
        if (window != null && open.TryGetValue(window, out var entries))
            foreach (var child in entries.ToArray()) if (child.parentPopup == this) child.Close(immediate, false);
        layer.Close(immediate); Dismissing?.Invoke();
        if (hadFocus && window?.IsActive == true)
        {
            if (restoreFocus && owner?.FindVisualRoot() == window) KeyboardFocusFeedback.Restore(owner, ownerKeyboard);
            else window.FocusManager.ClearFocus();
        }
    }
    private bool HasFamilyFocus()
    {
        if (IsFocusWithin) return true;
        return window != null && open.TryGetValue(window, out var entries) &&
            entries.Any(child => child.parentPopup == this && child.IsOpen && child.HasFamilyFocus());
    }
    private void Observe()
    {
        if (!IsOpen) return;
        presented = true;
        var visible = owner?.FindVisualRoot() == window;
        for (Element? ancestor = owner; visible && ancestor != null; ancestor = ancestor.Parent)
            if (ancestor is UIElement { IsVisible: false }) visible = false;
        if (!visible || !HasFamilyFocus() && owner?.IsFocusWithin != true) Close();
    }
    private void Deactivated() => Close(true, false);
    private void Detach()
    {
        if (observing && window != null)
        { window.FrameRendered -= Observe; window.Deactivated -= Deactivated; observing = false; }
        foreach (var (scroll, handler) in scrollOwners) scroll.ScrollChanged -= handler;
        scrollOwners.Clear();
        if (window != null && open.TryGetValue(window, out var entries)) entries.Remove(this);
        IsVisible = false; host?.Remove(this);
    }
    internal static bool HandleKey(Window window, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || !open.TryGetValue(window, out var entries)) return false;
        var popup = entries.LastOrDefault(p => p.IsOpen);
        if (popup == null) return false;
        popup.Close(); e.Handled = true; return true;
    }
    internal static bool HasFocusFor(UIElement owner)
    {
        if (owner.FindVisualRoot() is not Window window || !open.TryGetValue(window, out var entries)) return false;
        foreach (var popup in entries)
            if (popup.IsOpen && popup.HasFamilyFocus())
                for (var ancestor = popup.owner; ancestor != null; ancestor = ancestor.Parent as UIElement)
                    if (ReferenceEquals(ancestor, owner)) return true;
        return false;
    }
    protected override Size MeasureContent(Size availableSize) { layer?.Measure(availableSize); return default; }
    protected override Size ArrangeOverride(Size finalSize) { layer?.Arrange(Bounds); return finalSize; }
    protected override UIElement? OnHitTest(Point point) => layer?.HitTest(point);
    protected override void OnDispose() { Close(true, false); Detach(); base.OnDispose(); }
}
