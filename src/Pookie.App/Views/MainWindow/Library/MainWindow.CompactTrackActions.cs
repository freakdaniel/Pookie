using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Realize secondary controls on first interaction, then reuse them as rows
    // recycle. Scrolling untouched rows and miniature previews stays inexpensive.
    private void EnsureCompactTrackActions(CompactTrackRow row)
    {
        if (row.More != null) return;
        var repostIcon = Icons.View("repeat", 18, MutedIcon).Center();
        var repost = TrackButtons.Icon(repostIcon, () => { if (row.Track is { } track) Run(() => ToggleTrackRepostAsync(track)); }, TrackButtons.Text, 32).Hint("Сделать репост");
        var copy = TrackButtons.Icon(Icons.View("copy", 18), () => { if (row.Track is { } track) CopyTrackLink(track); }, TrackButtons.Text, 32).Hint("Скопировать ссылку");
        var share = ShareTrackButton(() => row.Track, TrackButtons.Text, 32);
        var more = TrackButtons.Icon(Icons.View("dots-three", 20), () => { }, TrackButtons.Text, 32).Hint("Ещё");
        more.Click += () => PopupAppearance.MenuFor(row.Root).Toggle(more);
        var extras = new StackPanel().Horizontal().Spacing(4).Children(repost, share, copy).IsVisible(row.Root.ActualWidth >= CompactTrackTail.FullActionsThreshold);
        row.Actions.Content = new StackPanel().Horizontal().Spacing(4).Right().CenterVertical().Children(extras, more);
        row.ExtraActions = extras; row.More = more; row.Repost = repost; row.RepostIcon = repostIcon;
        row.ActionButtons = [repost, share, copy, more];
        foreach (var button in row.ActionButtons) TrackCompactActionFocus(row, button);
    }

    private void TrackCompactActionFocus(CompactTrackRow row, Button button)
    {
        KeyboardFocusFeedback.Attach(button);
        button.GotFocus += () => RefreshCompactTrackRow(row);
        button.LostFocus += () => Application.Current.Dispatcher?.BeginInvoke(() => RefreshCompactTrackRow(row));
        button.MouseDown += _ => RefreshCompactTrackRow(row);
        button.MouseUp += _ => RefreshCompactTrackRow(row);
        button.KeyDown += _ => RefreshCompactTrackRow(row);
    }
}
