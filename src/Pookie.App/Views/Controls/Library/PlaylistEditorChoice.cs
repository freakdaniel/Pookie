using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class PlaylistEditorChoice : ContentControl
{
    private readonly (string Value, string Label)[] options;
    private readonly TextBlock label;
    private readonly AnimatedContextMenu menu;
    internal string Value { get; private set; } = "";
    internal Action? Changed;
    internal PlaylistEditorChoice(string name, params (string Value, string Label)[] options)
    {
        this.options = options;
        Background = Color.Transparent; BorderThickness = 0; Padding = default;
        label = new TextBlock().FontSize(14).TextTrimming(TextTrimming.CharacterEllipsis).CenterVertical();
        menu = PopupAppearance.CreateMenu(MenuPlacement.Below);
        var button = TrackButtons.Action(new Grid().Columns("*,18").Spacing(10).Children(label, Icons.View("caret-down", 18).Column(1).CenterVertical()),
            () => { }, TrackButtons.Text, 36).Padding(8, 4).Margin(-8, -4).CornerRadius(12).Hint(name);
        ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        foreach (var option in options)
        {
            var command = new Command("pookie.playlist-editor." + name + "." + option.Value, option.Label);
            button.Commands.Register(command, () => { Select(option.Value); Changed?.Invoke(); }); menu.Item(command);
        }
        PopupAppearance.Attach(button, menu); button.OnClick(() => menu.Toggle(button)); Content = button;
    }
    internal void Select(string value)
    { Value = value; label.Text = options.FirstOrDefault(option => option.Value == value).Label ?? value; }
    internal void CloseMenu() => menu.Close(true, false);
}
