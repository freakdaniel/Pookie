using Pookie.Localization;
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
        var groups = ShortcutBinding.All.GroupBy(binding => binding.GroupKey)
            .Select(group => SettingsGroup(UiText.Get(group.Key), group.Select(ShortcutRow).ToArray())).ToList();
        groups.Add(SettingsGroup(UiText.Get("settings.chats.messagesAttachments"),
            SettingsChoiceWithContent("chat_send", UiText.Get("settings.chats.sendMessage"), UiText.Get("settings.chats.sendKeyDescription"),
                () => preferences.ChatSendOnEnter, value => UpdatePreferences(p => p with { ChatSendOnEnter = value }), (key, _) => ShortcutKeycaps.View(key), ("Ctrl+Enter", false), ("Enter", true)),
            SettingsRow(UiText.Get("chats.search.results"), "", new StackPanel().Horizontal().Spacing(8).Children(
                ShortcutKeycaps.View("Enter"), new TextBlock().Text("/").Foreground(Muted).CenterVertical(), ShortcutKeycaps.View("Shift+Enter"))),
            SettingsRow(UiText.Get("shortcuts.chats.pauseVideo"), "", ShortcutKeycaps.View("Space")),
            SettingsRow(UiText.Get("shortcuts.chats.switchAttachment"), "", new StackPanel().Horizontal().Spacing(6).Children(ShortcutKeycaps.View("Left"), ShortcutKeycaps.View("Right"))),
            SettingsRow(UiText.Get("shortcuts.closePanel"), "", ShortcutKeycaps.View("Escape"))));
        return SettingsGroups(groups.ToArray());
    }

    private FrameworkElement ShortcutRow(ShortcutBinding binding)
    {
        var face = new Border().Background(Color.Transparent).BorderThickness(0);
        var edit = TrackButtons.Action(face, () => OpenShortcutRecorder(binding), TrackButtons.Text);
        var clear = TrackButtons.Action(new TextBlock().Text(UiText.Get("common.remove")), () => Run(() => AssignShortcutAsync(binding, "")), TrackButtons.Text);
        var reset = TrackButtons.Action(new TextBlock().Text(UiText.Get("common.reset")), () => Run(() => AssignShortcutAsync(binding, binding.Default)), TrackButtons.Text);
        shortcutButtons.Add(binding, edit);
        string? rendered = null;
        void Refresh()
        {
            var value = Assigned(binding);
            if (rendered == value) return;
            rendered = value;
            face.Child = ShortcutKeycaps.View(value);
            var description = value.Length > 0 ? UiText.Compose($"{UiText.Get("shortcuts.changePrefix")}{ShortcutBinding.Display(value)}") : UiText.Get("shortcuts.assignBinding");
            edit.Hint(description);
            AutomationProperties.SetName(edit, UiText.Compose($"{binding.Title}. {description}"));
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
                if (!await ConfirmAsync(UiText.Get("shortcuts.confirmReassign"),
                        detail: UiText.Format("shortcuts.conflict.reassign", ShortcutBinding.Display(value), titles, binding.Title), owner: Window)) return;
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
            target.Hint(UiText.Join(" · ", parts.Prepend(title ?? bindings[0].Title)));
        }
        shortcutRefreshers.Add(Refresh);
        Refresh();
        return target;
    }
}
