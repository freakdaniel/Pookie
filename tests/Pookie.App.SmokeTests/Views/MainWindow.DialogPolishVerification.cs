using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlaylistDialogPolishAsync(SoundCloudTrack track, CollectionFixtureTransport transport)
    {
        transport.PickerCount = 4; await OpenPlaylistPickerAsync(track);
        await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing && !playlistPickerLoading.Skeleton.IsVisible);
        ScrollViewer? scroll = null;
        VisualTree.Visit(playlistPickerLoading.Root, child => { if (child is ScrollViewer viewer) scroll = viewer; });
        if (scroll == null || SmoothScroll.Maximum(scroll) > .5) throw new InvalidOperationException("Four playlists have an unexpected resting scrollbar.");
        var viewport = scroll.ViewportHeight; var sampled = 0;
        void CheckViewport()
        {
            if (!playlistDialogPages.Animating) return;
            sampled++;
            if (playlistDialogPages.ContentOverlaps)
                throw new InvalidOperationException("The two dialog pages painted text/buttons simultaneously.");
            if (Math.Abs(scroll.ViewportHeight - viewport) > .5 || SmoothScroll.Maximum(scroll) > .5)
                throw new InvalidOperationException("Switching dialog pages created a transient playlist scrollbar.");
        }
        Window.FrameRendered += CheckViewport;
        try
        {
            ShowPlaylistCreation(true);
            await WaitForLikedLayoutAsync(() => !playlistDialogPages.Animating && !playlistDialogContent.Resizing);
            if (sampled < 3 || playlistCreateAction.IsVisible || playlistCreateAction.Progress != 0)
                throw new InvalidOperationException("Empty creation did not animate or retained its hidden submit action.");
            var header = new List<string>();
            VisualTree.Visit(playlistDialogContent, child => { if (child is TextBlock text) header.Add(text.Text); });
            if (!header.Contains("Добавить") || !header.Contains("Создать") || header.Contains("Создать плейлист") || header.Contains("Добавить в плейлист"))
                throw new InvalidOperationException("Playlist dialog retained its old heading or lost its shared section tabs.");
            VisualTree.Visit(playlistCreateForm, child =>
            { if (child is TextBlock { Text: "Доступ" }) throw new InvalidOperationException("The redundant privacy heading remains."); });
            var icons = new HashSet<object>();
            VisualTree.Visit(playlistCreatePrivacy, child => { if (child is Image { Source: { } source }) icons.Add(source); });
            bool HasIcon(string name) => icons.Contains(Icons.Source(name, Color.White)) || icons.Contains(Icons.Source(name, Color.FromRgb(166, 166, 173)));
            if (!HasIcon("globe") || !HasIcon("lock-simple"))
                throw new InvalidOperationException("The privacy selector lost its globe/lock icons.");
            var left = playlistCreatePrivacy.SelectionBounds.X;
            var reflections = playlistCreatePrivacy.ReflectionRasterizations;
            if (reflections == 0) throw new InvalidOperationException("Playlist privacy lost its glass pill.");
            SetPlaylistPrivacy(true);
            await WaitForLikedLayoutAsync(() => playlistCreatePrivacy.Animating && playlistCreatePrivacy.SelectionBounds.X > left + 1 && playlistCreatePrivacy.SelectionBounds.Height < 40);
            CaptureUiPreview("dialog-privacy-moving");
            await WaitForLikedLayoutAsync(() => !playlistCreatePrivacy.Animating);
            if (playlistCreatePrivacy.SelectionBounds.X <= left || playlistCreatePublic.StyleName != TrackButtons.Text || playlistCreatePrivate.StyleName != TrackButtons.Text ||
                playlistCreatePrivacy.ReflectionRasterizations != reflections)
                throw new InvalidOperationException("Privacy lost its liquid glass pill or rebuilt its optics during switching.");
            CaptureUiPreview("dialog-create-compact");
            ShowPlaylistCreation(false);
            await WaitForLikedLayoutAsync(() => !playlistDialogPages.Animating && !playlistDialogContent.Resizing);
        }
        finally { Window.FrameRendered -= CheckViewport; }
        if (((MaterialTrackButton)playlistPickerClose).HasOverlayBlur || playlistPickerClose.StyleName != TrackButtons.Glass ||
            playlistPickerClose.Background.R != 255 || playlistPickerClose.Background.A >= 100)
            throw new InvalidOperationException("Playlist close button lost its light glass finish or restored CPU backdrop captures.");
        transport.PickerCount = 2; await OpenPlaylistPickerAsync(track);
        await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing);
        Console.WriteLine("PLAYLIST_DIALOG_POLISH_OK: stable viewport through both transitions, measured compact form, no privacy heading, moving neutral pill/icons and light glass close without CPU backdrop captures");
    }

}
