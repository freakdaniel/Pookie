using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private StackPanel sharePrivatePanel = null!;
    private InAppPopup? shareResetPopup;
    private FrameworkElement shareDestinationsPanel = null!;
    private TextBlock sharePrivateHint = null!;
    private Button shareResetLink = null!, shareResetConfirm = null!, shareResetCancel = null!;
    private bool sharePrivateLoading, shareSecretResetting, shareResetConfirmation;
    private TaskCompletionSource? shareSecretResetCompletion;

    private Button ShareResetButton()
    {
        shareResetLink = TrackButtons.Icon(Icons.View("arrows-clockwise", 20), ToggleShareReset,
            TrackButtons.Text, 48).Hint("Сбросить секретную ссылку").IsVisible(false);
        ((MaterialTrackButton)shareResetLink).UseListStateLayer();
        return shareResetLink;
    }

    private FrameworkElement SharePrivateView()
    {
        sharePrivateHint = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap)
            .Text("Плейлист доступен всем, у кого есть эта секретная ссылка. Сбрось её, чтобы закрыть доступ по старой ссылке");
        return sharePrivatePanel = new StackPanel().Vertical().Spacing(10).IsVisible(false).Children(
            new StackPanel().Horizontal().Spacing(8).Children(Icons.View("lock-simple", 16, Muted),
                new TextBlock().Text("Секретная ссылка").SemiBold().FontSize(13)), sharePrivateHint);
    }

    private void ToggleShareReset()
    {
        if (shareResetPopup?.IsOpen == true) { shareResetPopup.Close(); return; }
        if (!shareDialog.IsOpen || !shareResetLink.IsVisible || !shareResetLink.IsEnabled) return;
        if (shareResetPopup == null)
        {
            shareResetCancel = TrackButtons.Action(new TextBlock().Text("Отмена").FontSize(13),
                () => shareResetPopup!.Close(), TrackButtons.Text);
            shareResetConfirm = TrackButtons.Action(new TextBlock().Text("Сбросить").FontSize(13).SemiBold().Foreground(TrackButtons.OnPrimary),
                () => Run(ResetShareSecretAsync), TrackButtons.Filled);
            var card = new GlassCard { HoverEnabled = false }.Width(320).Background(PopupAppearance.Surface).BorderThickness(1)
                .BorderBrush(TrackButtons.TonalSurface).CornerRadius(20).Padding(20).Child(
                    new StackPanel().Vertical().Spacing(16).Children(
                        new TextBlock().Text("Сбросить секретную ссылку?").FontSize(16).SemiBold().TextWrapping(TextWrapping.Wrap),
                        new TextBlock().Text("Старая ссылка перестанет работать во всех сообщениях и виджетах. Доступ останется только по новой ссылке")
                            .FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap),
                        new StackPanel().Horizontal().Spacing(8).Right().Children(shareResetCancel, shareResetConfirm)));
            shareResetPopup = new InAppPopup { Dismissing = () => shareResetConfirmation = false };
            shareResetPopup.InitializePopup(card, () => shareResetLink.Bounds, () => MenuPlacement.Below, alignCenter: true);
        }
        shareResetConfirmation = true;
        shareResetPopup.ShowPopup(shareResetLink, shareResetCancel);
    }

    private bool HandleShareResetKey(KeyEventArgs e)
    {
        if (shareResetPopup?.IsOpen != true) return false;
        if (e.Key == Key.Escape) { shareResetPopup.Close(); e.Handled = true; }
        else if (e.Key == Key.Tab)
        {
            (shareResetCancel.IsFocused ? shareResetConfirm : shareResetCancel).Focus(); e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (shareResetConfirm.IsFocused) Run(ResetShareSecretAsync); else shareResetPopup.Close();
            e.Handled = true;
        }
        return true;
    }

    private void RefreshPrivateShare()
    {
        if (sharePrivatePanel == null) return;
        var playlist = shareTarget?.Playlist;
        var isPrivate = playlist?.IsPrivate == true;
        sharePrivatePanel.IsVisible = isPrivate;
        shareDestinationsPanel.IsVisible = !isPrivate;
        var owner = isPrivate && me != null && playlist?.User?.Id == me.Id && api.Session != null && !demo;
        shareResetLink.IsVisible = owner;
        shareResetLink.IsEnabled = !sharePrivateLoading && !shareSecretResetting && shareUri != null;
        if (!owner) shareResetPopup?.Close(true, false);
        FitShareContent();
    }

    private async Task ResolveSharePlaylistAsync(SoundCloudPlaylist playlist, long generation, CancellationToken token)
    {
        try
        {
            if (shareSecretResetCompletion is { } pendingReset) await pendingReset.Task.WaitAsync(token);
            var resolved = await api.GetPlaylistForSharingAsync(playlist.Id, token, playlist.User?.Id == me?.Id ? null : PlaylistSharing.Token(playlist));
            if (disposed || generation != shareGeneration) return;
            shareTarget = LibraryData.FromPlaylist(resolved); shareUri = PlaylistSharing.ShareUri(resolved);
            sharePrivateLoading = false; RefreshPrivateShare(); RefreshShareLink();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (disposed || generation != shareGeneration) return;
            sharePrivateLoading = false; RefreshPrivateShare(); RefreshShareLink(); SetShareMessage(FriendlyError(error));
        }
    }

    private async Task ResetShareSecretAsync()
    {
        if (shareSecretResetting || sharePrivateLoading || !shareResetConfirmation ||
            shareTarget?.Playlist is not { IsPrivate: true } playlist || me is not { } user ||
            playlist.User?.Id != user.Id || api.Session is not { } session || demo) return;
        shareResetPopup?.Close();
        var generation = shareGeneration;
        bool Current() => !disposed && generation == shareGeneration && api.Session == session;
        shareSecretResetting = true; shareSecretResetCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously); shareUri = null;
        RefreshShareLink(); RefreshPrivateShare(); SetShareMessage("Обновляем секретную ссылку…");
        try
        {
            // A submitted reset must not be replayed or cancelled by closing the dialog.
            var updated = await api.ResetPlaylistSecretAsync(user.Id, playlist.Id, lifetime.Token);
            if (!disposed && api.Session == session) InvalidatePlaylistLibrary(playlist.Id);
            if (!Current()) return;
            shareTarget = LibraryData.FromPlaylist(updated); shareUri = PlaylistSharing.ShareUri(updated);
            shareResetConfirmation = false; RefreshShareLink(); SetShareMessage("Ссылка обновлена. Старая ссылка больше не работает.");
        }
        catch (Exception error)
        {
            if (!disposed && api.Session == session) InvalidatePlaylistLibrary(playlist.Id);
            if (Current())
            {
                // The server may already have reset it. Never re-enable a stale link.
                RefreshShareLink();
                SetShareMessage(FriendlyError(error) + " Открой окно заново, чтобы проверить актуальную ссылку.");
                shareResetConfirmation = false;
            }
        }
        finally
        {
            shareSecretResetting = false;
            var completion = shareSecretResetCompletion; shareSecretResetCompletion = null; completion?.TrySetResult();
            if (!disposed) RefreshPrivateShare();
        }
    }
}
