using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using System.Runtime.CompilerServices;

namespace Pookie.App;

// Keyboard focus remains usable after a pointer click, but only keyboard input shows its cue.
internal static class KeyboardFocusFeedback
{
    internal static readonly MewProperty<bool> VisibleProperty = MewProperty<bool>.Register<Control>("KeyboardFocusVisible", false);
    private sealed class State
    {
        internal readonly ObservableValue<bool> Visible = new(false);
        internal bool? Restoring;
    }
    private static readonly ConditionalWeakTable<Control, State> values = new();

    internal static void Attach(Control control)
    {
        var state = values.GetValue(control, static _ => new());
        control.Bind(VisibleProperty, state.Visible);
        control.GotFocus += () => Set(control, state.Restoring ?? true);
        control.LostFocus += () => Set(control, false);
        control.MouseDown += _ => Set(control, false);
        // Popup policy can consume the trigger's down event while restoring its focus.
        control.MouseUp += _ => Set(control, false);
        control.KeyDown += _ => Set(control, true);
    }

    internal static bool IsVisible(Control control) => values.TryGetValue(control, out var state) && state.Visible.Value;
    internal static void Set(Control control, bool keyboard)
    {
        if (values.TryGetValue(control, out var state)) state.Visible.Value = keyboard && control.IsFocused;
    }
    internal static void Restore(UIElement target, bool keyboard)
    {
        if (target is not Control control || !values.TryGetValue(control, out var state)) { target.Focus(); return; }
        state.Restoring = keyboard;
        try { control.Focus(); Set(control, keyboard); }
        finally { state.Restoring = null; }
    }
}
