using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private AnimatedContextMenu trackCoverMenu = null!;
    private bool HasTrackCoverImage => trackDetailCover.Source is ImageSource &&
        !ReferenceEquals(trackDetailCover.Source, Icons.Source(Icons.MissingTrackArtwork));

    private void AttachTrackCoverMenu(FrameworkElement cover)
    {
        var menu = trackCoverMenu = PopupAppearance.CreateMenu();
        var image = PopupAppearance.MenuCommand("pookie.cover.copy-image", "Скопировать изображение", "copy");
        var link = PopupAppearance.MenuCommand("pookie.cover.copy-link", "Скопировать ссылку", "arrow-square-out");
        cover.Commands.Register(image, () => Run(CopyTrackCoverAsync), () => HasTrackCoverImage);
        cover.Commands.Register(link, () =>
        {
            if (HasTrackCoverImage && trackDetailState?.Track is { } track && TryArtworkUri(track.ArtworkUrl ?? track.User?.AvatarUrl, out var uri)) CopyText(uri.AbsoluteUri);
        }, () => HasTrackCoverImage && trackDetailState?.Track is { } track && TryArtworkUri(track.ArtworkUrl ?? track.User?.AvatarUrl, out _));
        menu.Item(image); menu.Item(link);
        cover.MouseDown += args =>
        {
            if (args.Button != MouseButton.Right) return;
            args.Handled = true;
            if (HasTrackCoverImage) menu.Toggle(cover, args.GetPosition(Window));
        };
    }

    private async Task CopyTrackCoverAsync()
    {
        if (!HasTrackCoverImage || trackDetailCover.Source is not ImageSource source) return;
        var width = source.PixelWidth; var height = source.PixelHeight;
        var pixels = await Task.Run(() =>
        {
            var buffer = new byte[checked(width * height * 4)];
            source.CopyPixels(buffer, width * 4); return buffer;
        }, lifetime.Token);
        if (disposed) return;
        status.Value = ImageClipboard.TrySet(pixels, width, height) ? "Изображение скопировано." : "Не удалось скопировать изображение.";
    }
}
