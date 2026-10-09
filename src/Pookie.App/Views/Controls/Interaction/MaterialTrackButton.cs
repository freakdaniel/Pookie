using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Keep the visible background in one property slot. Changing named styles in
// MewUI 0.22 can snap their setters before the new state's transition starts.
internal sealed class MaterialTrackButton : Button
{
    private Color? target;
    private bool pending, pendingHot, snapNext;
    private static readonly Transition[] colorTransitions =
        [Transition.Create(Control.BackgroundProperty, 240, Easing.CubicBezier(.2, 0, 0, 1))];

    internal void Variant(string variant)
    {
        StyleName = variant;
        RefreshSurface(snapNext); snapNext = false;
    }

    internal void SnapNextChange() => snapNext = true;

    internal void Availability(bool available, bool waiting)
    {
        if (waiting && !pending)
        {
            pendingHot = IsMouseOver; pending = true;
            // Pending requests still reject input, but never flash a disabled
            // colour/opacity between the inactive and active states.
            Opacity = 1;
        }
        IsEnabled = available;
        if (!waiting && pending)
        {
            pending = false; ClearLocalValue(UIElement.OpacityProperty);
        }
        RefreshSurface();
    }

    private void RefreshSurface(bool snap = false)
    {
        var variant = StyleName;
        if (variant == null) return;
        var surface = TrackButtons.Surface(variant);
        var foreground = TrackButtons.Foreground(variant);
        var hot = pending ? pendingHot : IsEnabled && IsMouseOver;
        var next = !pending && IsEnabled && IsPressed ? TrackButtons.PressedColor(surface, foreground) :
            hot ? TrackButtons.HoverColor(surface, foreground) : surface;
        if (target == next && !snap) return;
        var first = target == null;
        target = next;
        Transitions = first || snap ? [] : colorTransitions;
        Background = next;
        Transitions = colorTransitions;
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == IsMouseOverProperty || property == IsPressedProperty || property == IsEnabledProperty)
            RefreshSurface();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        RefreshSurface();
        base.OnRender(context);
    }
}
