using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private StackPanel sharePrivatePanel = null!;
    private InAppPopup? shareAccessPopup;
    private FrameworkElement shareDestinationsPanel = null!;
    private TextBlock sharePrivateTitle = null!, sharePrivateHint = null!;
    private Image sharePrivateIcon = null!;
    private Button shareAccessButton = null!, shareAccessConfirm = null!, shareAccessCancel = null!;
    private enum ShareAccessAction { None, ResetSecret, PublishMix, HideMix }
    private ShareAccessAction shareAccessAction;
    private TextBlock shareAccessTitle = null!, shareAccessMessage = null!, shareAccessLabel = null!;
    private Image shareResetIcon = null!, mixLockClosed = null!, mixLockOpen = null!;
    private bool sharePrivateLoading, shareSecretResetting;
    private TaskCompletionSource? shareSecretResetCompletion;

    private Button ShareAccessButton()
    {
        var icons = new Grid().Columns("*").Rows("*").Width(20).Height(20).IsHitTestVisible(false).Children(
            shareResetIcon = Icons.View("arrows-clockwise", 20).Column(0).Row(0),
            mixLockClosed = Icons.View("lock-simple", 20).Column(0).Row(0).IsVisible(false),
            mixLockOpen = Icons.View("lock-simple-open", 20).Column(0).Row(0).Opacity(0).IsVisible(false));
        shareAccessButton = TrackButtons.Icon(icons, ToggleShareAccessMenu,
            TrackButtons.Text, 48).Hint("Сбросить секретную ссылку").IsVisible(false);
        ((MaterialTrackButton)shareAccessButton).UseListStateLayer();
        return shareAccessButton;
    }

    private FrameworkElement SharePrivateView()
    {
        sharePrivateHint = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        return sharePrivatePanel = new StackPanel().Vertical().Spacing(10).IsVisible(false).Children(
            new StackPanel().Horizontal().Spacing(8).Children(sharePrivateIcon = Icons.View("lock-simple", 16, Muted),
                sharePrivateTitle = new TextBlock().SemiBold().FontSize(13)), sharePrivateHint);
    }

    private void ToggleShareAccessMenu()
    {
        if (shareAccessPopup?.IsOpen == true) { shareAccessPopup.Close(); return; }
        if (!shareDialog.IsOpen || !shareAccessButton.IsVisible || !shareAccessButton.IsEnabled) return;
        if (shareAccessPopup == null)
        {
            shareAccessCancel = TrackButtons.Action(new TextBlock().Text("Отмена").FontSize(13),
                () => shareAccessPopup!.Close(), TrackButtons.Text);
            shareAccessConfirm = TrackButtons.Action(shareAccessLabel = new TextBlock().FontSize(13).SemiBold().Foreground(TrackButtons.OnPrimary),
                ConfirmShareAccess, TrackButtons.Filled);
            var card = new GlassCard { HoverEnabled = false }.Width(320).Background(PopupAppearance.Surface).BorderThickness(1)
                .BorderBrush(TrackButtons.TonalSurface).CornerRadius(20).Padding(20).Child(
                    new StackPanel().Vertical().Spacing(16).Children(
                        shareAccessTitle = new TextBlock().FontSize(16).SemiBold().TextWrapping(TextWrapping.Wrap),
                        shareAccessMessage = new TextBlock()
                            .FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap),
                        new StackPanel().Horizontal().Spacing(8).Right().Children(shareAccessCancel, shareAccessConfirm)));
            shareAccessPopup = new InAppPopup { Dismissing = () => shareAccessAction = ShareAccessAction.None };
            shareAccessPopup.InitializePopup(card, () => shareAccessButton.Bounds, () => MenuPlacement.Below, alignCenter: true);
        }
        shareAccessAction = shareTarget?.Playlist is { IsSystemMix: true } mix
            ? mix.IsPublic == true ? ShareAccessAction.HideMix : ShareAccessAction.PublishMix
            : ShareAccessAction.ResetSecret;
        (shareAccessTitle.Text, shareAccessMessage.Text, shareAccessLabel.Text) = shareAccessAction switch
        {
            ShareAccessAction.PublishMix => ("Открыть публичную ссылку?", "Любой сможет найти и послушать этот микс.", "Открыть"),
            ShareAccessAction.HideMix => ("Закрыть публичную ссылку?", "Микс снова будет доступен только тебе. Ранее отправленная ссылка перестанет открывать его для других.", "Закрыть ссылку"),
            _ => ("Сбросить секретную ссылку?", "Старая ссылка перестанет работать во всех сообщениях и виджетах. Доступ останется только по новой ссылке", "Сбросить")
        };
        shareAccessPopup.ShowPopup(shareAccessButton, shareAccessCancel);
    }

    private void ConfirmShareAccess()
    {
        if (shareAccessPopup?.IsOpen != true || !shareAccessButton.IsEnabled) return;
        if (shareAccessAction == ShareAccessAction.ResetSecret) Run(ResetShareSecretAsync);
        else if (shareAccessAction is ShareAccessAction.PublishMix or ShareAccessAction.HideMix)
            Run(() => ChangeMixSharingAsync(shareAccessAction == ShareAccessAction.PublishMix));
    }

    private bool HandleShareAccessKey(KeyEventArgs e)
    {
        if (shareAccessPopup?.IsOpen != true) return false;
        if (e.Key == Key.Escape) { shareAccessPopup.Close(); e.Handled = true; }
        else if (e.Key == Key.Tab)
        {
            (shareAccessCancel.IsFocused ? shareAccessConfirm : shareAccessCancel).Focus(); e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (shareAccessConfirm.IsFocused) ConfirmShareAccess(); else shareAccessPopup.Close();
            e.Handled = true;
        }
        return true;
    }

    private void RefreshPrivateShare(bool opening = false)
    {
        if (sharePrivatePanel == null) return;
        var playlist = shareTarget?.Playlist;
        var mix = IsSystemCollection(shareTarget);
        var isPrivate = !mix && playlist?.IsPrivate == true;
        sharePrivatePanel.IsVisible = isPrivate || mix && playlist?.IsPublic != null;
        if (sharePrivatePanel.IsVisible)
        {
            var publicMix = mix && playlist?.IsPublic == true;
            sharePrivateIcon.Source = Icons.Source(publicMix ? "globe" : "lock-simple", Muted);
            sharePrivateTitle.Text = mix ? publicMix ? "Публичный микс" : "Приватный микс" : "Секретная ссылка";
            sharePrivateHint.Text = !mix ? "Плейлист доступен всем, у кого есть эта секретная ссылка. Сбрось её, чтобы закрыть доступ по старой ссылке" :
                publicMix ? "Любой может найти и послушать этот микс" :
                OwnsMix(playlist!) ? "Включи публичную ссылку, чтобы другие могли найти и послушать этот микс" :
                "Микс доступен только слушателю, для которого он создан";
        }
        RefreshMixShareControls(opening);
        shareResetIcon.IsVisible = !mix;
        mixLockClosed.IsVisible = mixLockOpen.IsVisible = mix;
        if (mix) SetMixLock(playlist!.IsPublic == true, animate: false);
        var owner = isPrivate && me != null && playlist?.User?.Id == me.Id && api.Session != null && !demo;
        shareAccessButton.IsVisible = mix ? OwnsMix(playlist!) : owner;
        shareAccessButton.IsEnabled = shareAccessButton.IsVisible && (mix
            ? playlist?.IsPublic != null && !mixShareLoading && mixPrivacyWrite == null && !demo &&
                api.Transport?.Supports(SoundCloudTransportCapabilities.SystemPlaylistPrivacy) == true
            : !sharePrivateLoading && !shareSecretResetting && shareUri != null);
        shareAccessButton.Hint(mix ? playlist?.IsPublic == true ? "Закрыть публичную ссылку" : "Открыть публичную ссылку" : "Сбросить секретную ссылку");
        if (!shareAccessButton.IsVisible) shareAccessPopup?.Close(true, false);
        FitShareContent();
    }

    private async Task<SoundCloudPlaylist> FetchPlaylistForSharingAsync(SoundCloudPlaylist playlist, CancellationToken token)
    {
        if (shareSecretResetCompletion is { } pendingReset) await pendingReset.Task.WaitAsync(token);
        var secret = playlist.User?.Id == me?.Id ? null : PlaylistSharing.Token(playlist);
        return await api.GetPlaylistForSharingAsync(playlist.Id, token, secret);
    }

    private async Task ResolveSharePlaylistAsync(SoundCloudPlaylist playlist, long generation, CancellationToken token)
    {
        try
        {
            var resolved = await FetchPlaylistForSharingAsync(playlist, token);
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
        if (shareSecretResetting || sharePrivateLoading || shareAccessAction != ShareAccessAction.ResetSecret ||
            shareTarget?.Playlist is not { IsPrivate: true } playlist || me is not { } user ||
            playlist.User?.Id != user.Id || api.Session is not { } session || demo) return;
        shareAccessPopup?.Close();
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
            shareAccessAction = ShareAccessAction.None; RefreshShareLink(); SetShareMessage("Ссылка обновлена. Старая ссылка больше не работает.");
        }
        catch (Exception error)
        {
            if (!disposed && api.Session == session) InvalidatePlaylistLibrary(playlist.Id);
            if (Current())
            {
                // The server may already have reset it. Never re-enable a stale link.
                RefreshShareLink();
                SetShareMessage(FriendlyError(error) + " Открой окно заново, чтобы проверить актуальную ссылку.");
                shareAccessAction = ShareAccessAction.None;
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
