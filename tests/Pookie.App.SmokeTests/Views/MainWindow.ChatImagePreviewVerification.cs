using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatImagePreviewAsync(ChatFixtureTransport transport, ChatFixtureMediaHandler media)
    {
        var body = media.Add(new(90, 42, "Фото для просмотра", [
            new("original.png", "image/png", PhotoFixture(960)),
            new("second.png", "image/png", PhotoFixture(240))]));
        transport.AddIncoming(body); await RefreshChatsAsync(); RequestChatBottom();
        await WaitForLikedLayoutAsync(() => !chatScrollBottom && ChatRows.Any(row => row.Bound && row.Message?.Body == body &&
            row.MediaResolved && !row.Content.IsResizing && row.Attachments.FindVisualChild<ChatPhotoFrame>() is ChatPhotoFrame { Picture.Source: ImageSource }));
        var row = ChatRows.First(row => row.Bound && row.Message?.Body == body);
        var image = (ChatPhotoFrame)row.Attachments.Children[0].FindVisualChild<ChatPhotoFrame>()!;
        // Reveal the first photo of a multi-image message without moving the account's history.
        var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
        BeginChatUserScroll(); CancelChatScrollRestoration();
        viewer.SetScrollOffsets(0, Math.Max(0, viewer.VerticalOffset + image.Bounds.Y - viewer.Bounds.Y));
        await WaitForLoginFrameAsync();
        var audio = (paused, videoMusicGain, videoMusicPaused, videoMusicGeneration);
        RouteWaveformClick(image.Bounds.Center);
        await WaitForLikedLayoutAsync(() => chatVideoShowingImage && chatVideoSurface.Reveal > .999);
        if (chatVideoCurrent != (row.Message!.Id, 0) || chatVideoImage.Source is not ImageSource { PixelWidth: 960, PixelHeight: 960 } ||
            chatVideoSession != null || chatVideoControls.IsVisible || chatVideoTitle.Text != "original.png" ||
            audio != (paused, videoMusicGain, videoMusicPaused, videoMusicGeneration) || !chatVideoNext.IsVisible)
            throw new InvalidOperationException("Chat photo did not open its original pixels in the shared image viewer or changed audio.");
        CaptureUiPreview("chats-image-preview", chatVideoSurface);
        await VerifyArtworkMenuActionsAsync(artworkPreviewMenu, ChatVideoPictureBounds().Center, (ImageSource)chatVideoImage.Source!, null);
        await NavigateChatVideoAsync(1);
        if (chatVideoCurrent != (row.Message.Id, 1) || chatVideoImage.Source is not ImageSource { PixelWidth: 240 } || chatVideoTitle.Text != "second.png")
            throw new InvalidOperationException("Chat gallery did not navigate to the next image attachment.");
        await NavigateChatVideoAsync(-1);
        RouteShareKey(Key.Escape); await chatVideoClosing;
        if (chatVideoOpen.Value || chatVideoImage.Source != null || chatVideoSurface.IsVisible ||
            Window.FocusManager.FocusedElement != image)
            throw new InvalidOperationException("Closing chat image preview retained pixels or lost the thumbnail focus.");
        image.Focus(); RouteSettingsKey(Key.Enter);
        await WaitForLikedLayoutAsync(() => chatVideoShowingImage && chatVideoSurface.Reveal > .999);
        RouteWaveformClick(chatVideoClose.Bounds.Center); await chatVideoClosing;
        if (chatVideoOpen.Value || chatVideoImage.Source != null)
            throw new InvalidOperationException("Keyboard-opened chat image did not close with the close button.");
        Console.WriteLine("CHAT_IMAGE_PREVIEW_OK: original resolution, routed/keyboard opening, gallery navigation, shared save menu, unchanged audio and focus/resource cleanup");
        static byte[] PhotoFixture(int size)
        {
            var source = ImageSource.FromBytes(ArtworkFixtureBitmap(size));
            var pixels = new byte[size * size * 4]; source.CopyPixels(pixels, size * 4);
            return ImageClipboard.EncodePng(pixels, size, size);
        }
    }
}
