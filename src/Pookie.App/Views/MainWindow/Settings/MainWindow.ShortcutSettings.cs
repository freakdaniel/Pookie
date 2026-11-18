using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Automation;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ShortcutBinding? recordingShortcut;
    private bool confirmingShortcut;
    private readonly Dictionary<ShortcutBinding, Button> shortcutButtons = [];
    private readonly List<Action> shortcutRefreshers = [];

    private string Assigned(ShortcutBinding binding) => preferences.ShortcutOverrides.GetValueOrDefault(binding.Id) ?? binding.Default;

    private FrameworkElement ShortcutSettingsView()
    {
        var groups = ShortcutBinding.All.GroupBy(binding => binding.Group)
            .Select(group => SettingsGroup(group.Key, group.Select(ShortcutRow).ToArray())).ToList();
        groups.Add(SettingsGroup("Сообщения и вложения",
            SettingsChoiceWithContent("chat_send", "Отправлять сообщение", "При отправке по Enter новая строка — Shift+Enter. Ctrl+Enter также отправляет сообщение",
                () => preferences.ChatSendOnEnter, value => UpdatePreferences(p => p with { ChatSendOnEnter = value }), key => ShortcutKeycaps.View(key), ("Ctrl+Enter", false), ("Enter", true)),
            SettingsRow("Результаты поиска в переписке", "", new StackPanel().Horizontal().Spacing(8).Children(
                ShortcutKeycaps.View("Enter"), new TextBlock().Text("/").Foreground(Muted).CenterVertical(), ShortcutKeycaps.View("Shift+Enter"))),
            SettingsRow("Пауза видео во вложении", "", ShortcutKeycaps.View("Space")),
            SettingsRow("Переключение вложений", "", new StackPanel().Horizontal().Spacing(6).Children(ShortcutKeycaps.View("Left"), ShortcutKeycaps.View("Right"))),
            SettingsRow("Закрыть текущую панель или диалог", "", ShortcutKeycaps.View("Escape"))));
        return SettingsGroups(groups.ToArray());
    }

    private FrameworkElement ShortcutRow(ShortcutBinding binding)
    {
        var face = new Border().Background(Color.Transparent).BorderThickness(0);
        var edit = TrackButtons.Action(face, () => OpenShortcutRecorder(binding), TrackButtons.Text);
        var clear = TrackButtons.Action(new TextBlock().Text("Убрать"), () => Run(() => AssignShortcutAsync(binding, "")), TrackButtons.Text);
        var reset = TrackButtons.Action(new TextBlock().Text("Сбросить"), () => Run(() => AssignShortcutAsync(binding, binding.Default)), TrackButtons.Text);
        shortcutButtons.Add(binding, edit);
        string? rendered = null;
        void Refresh()
        {
            var value = Assigned(binding);
            if (rendered == value) return;
            rendered = value;
            face.Child = ShortcutKeycaps.View(value);
            var description = value.Length > 0 ? "Изменить сочетание · " + ShortcutBinding.Display(value) : "Назначить сочетание";
            edit.Hint(description);
            AutomationProperties.SetName(edit, binding.Title + ". " + description);
            clear.IsEnabled = value.Length > 0;
            reset.IsEnabled = value != binding.Default;
        }
        shortcutRefreshers.Add(Refresh);
        Refresh();
        return SettingsRow(binding.Title, "", new StackPanel().Horizontal().Spacing(4).Children(edit, clear, reset));

    }

    private void RefreshShortcuts()
    {
        foreach (var refresh in shortcutRefreshers) refresh();
    }

    private ShortcutBinding[] ShortcutConflicts(ShortcutBinding binding, string value) =>
        ShortcutBinding.All.Where(other => value.Length > 0 && other != binding && Assigned(other) == value &&
            (binding.Supports(false) && other.Supports(false) || binding.Supports(true) && other.Supports(true))).ToArray();

    private async Task AssignShortcutAsync(ShortcutBinding binding, string value)
    {
        var conflicts = ShortcutConflicts(binding, value);
        if (conflicts.Length > 0)
        {
            confirmingShortcut = true;
            try
            {
                var titles = string.Join(", ", conflicts.Select(other => $"«{other.Title}»"));
                if (!await MessageBox.AskYesNoAsync("Переназначить сочетание?",
                        detail: $"{ShortcutBinding.Display(value)} используется для {titles}. Передать его команде «{binding.Title}»?", owner: Window)) return;
            }
            finally { confirmingShortcut = false; }
        }
        StoreShortcut(binding, value, conflicts);
    }

    private void StoreShortcut(ShortcutBinding binding, string value, ShortcutBinding[] conflicts)
    {
        var updated = new Dictionary<string, string>(preferences.ShortcutOverrides);
        foreach (var conflict in conflicts) updated[conflict.Id] = "";
        if (value == binding.Default) updated.Remove(binding.Id);
        else updated[binding.Id] = value;
        UpdatePreferences(p => p with { ShortcutOverrides = updated });
    }

    private T ShortcutHint<T>(T target, ShortcutAction action, string? title = null) where T : FrameworkElement
    {
        var bindings = ShortcutBinding.All.Where(binding => binding.Action == action).ToArray();
        void Refresh()
        {
            var parts = bindings.Select(Assigned).Where(value => value.Length > 0).Select(ShortcutBinding.Display);
            target.Hint(string.Join(" · ", parts.Prepend(title ?? bindings[0].Title)));
        }
        shortcutRefreshers.Add(Refresh);
        Refresh();
        return target;
    }
}
