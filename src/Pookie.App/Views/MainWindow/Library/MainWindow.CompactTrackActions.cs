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
        var repostIcon = Icons.View("repeat", 18, Muted).Center();
        var repost = TrackButtons.Icon(repostIcon, () => { if (row.Track is { } track) Run(() => ToggleTrackRepostAsync(track)); }, TrackButtons.Text, 32).Hint("Сделать репост");
        var copy = TrackButtons.Icon(Icons.View("copy", 18), () => { if (row.Track is { } track) CopyTrackLink(track); }, TrackButtons.Text, 32).Hint("Скопировать ссылку");
        var share = TrackButtons.Icon(Icons.View("arrow-square-out", 18), () => { }, TrackButtons.Text, 32).Hint("Поделиться");
        var menu = PopupAppearance.CreateMenu();
        var copyLink = new Command("pookie.track.share.copy", "Скопировать ссылку");
        var open = new Command("pookie.track.share.open", "Открыть в SoundCloud");
        share.Commands.Register(copyLink, () => { if (row.Track is { } track) CopyTrackLink(track); }, () => row.Track != null);
        share.Commands.Register(open, () => OpenSoundCloudPage(row.Track?.PermalinkUrl), () => TrackLink(row.Track?.PermalinkUrl) != null);
        menu.Item(copyLink).Item(open); PopupAppearance.Attach(share, menu);
        share.Click += () => menu.Show(share);
        var more = TrackButtons.Icon(Icons.View("dots-three", 20), () => { }, TrackButtons.Text, 32).Hint("Ещё");
        more.Click += () => PopupAppearance.MenuFor(row.Root).Show(more);
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
