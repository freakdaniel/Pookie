using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Button profileButton = null!, settingsButton = null!, queueButton = null!;
    private readonly List<(ObservableValue<bool> State, MenuLayer Layer, Button Anchor)> shellMenus = [];

    private FloatingMenuLayer ShellMenu(Button anchor, Border card, ObservableValue<bool> state, FloatingMenuPlacement placement)
    {
        var layer = new FloatingMenuLayer(anchor, card, placement).Initialize();
        RegisterShellMenu(anchor, layer, state);
        return layer;
    }

    private void RegisterShellMenu(Button anchor, MenuLayer layer, ObservableValue<bool> state)
    {
        // Give empty card areas a focus target, so interacting inside does not dismiss the menu.
        layer.Card.Focusable = true;
        shellMenus.Add((state, layer, anchor));
        state.Changed += () =>
        {
            if (state.Value)
            {
                foreach (var other in shellMenus) if (!ReferenceEquals(other.State, state)) other.State.Value = false;
                CloseChatAttachMenu(); layer.Show(); anchor.Focus();
            }
            else
            {
                if (layer.IsFocusWithin) anchor.Focus();
                layer.Close();
            }
        };
        anchor.OnKeyDown(e =>
        {
            if (e.Key is Key.Up or Key.Down) { e.Handled = true; state.Value = true; }
        });
    }

    private void DismissUnfocusedShellMenus()
    {
        // Check after focus routing has completed, including moves between items in the card.
        foreach (var menu in shellMenus)
            if (menu.State.Value && !menu.Anchor.IsFocusWithin && !menu.Layer.IsFocusWithin)
                menu.State.Value = false;
    }

    private void CloseShellMenus(bool immediate = false)
    {
        foreach (var menu in shellMenus)
        {
            menu.State.Value = false;
            if (immediate) menu.Layer.Close(true);
        }
    }

    private static Button MenuChoice(string icon, string text, Action action)
    {
        var button = TrackButtons.Action(new StackPanel().Horizontal().Spacing(12).Children(
            Icons.View(icon, 21).CenterVertical(), new TextBlock().Text(text).FontSize(14).CenterVertical()),
            action, TrackButtons.Text, 42).Padding(12, 0);
        ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        return button;
    }
}
