using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal static class ShortcutKeycaps
{
    internal static StackPanel View(string binding, bool large = false)
    {
        var hasGesture = KeyGesture.TryParse(binding, out var gesture);
        // Keep five-part chords readable inside the fixed-width recorder.
        var spacious = large && (!hasGesture || System.Numerics.BitOperations.PopCount((uint)gesture.Modifiers) < 3);
        var keys = new StackPanel().Horizontal().Spacing(spacious ? 7 : 4).Center().IsHitTestVisible(false);
        if (binding is "Mouse4" or "Mouse5")
            Add(binding == "Mouse4" ? "mouse-button-4" : "mouse-button-5", binding);
        else if (hasGesture)
        {
            if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) Add("caret-up", "Ctrl");
            if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) Add("option", OperatingSystem.IsMacOS() ? "Option" : "Alt");
            if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) Add("arrow-fat-up", "Shift");
            if (gesture.Modifiers.HasFlag(ModifierKeys.Meta))
            {
                if (OperatingSystem.IsMacOS()) Add("command", "Cmd");
                else if (OperatingSystem.IsWindows()) Add("windows-logo", "Win");
                else Add("keyboard", "Super");
            }
            var (icon, name) = gesture.Key switch
            {
                Key.Left => ("arrow-left", UiText.Get("key.left")), Key.Right => ("arrow-right", UiText.Get("key.right")),
                Key.Up => ("arrow-up", UiText.Get("key.up")), Key.Down => ("arrow-down", UiText.Get("key.down")),
                Key.Enter => ("key-return", "Enter"), Key.Backspace => ("backspace", "Backspace"),
                Key.Space => ("keyboard", UiText.Get("key.space")), Key.Escape => ("keyboard", "Esc"),
                _ => ("keyboard", new KeyGesture(gesture.Key).ToDisplayString())
            };
            Add(icon, name);
        }
        else
            keys.Add(new TextBlock().Text(large ? "—" : UiText.Get("shortcuts.assign")).Foreground(TextAppearance.Secondary).Center());
        AutomationProperties.SetName(keys, binding.Length == 0 ? UiText.Get("shortcuts.unassigned") : ShortcutBinding.Display(binding));
        return keys;

        void Add(string icon, string name)
        {
            if (keys.Children.Count > 0)
                keys.Add(Icons.View("plus", spacious ? 12 : 9, TextAppearance.Secondary).CenterVertical());
            var face = new StackPanel().Horizontal().Spacing(spacious ? 7 : 4).Center().Children(
                Icons.View(icon, spacious ? 24 : 16).CenterVertical(),
                new TextBlock().Text(name).FontSize(spacious ? 18 : 13).SemiBold().Foreground(TrackButtons.OnSurface).CenterVertical());
            keys.Add(new Border().Height(spacious ? 50 : 28).Padding(spacious ? 12 : 6, 0).CornerRadius(spacious ? 9 : 6)
                .Background(TrackButtons.TonalSurface).BorderBrush(Color.FromRgb(89, 89, 96))
                .BorderThickness(new Thickness(1, 1, 1, spacious ? 3 : 2)).Child(face));
        }
    }
}
