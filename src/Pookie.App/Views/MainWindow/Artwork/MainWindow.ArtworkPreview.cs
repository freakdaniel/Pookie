using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ArtworkMenuImage? previewArtwork;

    private Button ArtworkPreviewButton(Image cover, Func<string?> title, Func<string?> url, out AnimatedContextMenu menu)
    {
        var button = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .CornerRadius(10).Content(new Border().CornerRadius(10).ClipToBounds().Child(ArtworkLayer(cover)))
            .Hint("Посмотреть обложку");
        button.Cursor = CursorType.Hand;
        ArtworkMenuImage? Current() => cover.Source is ImageSource source
            ? new(source, title() ?? "Обложка", TryArtworkUri(url(), out var uri) ? uri : null) : null;
        menu = AttachArtworkMenu(button, Current);
        button.OnClick(() =>
        {
            // Placeholders and unfinished downloads are not full-size artwork.
            if (Current() is { } artwork) OpenArtworkPreview(artwork.Source, artwork.Title, button, artwork.Link);
        });
        return button;
    }

    private void OpenArtworkPreview(ImageSource source, string title, UIElement returnFocus, Uri? link = null)
    {
        var keyboard = returnFocus is Control control && KeyboardFocusFeedback.IsVisible(control);
        var replacingVideo = chatVideoOpen.Value && !chatVideoShowingImage;
        CloseChatVideo(true, replacing: true);
        if (replacingVideo) RestoreMusicAfterChatVideo();
        chatVideoShowingImage = true; chatVideoReturnFocus = returnFocus;
        previewArtwork = new(source, title, link);
        chatVideoReturnFocusKeyboard = keyboard;
        chatVideoLayout.ImageMode = true; chatVideoControls.IsVisible = false;
        chatVideoPrevious.IsVisible = chatVideoNext.IsVisible = false;
        SetChatVideoExpanded(false, immediate: true);
        chatVideoImage.Source = source;
        chatVideoTitle.Text = title; chatVideoStatus.Text = "";
        chatVideoOpen.Value = true;
        _ = chatVideoSurface.SetOpen(true);
        chatVideoSurface.InvalidateMeasure(); Window.PerformLayout();
        Window.InvalidateVisual(); chatVideoClose.Focus();
    }
}
