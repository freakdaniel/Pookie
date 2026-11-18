using Aprillz.MewUI;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySidebarPlaylistInteractionsAsync(SoundCloudUser user)
    {
        async Task<SidebarPlaylistRow> ShowRow()
        {
            await OpenUserProfileAsync(user);
            shellScroll.SetScrollOffsets(0, 400);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && userSidebarPlaylists.Count == 1 &&
                userSidebarPlaylists[0].Root.ActualWidth > 150 && userSidebarPlaylists[0].Cover.Source != null);
            return userSidebarPlaylists[0];
        }
        var row = await ShowRow();
        MoveActionPointer(new(2, 2));
        await WaitForLikedLayoutAsync(() => row.Overlay.Opacity < .001);
        MoveActionPointer(SettingsCenter(row.Cover));
        await WaitForLikedLayoutAsync(() => row.Overlay.Opacity > .999 && row.HoverFill.Strength > .999);
        if (row.HoverFill.CornerRadius != userSidebarRows[0].HoverFill.CornerRadius ||
            row.Cover.ActualWidth != userSidebarRows[0].Cover.ActualWidth || row.Overlay.Bounds != row.Cover.Bounds)
            throw new InvalidOperationException("Compact playlist diverged from track highlight/artwork geometry.");
        CaptureUiPreview("sidebar-playlist-hover");
        var titleColor = row.Title.Foreground; var authorColor = row.Author.Foreground;
        MoveActionPointer(SettingsCenter(row.Title));
        await WaitForLikedLayoutAsync(() => row.Title.Foreground != titleColor);
        MoveActionPointer(SettingsCenter(row.Author));
        await WaitForLikedLayoutAsync(() => row.Title.Foreground == titleColor && row.Author.Foreground != authorColor);
        var originalPlayer = player;
        try
        {
            player = null;
            var generation = navigationGeneration;
            RouteWaveformClick(SettingsCenter(row.Overlay.PlaybackButton!));
            await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == row.Item.Key);
            if (navigationGeneration != generation || page.Value != Page.UserProfile || playbackQueue.Snapshot.Source.Count != 19)
                throw new InvalidOperationException("Compact playlist Play navigated or queued only a preview.");
            current = playbackQueue.Current!.Track; isPlaying.Value = true; RefreshLikedPlayback();
            MoveActionPointer(new(2, 2));
            await WaitForLikedLayoutAsync(() => row.Overlay.Opacity > .999 && row.Overlay.ShowsPause);
            isPlaying.Value = false; RefreshLikedPlayback();
            if (row.Overlay.ShowsPause) throw new InvalidOperationException("Compact playlist retained pause after playback paused.");
            playbackQueue.Clear(); current = null; RefreshLikedPlayback();
            await WaitForLikedLayoutAsync(() => row.Overlay.Opacity < .001);
        }
        finally { player = originalPlayer; }
        RouteWaveformClick(SettingsCenter(row.Title));
        await WaitForLikedLayoutAsync(() => page.Value == Page.LibraryCollection && displayedCollection?.Playlist?.Id == row.Item.Playlist!.Id);
        row = await ShowRow();
        RouteWaveformClick(SettingsCenter(row.Author));
        await WaitForLikedLayoutAsync(() => page.Value == Page.UserProfile && userProfileState?.User.Id == row.Item.Playlist!.User!.Id);
        if (userProfileState!.User.Id == user.Id) throw new InvalidOperationException("Author click was swallowed by the playlist row.");
        await OpenUserProfileAsync(user);
        Console.WriteLine("SIDEBAR_PLAYLIST_UI_OK: matching rounded highlight, independent animated title/author links, full queue playback without navigation and synchronized play/pause overlay");
    }
}
