using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Button profileButton = null!, settingsButton = null!, queueButton = null!;
    private readonly List<(ObservableValue<bool> State, FloatingMenuLayer Layer, FloatingMenuHover Hover)> shellMenus = [];

    private FloatingMenuLayer ShellMenu(Button anchor, Border card, ObservableValue<bool> state, FloatingMenuPlacement placement)
    {
        var layer = new FloatingMenuLayer(anchor, card, placement).Initialize();
        var hover = new FloatingMenuHover(anchor, card,
            () => { if (Window.IsActive && anchor.IsEffectivelyEnabled) state.Value = true; }, () => state.Value = false);
        shellMenus.Add((state, layer, hover));
        state.Changed += () =>
        {
            hover.Stop();
            if (state.Value)
            {
                foreach (var other in shellMenus) if (!ReferenceEquals(other.State, state)) other.State.Value = false;
                CloseChatAttachMenu(); layer.Show();
            }
            else layer.Close();
        };
        anchor.OnKeyDown(e =>
        {
            if (e.Key is Key.Up or Key.Down) { e.Handled = true; state.Value = true; }
        });
        return layer;
    }

    private void CloseShellMenus(bool immediate = false)
    {
        foreach (var menu in shellMenus)
        {
            menu.Hover.Stop(); menu.State.Value = false;
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
