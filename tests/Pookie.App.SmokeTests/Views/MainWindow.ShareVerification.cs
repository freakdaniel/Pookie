using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySharePanelAsync(SoundCloudTrack track, LibraryItem playlist)
    {
        var previous = shareLink.ClipboardService;
        var clipboard = new ShareClipboard(); shareLink.ClipboardService = clipboard;
        try
        {
            MoveActionPointer(new(2, 2)); collectionFilter.Focus();
            var trigger = Window.FocusManager.FocusedElement;
            track = track with { PermalinkUrl = "https://soundcloud.com/fixture/shared-track/s-secret?secret_token=s-private", Duration = 180000 };
            ShareTrack(track);
            await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running && sharePreview.Opacity == 1 && shareCopy.ActualWidth > 0);
            if (!shareLink.IsReadOnly || !shareLink.IsFocused || !shareTimeRow.IsVisible || shareLink.Text != track.PermalinkUrl)
                throw new InvalidOperationException("Share panel lost its target, private link, initial focus or read-only URL.");
            shareAt.IsChecked = true; shareTime.Text = "1:23";
            if (!shareLink.Text.EndsWith("#t=1:23", StringComparison.Ordinal) || !shareLink.Text.Contains("secret_token=s-private"))
                throw new InvalidOperationException("Timed sharing removed the private access token or failed to add the timestamp.");
            RouteWaveformClick(SettingsCenter(shareCopy));
            if (clipboard.Text != shareLink.Text || shareCopyLabel.Text != "Скопировано")
                throw new InvalidOperationException("Share copy button did not write the displayed link.");
            CaptureUiPreview("share-track");
            foreach (var invalid in new[] { "0:99", "1:60", "3:01", "-1:00", "999999999:00", "1:2:3:4", "" })
            {
                shareTime.Text = invalid;
                if (shareCopy.IsEnabled || shareDestinations.Any(button => button.IsEnabled))
                    throw new InvalidOperationException("An invalid share timestamp enabled copying or sending: " + invalid);
            }
            shareTime.Text = "0:00";
            if (!shareCopy.IsEnabled) throw new InvalidOperationException("Zero timestamp cannot be shared.");
            var escape = new KeyEventArgs(Key.Escape, 0); shareDialog.HandleKey(escape);
            await WaitForLikedLayoutAsync(() => !shareDialog.IsVisible);
            if (!escape.Handled || Window.FocusManager.FocusedElement != trigger)
                throw new InvalidOperationException("Dismissing Share failed to restore focus.");
            OpenShare(playlist);
            await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running);
            if (shareTimeRow.IsVisible || shareLink.Text.Contains("#t=") || shareTarget?.Playlist?.Id != playlist.Playlist?.Id)
                throw new InvalidOperationException("Playlist sharing retained the previous track's time or target.");
            CaptureUiPreview("share-playlist");
            CloseShare(); ShareTrack(track);
            await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running);
            if (shareTarget?.Track?.Id != track.Id || !shareContent.IsHitTestVisible)
                throw new InvalidOperationException("Reopening Share during dismissal left the old close callback active.");
            Console.WriteLine("SHARE_UI_OK: track/playlist modal, private access preserved, timestamp validation, real copy button with isolated clipboard, Escape/focus return and reopen during dismissal");
        }
        finally { CloseShare(true); shareLink.ClipboardService = previous; }
    }

    private sealed class ShareClipboard : IClipboardService
    {
        internal string Text = "";
        public bool TrySetText(string text) { Text = text; return true; }
        public bool TryGetText(out string text) { text = Text; return true; }
    }
}
