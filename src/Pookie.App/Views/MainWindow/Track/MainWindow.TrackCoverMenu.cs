using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private AnimatedContextMenu trackCoverMenu = null!, collectionCoverMenu = null!, artworkPreviewMenu = null!;
    private sealed record ArtworkMenuImage(ImageSource Source, string Title, Uri? Link);
    private bool HasTrackCoverImage => trackDetailCover.Source is ImageSource;
    private Func<SaveFileDialogOptions, CancellationToken, Task<string?>> artworkSaveDialog = FileDialog.SaveFileAsync;

    private AnimatedContextMenu AttachArtworkMenu(FrameworkElement owner, Func<ArtworkMenuImage?> current,
        Func<Point, bool>? contains = null)
    {
        var menu = PopupAppearance.CreateMenu();
        ArtworkMenuImage? selected = null;
        var image = PopupAppearance.MenuCommand("pookie.cover.copy-image", "Скопировать изображение", "copy");
        var link = PopupAppearance.MenuCommand("pookie.cover.copy-link", "Скопировать ссылку", "link");
        var save = PopupAppearance.MenuCommand("pookie.cover.save-as", "Сохранить как", "download-simple");
        owner.Commands.Register(image, () => { if (selected is { } artwork) Run(() => CopyArtworkAsync(artwork.Source)); }, () => selected != null);
        owner.Commands.Register(link, () => { if (selected?.Link is { } uri) CopyText(uri.AbsoluteUri); }, () => selected?.Link != null);
        owner.Commands.Register(save, () => { if (selected is { } artwork) Run(() => SaveArtworkAsAsync(artwork)); }, () => selected != null);
        menu.Item(image); menu.Item(link); menu.Item(save);
        owner.MouseDown += args =>
        {
            if (args.Button != MouseButton.Right) return;
            var point = args.GetPosition(Window);
            if (contains != null && !contains(point)) return;
            args.Handled = true;
            selected = current();
            if (selected != null) menu.Toggle(owner, point);
        };
        return menu;
    }

    private Task<byte[]> EncodeArtworkPngAsync(ImageSource source) => Task.Run(() =>
    {
        var width = source.PixelWidth; var height = source.PixelHeight;
        var pixels = new byte[checked(width * height * 4)];
        source.CopyPixels(pixels, width * 4);
        return ImageClipboard.EncodePng(pixels, width, height);
    }, lifetime.Token);

    private async Task SaveArtworkAsAsync(ArtworkMenuImage artwork)
    {
        var name = new string(artwork.Title.Select(c => char.IsControl(c) || "/\\:*?\"<>|".Contains(c) ? '_' : c).Take(120).ToArray()).Trim(' ', '.');
        var destination = await artworkSaveDialog(new SaveFileDialogOptions
        {
            Owner = Window, Title = "Сохранить изображение", FileName = (name.Length == 0 ? "Обложка" : name) + ".png",
            DefaultExtension = "png", Filters = [new FileFilter("Изображение PNG", "*.png")]
        }, lifetime.Token);
        if (destination == null || disposed) return;
        var png = await EncodeArtworkPngAsync(artwork.Source);
        await File.WriteAllBytesAsync(destination, png, lifetime.Token);
        if (!disposed) status.Value = "Изображение сохранено.";
    }

    private async Task CopyArtworkAsync(ImageSource source)
    {
        var width = source.PixelWidth; var height = source.PixelHeight;
        if (OperatingSystem.IsLinux() && windowClipboard is { } clipboard)
        {
            var encoding = EncodeArtworkPngAsync(source);
            var copied = clipboard.TrySetPng(encoding);
            await encoding;
            if (!disposed) status.Value = copied ? "Изображение скопировано." : "Не удалось скопировать изображение.";
            return;
        }
        var pixels = await Task.Run(() =>
        {
            var buffer = new byte[checked(width * height * 4)];
            source.CopyPixels(buffer, width * 4); return buffer;
        }, lifetime.Token);
        if (disposed) return;
        status.Value = ImageClipboard.TrySet(pixels, width, height) ? "Изображение скопировано." : "Не удалось скопировать изображение.";
    }
}
