using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using System.Runtime.CompilerServices;

namespace Pookie.App;

// Keyboard focus remains usable after a pointer click, but only keyboard input shows its cue.
internal static class KeyboardFocusFeedback
{
    internal static readonly MewProperty<bool> VisibleProperty = MewProperty<bool>.Register<Control>("KeyboardFocusVisible", false);
    private static readonly ConditionalWeakTable<Control, ObservableValue<bool>> values = new();

    internal static void Attach(Control control)
    {
        control.Bind(VisibleProperty, values.GetValue(control, static _ => new(false)));
        control.GotFocus += () => Set(control, true);
        control.LostFocus += () => Set(control, false);
        control.MouseDown += _ => Set(control, false);
        // Popup policy can consume the trigger's down event while restoring its focus.
        control.MouseUp += _ => Set(control, false);
        control.KeyDown += _ => Set(control, true);
    }

    internal static bool IsVisible(Control control) => values.TryGetValue(control, out var visible) && visible.Value;
    internal static void Set(Control control, bool keyboard)
    {
        if (values.TryGetValue(control, out var visible)) visible.Value = keyboard && control.IsFocused;
    }
}
