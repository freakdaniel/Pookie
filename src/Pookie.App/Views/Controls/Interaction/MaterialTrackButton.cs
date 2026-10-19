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
    private (Color Surface, Color Foreground)? palette;
    private bool pending, pendingHot, snapNext;
    private Color surfaceFrom;
    private readonly AnimationClock surfaceClock = new(TimeSpan.FromMilliseconds(240), Easing.CubicBezier(.2, 0, 0, 1));
    private bool connected, groupStart;
    private double innerCorner = 8, shapeFrom, shapeTarget = 8;
    private readonly AnimationClock shapeClock = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    internal bool IsConnected => connected;
    internal CornerRadius ConnectedCorners => groupStart
        ? new(CornerRadius, innerCorner, innerCorner, CornerRadius)
        : new(innerCorner, CornerRadius, CornerRadius, innerCorner);

    internal MaterialTrackButton()
    {
        // Own the colour animation: straight RGBA transitions briefly make the
        // translucent white glass opaque before it has acquired its selected tint.
        Transitions = [];
        surfaceClock.TickCallback = amount => Background = BlendSurface(surfaceFrom, target!.Value, amount);
    }

    private static Color BlendSurface(Color from, Color to, double amount)
    {
        if (amount <= 0) return from;
        if (amount >= 1) return to;
        var alpha = from.A + (to.A - from.A) * amount;
        if (alpha <= 0) return Color.Transparent;
        // Interpolate premultiplied channels so the visible colour follows a
        // direct path over any artwork, including during interrupted transitions.
        byte Channel(byte start, byte end) => (byte)Math.Clamp(Math.Round(
            (start * from.A * (1 - amount) + end * to.A * amount) / alpha), 0, 255);
        return Color.FromArgb((byte)Math.Round(alpha), Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    internal void Variant(string variant)
    {
        StyleName = variant;
        RefreshSurface(snapNext); RefreshShape(snapNext); snapNext = false;
    }

    internal void Palette(Color surface, Color foreground)
    {
        palette = (surface, foreground);
        RefreshSurface();
    }

    internal void Connect(bool start)
    {
        connected = true; groupStart = start;
        shapeClock.TickCallback = amount => { innerCorner = shapeFrom + (shapeTarget - shapeFrom) * amount; InvalidateVisual(); };
        RefreshShape(true);
    }

    private void RefreshShape(bool snap = false)
    {
        if (!connected) return;
        var next = StyleName is TrackButtons.Selected or TrackButtons.Reposted ? CornerRadius : 8;
        if (shapeTarget == next && !snap) return;
        shapeClock.Stop(); shapeTarget = next;
        if (snap || FindVisualRoot() is not Window) { innerCorner = next; InvalidateVisual(); return; }
        shapeFrom = innerCorner; shapeClock.Start();
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
        var surface = palette?.Surface ?? TrackButtons.Surface(variant);
        var foreground = palette?.Foreground ?? TrackButtons.Foreground(variant);
        var hot = pending ? pendingHot : IsEnabled && IsMouseOver;
        // Connected toggles animate directly between selection states. A separate
        // pressed colour/shape would introduce a detour during that transition.
        var next = !connected && !pending && IsEnabled && IsPressed ? TrackButtons.PressedColor(surface, foreground) :
            hot ? TrackButtons.HoverColor(surface, foreground) : surface;
        if (target == next && !snap) return;
        var first = target == null;
        surfaceClock.Stop();
        surfaceFrom = Background;
        target = next;
        if (first || snap || FindVisualRoot() is not Window) Background = next;
        else surfaceClock.Start();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == IsMouseOverProperty || property == IsPressedProperty || property == IsEnabledProperty)
        { RefreshSurface(); RefreshShape(); }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        RefreshSurface();
        if (connected)
            DrawBackgroundAndBorder(context, GetSnappedBorderBounds(Bounds), Background, BorderBrush, new Thickness(BorderThickness), ConnectedCorners);
        else base.OnRender(context);
    }

    protected override void OnDispose()
    {
        surfaceClock.Stop(); shapeClock.Stop();
        base.OnDispose();
    }
}
