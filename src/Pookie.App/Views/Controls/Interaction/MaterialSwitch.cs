using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A visual template retains ToggleSwitch's native capture, keyboard and checked semantics.
internal static class MaterialSwitch
{
    private static readonly ControlTemplate template = new DelegateControlTemplate<ToggleSwitch>((owner, context) =>
    {
        var visual = new MaterialSwitchVisual();
        context.Bind(visual, MaterialSwitchVisual.SelectedProperty, ToggleBase.IsCheckedProperty);
        context.Bind(visual, MaterialSwitchVisual.PressedProperty, Control.IsPressedProperty);
        context.Bind(visual, MaterialSwitchVisual.HotProperty, UIElement.IsMouseOverProperty);
        context.Bind(visual, MaterialSwitchVisual.FocusedProperty, KeyboardFocusFeedback.VisibleProperty);
        context.Bind(visual, MaterialSwitchVisual.EnabledProperty, UIElement.IsEnabledProperty);
        return visual;
    });

    internal static ToggleSwitch Create(bool selected, Action<bool> changed)
    {
        var control = new ToggleSwitch().Width(40).Height(40).Padding(0).BorderThickness(0);
        control.Opacity = 1;
        KeyboardFocusFeedback.Attach(control);
        control.Template = template;
        control.IsChecked = selected;
        control.CheckedChanged += changed;
        return control;
    }
}

internal sealed class MaterialSwitchVisual : Control
{
    internal static readonly MewProperty<bool> SelectedProperty = MewProperty<bool>.Register<MaterialSwitchVisual>("Selected", false);
    internal static readonly MewProperty<bool> PressedProperty = MewProperty<bool>.Register<MaterialSwitchVisual>("Pressed", false);
    internal static readonly MewProperty<bool> HotProperty = MewProperty<bool>.Register<MaterialSwitchVisual>("Hot", false);
    internal static readonly MewProperty<bool> FocusedProperty = MewProperty<bool>.Register<MaterialSwitchVisual>("Focused", false);
    internal static readonly MewProperty<bool> EnabledProperty = MewProperty<bool>.Register<MaterialSwitchVisual>("Enabled", true);
    private static readonly Color Surface = Color.FromRgb(24, 24, 24);
    private static readonly Color HandleOff = Color.FromRgb(147, 144, 149);
    private static readonly Color OnSurfaceVariant = Color.FromRgb(202, 198, 204);
    private readonly AnimationClock travel = new(TimeSpan.FromMilliseconds(300), Easing.CubicBezier(.175, .885, .32, 1.275));
    private readonly AnimationClock appearance = new(TimeSpan.FromMilliseconds(250), Easing.CubicBezier(.2, 0, 0, 1));
    private double position, positionFrom, positionTarget;
    private Look current, from, target;
    internal double SelectionPosition => position;
    internal double HandleSize => current.Size;
    internal double StateOpacity => current.State;
    internal bool Animating => travel.IsRunning || appearance.IsRunning;
    internal Rect TrackBounds => new(Bounds.X, Bounds.Y + (Bounds.Height - 24) / 2, 40, 24);
    internal Rect HandleBounds
    {
        get
        {
            var track = TrackBounds;
            return new(track.X + 12 + position * 16 - current.Size / 2,
                track.Y + 12 - current.Size / 2, current.Size, current.Size);
        }
    }

    internal MaterialSwitchVisual()
    {
        IsHitTestVisible = false;
        Width = 40; Height = 40;
        current = from = target = ResolveLook();
        travel.TickCallback = value => { position = positionFrom + (positionTarget - positionFrom) * value; InvalidateVisual(); };
        travel.CompletedCallback = () => { position = positionTarget; InvalidateVisual(); };
        appearance.TickCallback = value => { current = from.Blend(target, value); InvalidateVisual(); };
        appearance.CompletedCallback = () => { current = target; InvalidateVisual(); };
    }

    private readonly record struct Look(double Size, Color Track, Color Handle, double State)
    {
        internal Look Blend(Look next, double amount) => new(Size + (next.Size - Size) * amount,
            Track.Lerp(next.Track, amount), Handle.Lerp(next.Handle, amount),
            State + (next.State - State) * amount);
    }

    private Look ResolveLook()
    {
        var selected = GetValue(SelectedProperty);
        var enabled = GetValue(EnabledProperty) && IsEffectivelyEnabled;
        var pressed = enabled && GetValue(PressedProperty);
        var focused = enabled && GetValue(FocusedProperty);
        var hot = enabled && GetValue(HotProperty);
        var track = selected ? TrackButtons.OnSurface : TrackButtons.TonalSurface;
        var handle = selected ? TrackButtons.OnPrimary : HandleOff;
        if (!enabled)
        {
            track = selected ? TrackButtons.StateColor(Surface, TrackButtons.OnSurface, .12) : TrackButtons.TonalSurface;
            handle = selected ? Surface : TrackButtons.StateColor(track, TrackButtons.OnSurface, .38);
        }
        else if (hot || focused || pressed)
            handle = selected ? Color.FromRgb(64, 64, 69) : OnSurfaceVariant;
        return new(pressed ? 21 : selected ? 18 : 12, track, handle,
            pressed || focused ? .12 : hot ? .08 : 0);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == SelectedProperty || property == PressedProperty || property == HotProperty ||
            property == FocusedProperty || property == EnabledProperty || property == IsEnabledProperty)
            Refresh();
    }

    private void Refresh()
    {
        var nextPosition = GetValue(SelectedProperty) ? 1d : 0;
        var next = ResolveLook();
        var snap = FindVisualRoot() is not Window || !GetValue(EnabledProperty);
        if (positionTarget != nextPosition || snap)
        {
            travel.Stop(); positionFrom = position; positionTarget = nextPosition;
            if (snap) position = nextPosition; else travel.Start();
        }
        if (target != next || snap)
        {
            appearance.Stop(); from = current; target = next;
            appearance.Duration = TimeSpan.FromMilliseconds(GetValue(PressedProperty) ? 100 : 250);
            if (snap) current = next; else appearance.Start();
        }
        InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize) => new(40, 40);
    protected override void OnRender(IGraphicsContext context)
    {
        var track = TrackBounds;
        context.FillRoundedRectangle(track, 12, 12, current.Track);
        var handle = HandleBounds;
        if (current.State > 0)
            context.FillEllipse(new Rect(handle.X + handle.Width / 2 - 15, track.Y - 3, 30, 30),
                Color.FromArgb((byte)Math.Round(current.State * 255), 232, 232, 235));
        context.FillEllipse(handle, current.Handle);
    }

    protected override void OnDispose() { travel.Stop(); appearance.Stop(); base.OnDispose(); }
}
