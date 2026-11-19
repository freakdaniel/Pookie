using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBlock mixLinkPlaceholder = null!;
    private bool mixShareLoading, mixLockPublic;
    private Task? mixPrivacyWrite;
    private bool shareActionsShown = true;
    private readonly AnimationClock shareActionsFade = PlaylistDialogContent.CreateResizeClock();

    private void SetMixLock(bool showOpen, bool animate)
    {
        if (mixLockPublic == showOpen) return;
        mixLockPublic = showOpen;
        foreach (var icon in new[] { mixLockClosed, mixLockOpen })
            icon.Transitions = animate ? [Transition.Create(UIElement.OpacityProperty, 160, Easing.CubicBezier(.77, 0, .175, 1))] : [];
        mixLockOpen.Opacity = showOpen ? 1 : 0;
        mixLockClosed.Opacity = showOpen ? 0 : 1;
    }

    private bool OwnsMix(SoundCloudPlaylist mix) => me is { Id: > 0 } user && mix.MadeFor?.Id == user.Id;

    private void RefreshMixShareControls(bool opening)
    {
        var mix = shareTarget?.Playlist;
        var system = mix is { IsSystemMix: true };
        mixLinkPlaceholder.IsVisible = system && shareUri == null;
        mixLinkPlaceholder.Text = mixShareLoading ? "Проверяем доступ…" : mix?.IsPublic == false
            ? "Публичная ссылка выключена" : "Ссылка недоступна";
        var canShare = !system || shareUri != null;
        var allowMessages = !system || me != null && api.Session != null;
        shareSectionTabs[1].IsEnabled = canShare && allowMessages && shareTab.Value != 1;
        // Keep the last confirmed visibility while a mix privacy request is pending.
        if (opening || !system || !mixShareLoading)
            SetShareActionsVisible(canShare, allowMessages, animate: system && !opening && shareDialog.IsOpen);
        shareSectionTabs[2].IsVisible = !system;
    }

    private void SetShareActionsVisible(bool show, bool allowMessages, bool animate)
    {
        var tab = shareSectionTabs[1];
        if (animate && shareActionsShown == show)
        {
            tab.IsVisible = allowMessages && shareDestinationsPanel.IsVisible;
            return;
        }
        shareActionsFade.Stop();
        shareActionsShown = show;
        var target = show ? 1d : 0d;
        var from = shareDestinationsPanel.Opacity;
        void SetOpacity(double value) => tab.Opacity = shareDestinationsPanel.Opacity = value;
        void SetVisible(bool visible)
        {
            shareDestinationsPanel.IsVisible = visible;
            tab.IsVisible = visible && allowMessages;
        }
        if (!animate || from == target)
        {
            SetOpacity(target);
            SetVisible(show);
            return;
        }

        SetVisible(true);
        shareActionsFade.TickCallback = t => SetOpacity(from + (target - from) * t);
        shareActionsFade.CompletedCallback = () =>
        {
            SetOpacity(target);
            SetVisible(show);
            FitShareContent();
        };
        shareActionsFade.Start();
    }

    private async Task LoadMixSharingAsync(string urn, long generation, CancellationToken token)
    {
        try
        {
            if (mixPrivacyWrite is { } pending)
            {
                // A reopened dialog must see the result of the already submitted change.
                try { await pending.WaitAsync(token); }
                catch (Exception) when (pending.IsFaulted) { }
            }
            var currentMix = await api.GetMixForSharingAsync(urn, token);
            if (disposed || generation != shareGeneration) return;
            shareTarget = LibraryData.FromPlaylist(currentMix);
            shareAuthor.Text = currentMix.MadeFor?.Username ?? currentMix.User?.Username ?? shareTarget.Subtitle;
            shareUri = PlaylistSharing.ShareUri(currentMix);
            mixShareLoading = false; RefreshPrivateShare(); RefreshShareLink();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (disposed || generation != shareGeneration) return;
            mixShareLoading = false;
            if (shareTarget?.Playlist is { } previous) shareTarget = shareTarget with { Playlist = previous with { IsPublic = null } };
            shareUri = null; RefreshPrivateShare(); RefreshShareLink(); SetShareMessage(FriendlyError(error));
        }
    }

    private async Task ChangeMixSharingAsync(bool selected)
    {
        if (shareAccessAction != (selected ? ShareAccessAction.PublishMix : ShareAccessAction.HideMix) ||
            !shareAccessButton.IsEnabled || mixPrivacyWrite != null || mixShareLoading || shareTarget?.Playlist is not { IsSystemMix: true } mix ||
            !OwnsMix(mix) || mix.IsPublic == selected || api.Session == null || api.Transport is not { } transport) return;
        shareAccessPopup?.Close();
        var generation = shareGeneration;
        var updated = mix with { IsPublic = selected };
        shareUri = null;
        mixShareLoading = true;
        RefreshPrivateShare(); RefreshShareLink();
        try
        {
            mixPrivacyWrite = transport.SetSystemPlaylistPublicAsync(mix.Urn, selected, lifetime.Token);
            await mixPrivacyWrite;
            if (disposed || generation != shareGeneration) return;
            shareTarget = LibraryData.FromPlaylist(updated);
            SetMixLock(selected, animate: true);
            shareUri = PlaylistSharing.ShareUri(updated);
            mixShareLoading = false;
            RefreshShareLink();
        }
        catch (Exception error)
        {
            if (disposed || generation != shareGeneration) return;
            shareTarget = LibraryData.FromPlaylist(mix);
            await LoadMixSharingAsync(mix.Urn, generation, shareRequest!.Token);
            if (!disposed && generation == shareGeneration) SetShareMessage(FriendlyError(error));
        }
        finally
        {
            mixPrivacyWrite = null;
            if (!disposed) RefreshPrivateShare();
        }
    }
}
