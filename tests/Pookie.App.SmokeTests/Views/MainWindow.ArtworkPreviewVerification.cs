using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyArtworkPreviewUiAsync()
    {
        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive);
        Button Trigger(Image cover, FrameworkElement root)
        {
            Button? result = null;
            VisualTree.Visit(root, child => { if (child is Button button && button.IsAncestorOf(cover)) result = button; });
            return result ?? throw new InvalidOperationException("Artwork is not a keyboard-accessible button.");
        }
        async Task Open(Image cover, FrameworkElement root, string title)
        {
            var source = cover.Source;
            var trigger = Trigger(cover, root);
            var audio = (paused, videoMusicGain, videoMusicPaused, videoMusicGeneration);
            trigger.Focus();
            RouteWaveformClick(trigger.Bounds.Center);
            await WaitForLikedLayoutAsync(() => chatVideoOpen.Value && chatVideoSurface.Reveal > .999);
            if (!chatVideoShowingImage || !chatVideoLayout.ImageMode || !ReferenceEquals(chatVideoImage.Source, source) ||
                chatVideoTitle.Text != title || chatVideoControls.IsVisible || chatVideoPrevious.IsVisible || chatVideoNext.IsVisible ||
                chatVideoSession != null || chatVideoSpeedOpen || chatVideoExpanded.Value ||
                audio != (paused, videoMusicGain, videoMusicPaused, videoMusicGeneration))
                throw new InvalidOperationException("Cover preview changed audio, showed video controls, or displayed the wrong artwork.");
            var picture = ChatVideoPictureBounds();
            if (picture.Width < 200 || picture.Height < 200 || !chatVideoSurface.Bounds.Contains(picture.Center))
                throw new InvalidOperationException("Artwork preview did not fit the overlay.");
            RouteShareKey(Key.Tab);
            if (Window.FocusManager.FocusedElement != chatVideoClose)
                throw new InvalidOperationException("Tab escaped the artwork preview.");
            CaptureUiPreview("cover-preview-" + (page.Value == Page.Track ? "track" : "playlist"), chatVideoSurface);
            if (previewArtwork?.Link is { } link)
                await VerifyArtworkMenuActionsAsync(artworkPreviewMenu, ChatVideoPictureBounds().Center, (ImageSource)source!, link);
            RouteContextRightClick(new(chatVideoSurface.Bounds.X + 4, chatVideoSurface.Bounds.Y + 4));
            if (artworkPreviewMenu.IsOpen) throw new InvalidOperationException("Right-click outside the picture opened its menu.");
        }
        async Task Closed(Image cover, FrameworkElement root, bool keyboard = false)
        {
            await chatVideoClosing;
            var trigger = Trigger(cover, root);
            if (chatVideoOpen.Value || chatVideoImage.Source != null || chatVideoSurface.IsVisible ||
                Window.FocusManager.FocusedElement != trigger)
                throw new InvalidOperationException("Cover preview did not close or restore focus to its trigger.");
            MoveActionPointer(new(4, 4));
            await Task.Delay(MaterialTooltipLayer.HoverDelayMs + 200);
            await WaitForLoginFrameAsync();
            var hints = Window.OverlayLayer.GetService<MaterialTooltipLayer>()!;
            if (KeyboardFocusFeedback.IsVisible(trigger) != keyboard ||
                !keyboard && hints.IsOpen && hints.Owner == trigger)
                throw new InvalidOperationException("Pointer-opened cover returned as keyboard focus and displayed a tooltip under a distant pointer.");
        }

        var trackTrigger = Trigger(trackDetailCover, trackDetailScroll);
        var source = trackDetailCover.Source;
        trackDetailCover.Source = Icons.Source(Icons.MissingTrackArtwork);
        RouteWaveformClick(trackTrigger.Bounds.Center);
        if (chatVideoOpen.Value) throw new InvalidOperationException("Placeholder cover opened a preview.");
        trackDetailCover.Source = source;
        RouteContextRightClick(trackTrigger.Bounds.Center);
        await WaitForLikedLayoutAsync(() => trackCoverMenu.Reveal > .999);
        if (chatVideoOpen.Value) throw new InvalidOperationException("Right-click opened both the image and its menu.");
        trackCoverMenu.Close(true);
        await VerifyArtworkMenuActionsAsync(trackCoverMenu, trackTrigger.Bounds.Center, (ImageSource)source!,
            new Uri(trackDetailState!.Track.ArtworkUrl!));
        await Open(trackDetailCover, trackDetailScroll, trackDetailState!.Track.Title);
        RouteShareKey(Key.Escape);
        await Closed(trackDetailCover, trackDetailScroll);
        await Open(trackDetailCover, trackDetailScroll, trackDetailState!.Track.Title);
        RouteWaveformClick(chatVideoClose.Bounds.Center);
        await Closed(trackDetailCover, trackDetailScroll);
        // A fresh pointer hover still works after the restored focus is cleared.
        MoveActionPointer(trackTrigger.Bounds.Center);
        var coverHints = Window.OverlayLayer.GetService<MaterialTooltipLayer>()!;
        await WaitForLikedLayoutAsync(() => coverHints.IsOpen && coverHints.Owner == trackTrigger);
        MoveActionPointer(new(4, 4));
        await WaitForLikedLayoutAsync(() => !coverHints.IsOpen);
        // A genuine keyboard opening must retain its keyboard focus feedback.
        trackTrigger.Focus();
        RouteSettingsKey(Key.Enter);
        await WaitForLikedLayoutAsync(() => chatVideoOpen.Value && chatVideoSurface.Reveal > .999);
        RouteShareKey(Key.Escape);
        await Closed(trackDetailCover, trackDetailScroll, keyboard: true);

        var transport = new CollectionFixtureTransport();
        api.BrowserTransport = transport;
        var item = LibraryData.FromPlaylist(transport.Playlist);
        collectionImages[CollectionArtworkUrl(item)!] = (ImageSource)source!;
        await OpenLibraryItemAsync(item);
        await WaitForLikedLayoutAsync(() => collectionCover.Source is ImageSource && collectionCover.ActualWidth > 100);
        var collectionTrigger = Trigger(collectionCover, collectionHero);
        await VerifyArtworkMenuActionsAsync(collectionCoverMenu, collectionTrigger.Bounds.Center,
            (ImageSource)collectionCover.Source!, new Uri(CollectionArtworkUrl(item)!));
        await Open(collectionCover, collectionHero, transport.Playlist.Title);
        // Clicking the image itself keeps it open; clicking outside dismisses it.
        RouteWaveformClick(ChatVideoPictureBounds().Center);
        if (!chatVideoOpen.Value) throw new InvalidOperationException("Clicking the picture dismissed the cover.");
        RouteWaveformClick(new(chatVideoSurface.Bounds.X + 4, chatVideoSurface.Bounds.Y + 4));
        await Closed(collectionCover, collectionHero);

        // Exercise the same decoder/overlay path used by chat, after image mode.
        using var fixture = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media.chat-video.mp4")!;
        using var video = new MemoryStream();
        await fixture.CopyToAsync(video);
        await OpenChatVideoAsync(new ChatAttachment("clip.mp4", "video/mp4", video.ToArray()), "cover-preview-fixture", 0);
        await WaitForLikedLayoutAsync(() => chatVideoVersion > 0 && chatVideoSurface.Reveal > .999);
        if (chatVideoShowingImage || chatVideoLayout.ImageMode || !chatVideoControls.IsVisible || chatVideoSession == null)
            throw new InvalidOperationException("Opening video after a cover did not restore the decoder and controls.");
        // Replace an active video with a cover; the decoder must be released.
        OpenArtworkPreview((ImageSource)collectionCover.Source!, item.Title, Trigger(collectionCover, collectionHero));
        await chatVideoClosing;
        if (!chatVideoShowingImage || chatVideoSession != null || chatVideoControls.IsVisible)
            throw new InvalidOperationException("Replacing video with artwork retained the decoder or video controls.");
        RouteShareKey(Key.Escape);
        await Closed(collectionCover, collectionHero);
        Console.WriteLine("ARTWORK_PREVIEW_OK: track/playlist clicks, placeholders, context menu, image fit, unchanged audio, Tab/Escape/close/backdrop, focus restoration and video/image switching");
    }
}
