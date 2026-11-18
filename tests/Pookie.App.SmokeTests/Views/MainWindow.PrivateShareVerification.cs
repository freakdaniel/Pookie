using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPrivateShareAsync(LibraryItem source)
    {
        var previous = api.BrowserTransport;
        var previousClipboard = shareClipboard;
        var clipboard = new ShareClipboard(); shareClipboard = clipboard;
        var fixture = new PrivateShareFixture(source.Playlist! with { Sharing = "private", SecretToken = "s-old", User = me,
            PermalinkUrl = "https://soundcloud.com/fixture/sets/private" });
        api.BrowserTransport = fixture;
        try
        {
            PrepareShareWidgetFixture();
            var item = LibraryData.FromPlaylist(fixture.Playlist);
            SetCollectionPresentation(item); RefreshCollectionActions();
            if (collectionSocialActions.IsVisible || collectionLike.IsEnabled || collectionRepost.IsEnabled)
                throw new InvalidOperationException("Private playlist retained social actions.");
            await TogglePlaylistActionAsync(fixture.Playlist, false);
            await TogglePlaylistActionAsync(fixture.Playlist, true);
            OpenShare(item);
            await WaitForLikedLayoutAsync(() => !sharePrivateLoading && !shareDialog.Running && !shareContent.Resizing);
            if (!sharePrivatePanel.IsVisible || shareDestinationsPanel.IsVisible || !shareResetLink.IsVisible ||
                !shareLink.Text.EndsWith("/s-old") || !shareCopy.IsEnabled) throw new InvalidOperationException("Private share lost its secret link or owner actions.");
            CopyShareLink();
            if (clipboard.Text != shareLink.Text) throw new InvalidOperationException("Private copy used another address.");
            SelectShareTab(2, false);
            if (!shareEmbedCode.Text.Contains("secret_token%3Ds-old")) throw new InvalidOperationException("Widget lost the private token.");
            SelectShareTab(0, false);
            await WaitForLikedLayoutAsync(() => !shareContent.Resizing);
            if (sharePrivateHint.Text.EndsWith('.') || shareResetLink.Width != 48 ||
                !shareLinkGlass.IsAncestorOf(shareResetLink) || shareTimeAction.IsVisible)
                throw new InvalidOperationException("Private reset is not an icon inside the link field or the hint retained its final dot.");
            Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
            CaptureUiPreview("share-private-playlist");
            RouteWaveformClick(SettingsCenter(shareResetLink));
            if (!shareResetConfirmation || fixture.Writes != 0) throw new InvalidOperationException("Reset skipped confirmation.");
            await WaitForLikedLayoutAsync(() => shareResetPopup?.Reveal == 1 && shareResetCancel.IsFocused);
            var bodyBounds = shareContent.Bounds;
            RouteShareKey(Key.Tab);
            if (!shareResetConfirm.IsFocused) throw new InvalidOperationException("Reset popup lost its keyboard focus loop.");
            RouteShareKey(Key.Escape);
            if (shareResetPopup!.IsOpen || !shareDialog.IsOpen || fixture.Writes != 0)
                throw new InvalidOperationException("Escape reset the secret or closed the parent dialog.");
            await WaitForLikedLayoutAsync(() => !shareResetPopup.IsVisible);
            RouteWaveformClick(SettingsCenter(shareResetLink));
            await WaitForLikedLayoutAsync(() => shareResetPopup.Reveal == 1);
            if (shareContent.Bounds != bodyBounds) throw new InvalidOperationException("Reset popup resized its parent dialog.");
            CaptureUiPreview("share-private-confirmation");
            fixture.ResetHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RouteWaveformClick(SettingsCenter(shareResetConfirm));
            if (shareResetPopup!.IsOpen || shareCopy.IsEnabled || shareSend.IsEnabled || shareEmbedCopy.IsEnabled) throw new InvalidOperationException("Old secret remained shareable while resetting.");
            CloseShare(true); OpenShare(item);
            if (shareCopy.IsEnabled) throw new InvalidOperationException("Reopening exposed an old secret during reset.");
            fixture.ResetHold.SetResult();
            await WaitForLikedLayoutAsync(() => !sharePrivateLoading && !shareSecretResetting && shareCopy.IsEnabled);
            if (fixture.Writes != 1 || !shareLink.Text.EndsWith("/s-new")) throw new InvalidOperationException("Reset was replayed or reopening retained stale metadata.");
            fixture.Fail = true; shareResetConfirmation = true;
            await ResetShareSecretAsync();
            if (shareCopy.IsEnabled || shareEmbedCopy.IsEnabled || shareResetLink.IsEnabled) throw new InvalidOperationException("Uncertain reset re-enabled a stale link or another reset.");
            fixture.Fail = false;
            CloseShare(true); OpenShare(source);
            await WaitForLikedLayoutAsync(() => !shareDialog.Running);
            if (sharePrivatePanel.IsVisible || !shareDestinationsPanel.IsVisible) throw new InvalidOperationException("Public sharing retained private controls.");
            Console.WriteLine("PRIVATE_SHARE_UI_OK: hidden social actions, secret copy/embed, owner confirmation, reset/reopen race and uncertain response");
        }
        finally { CloseShare(true); api.BrowserTransport = previous; shareClipboard = previousClipboard; SetCollectionPresentation(source); RefreshCollectionActions(); }
    }

    private sealed class PrivateShareFixture(SoundCloudPlaylist playlist) : ISoundCloudBrowserTransport
    {
        internal SoundCloudPlaylist Playlist = playlist;
        internal int Writes;
        internal bool Fail;
        internal TaskCompletionSource? ResetHold;
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(JsonSerializer.SerializeToDocument(Playlist, SoundCloudJson.Default.SoundCloudPlaylist));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected like");
        public async Task<JsonDocument> EditPlaylistAsync(PlaylistEditRequest request, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (Fail) throw new HttpRequestException("Lost acknowledgement");
            if (ResetHold != null) await ResetHold.Task;
            Playlist = Playlist with { SecretToken = "s-new" };
            return JsonSerializer.SerializeToDocument(Playlist, SoundCloudJson.Default.SoundCloudPlaylist);
        }
    }
}
