using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ModalDialog shortcutDialog = null!;
    private Border shortcutCapture = null!, shortcutPreview = null!;
    private TextBlock shortcutTitle = null!, shortcutCaption = null!, shortcutNotice = null!, shortcutSaveLabel = null!;
    private Button shortcutSave = null!;
    private string? pendingShortcut;

    private FrameworkElement ShortcutRecorderView()
    {
        shortcutTitle = new TextBlock().FontSize(23).SemiBold().TextWrapping(TextWrapping.Wrap);
        shortcutCaption = new TextBlock().FontSize(13).Foreground(Muted).CenterHorizontal();
        shortcutPreview = new Border().Background(Color.Transparent).BorderThickness(0).Center();
        shortcutCapture = new Border().Height(118).CornerRadius(16).BorderThickness(1)
            .Background(Color.FromRgb(28, 28, 32)).Focusable(true).Child(new StackPanel().Vertical().Spacing(12).Center()
                .Children(shortcutCaption, shortcutPreview));
        shortcutCapture.IsTabStop = true;
        shortcutCapture.GotFocus += () => shortcutCapture.BorderBrush = Color.FromRgb(108, 108, 122);
        shortcutCapture.LostFocus += () => shortcutCapture.BorderBrush = Edge;
        shortcutNotice = new TextBlock().FontSize(13).Foreground(TrackButtons.OnSelected).TextWrapping(TextWrapping.Wrap).IsVisible(false);
        var cancel = TrackButtons.Action(new TextBlock().Text("Отмена").FontSize(14).SemiBold(), CloseShortcutRecorder, TrackButtons.Glass);
        shortcutSaveLabel = new TextBlock().FontSize(14).SemiBold().Foreground(TrackButtons.OnPrimary);
        shortcutSave = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(
            Icons.View("check", 18, TrackButtons.OnPrimary).CenterVertical(), shortcutSaveLabel.CenterVertical()), SavePendingShortcut, TrackButtons.Filled);
        var body = new StackPanel().Vertical().Spacing(18).Margin(28).Children(shortcutTitle,
            new TextBlock().Text("Нажми новое сочетание клавиш или боковую кнопку мыши.").Foreground(Muted).FontSize(14),
            shortcutCapture, shortcutNotice, new StackPanel().Horizontal().Spacing(8).Right().Children(cancel, shortcutSave));
        shortcutDialog = new ModalDialog(TrackDialogChrome.MatteBody(body).Width(540), shortcutCapture,
            staticBackdrop: true, cacheTransition: true, backdropTint: TrackDialogChrome.MatteTint) { DismissRequested = CloseShortcutRecorder };
        return shortcutDialog;
    }

    private void OpenShortcutRecorder(ShortcutBinding binding)
    {
        shortcutButtons[binding].Focus();
        recordingShortcut = binding;
        shortcutTitle.Text = binding.Title;
        PreviewShortcut(null);
        shortcutDialog.Show();
    }

    private void CloseShortcutRecorder()
    {
        recordingShortcut = null;
        pendingShortcut = null;
        shortcutDialog.Close();
    }

    private void PreviewShortcut(string? value)
    {
        if (recordingShortcut is not { } binding) return;
        pendingShortcut = value;
        var shown = value ?? Assigned(binding);
        var conflicts = ShortcutConflicts(binding, shown);
        shortcutCaption.Text = value == null ? (shown.Length == 0 ? "Сочетание не назначено" : "Текущее сочетание")
            : value.Length == 0 ? "Без сочетания" : "Новое сочетание";
        shortcutPreview.Child = ShortcutKeycaps.View(shown, large: true);
        AutomationProperties.SetName(shortcutCapture, shortcutCaption.Text + (shown.Length > 0 ? ". " + ShortcutBinding.Display(shown) : ""));
        shortcutSaveLabel.Text = conflicts.Length == 0 ? "Сохранить" : "Переназначить";
        shortcutSave.IsEnabled = value != null && value != Assigned(binding);
        shortcutNotice.IsVisible = conflicts.Length > 0;
        if (conflicts.Length > 0)
            shortcutNotice.Text = "Это сочетание уже используется для " + string.Join(", ", conflicts.Select(other => $"«{other.Title}»")) +
                ". При переназначении оно будет снято с этой команды.";
        shortcutDialog.InvalidateTransitionSnapshot();
    }

    private void SavePendingShortcut()
    {
        if (recordingShortcut is not { } binding || pendingShortcut is not { } value || !shortcutSave.IsEnabled) return;
        StoreShortcut(binding, value, ShortcutConflicts(binding, value));
        CloseShortcutRecorder();
    }
}
