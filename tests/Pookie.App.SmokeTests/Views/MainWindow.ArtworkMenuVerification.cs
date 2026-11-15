using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using StbImageSharp;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyArtworkMenuActionsAsync(AnimatedContextMenu menu, Point point, ImageSource source, Uri? link)
    {
        var directory = Directory.CreateTempSubdirectory("pookie-artwork-save-").FullName;
        var destination = Path.Combine(directory, "cover.png");
        var oldDialog = artworkSaveDialog;
        var oldClipboard = windowClipboard;
        using var clipboard = oldClipboard?.ForSelection(null, "POOKIE_ARTWORK_MENU_" + Guid.NewGuid().ToString("N"));
        if (clipboard != null) windowClipboard = clipboard;
        SaveFileDialogOptions? dialog = null;
        var requests = 0;
        artworkSaveDialog = (options, token) =>
        {
            token.ThrowIfCancellationRequested(); dialog = options; requests++;
            return Task.FromResult<string?>(requests == 1 ? destination : null);
        };
        Button Row(string text) => menu.Buttons.Single(button => (button.FindVisualChild<TextBlock>() as TextBlock)?.Text == text);
        async Task Show()
        {
            RouteContextRightClick(point);
            await WaitForLikedLayoutAsync(() => menu.Reveal > .999);
            if (menu.Model.Items.OfType<MenuItem>().Count() != 3 ||
                !Row("Скопировать изображение").IsEffectivelyEnabled || Row("Скопировать ссылку").IsEffectivelyEnabled != (link != null) ||
                !Row("Сохранить как").IsEffectivelyEnabled)
                throw new InvalidOperationException("Artwork menu is missing one of its three enabled actions.");
        }
        try
        {
            await Show();
            CaptureUiPreview("artwork-menu-" + (chatVideoShowingImage ? "preview" : "page"), menu.Card);
            if (clipboard != null && link != null)
            {
                RouteWaveformClick(Row("Скопировать ссылку").Bounds.Center);
                await WaitForLikedLayoutAsync(() => !menu.IsVisible);
                if (!clipboard.TryGetText(out var text) || text != link.AbsoluteUri)
                    throw new InvalidOperationException("Artwork menu copied a different image's URL.");
                await Show();
            }
            status.Value = "Save fixture pending";
            RouteWaveformClick(Row("Сохранить как").Bounds.Center);
            await WaitForLikedLayoutAsync(() => status.Value == "Изображение сохранено." && !menu.IsVisible);
            if (requests != 1 || dialog?.Owner != Window || dialog.DefaultExtension != "png" ||
                !dialog.FileName!.EndsWith(".png", StringComparison.Ordinal) || dialog.Filters?.Count != 1)
                throw new InvalidOperationException("Save as did not request a PNG file picker owned by the app.");
            var saved = ImageResult.FromMemory(await File.ReadAllBytesAsync(destination), ColorComponents.RedGreenBlueAlpha);
            var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
            source.CopyPixels(pixels, source.PixelWidth * 4);
            if (saved.Width != source.PixelWidth || saved.Height != source.PixelHeight)
                throw new InvalidOperationException("Saved artwork has different dimensions.");
            for (var i = 0; i < pixels.Length; i += 4)
                if (saved.Data[i] != pixels[i + 2] || saved.Data[i + 1] != pixels[i + 1] ||
                    saved.Data[i + 2] != pixels[i] || saved.Data[i + 3] != pixels[i + 3])
                    throw new InvalidOperationException("Saved PNG changed the artwork pixels.");

            // Cancel must preserve an existing file and leave the viewer open.
            var before = await File.ReadAllBytesAsync(destination);
            var wasPreview = chatVideoOpen.Value;
            await Show();
            status.Value = "Save fixture cancelled";
            RouteWaveformClick(Row("Сохранить как").Bounds.Center);
            await WaitForLikedLayoutAsync(() => requests == 2 && !menu.IsVisible);
            if (status.Value != "Save fixture cancelled" || chatVideoOpen.Value != wasPreview ||
                !(await File.ReadAllBytesAsync(destination)).SequenceEqual(before) || Directory.GetFiles(directory).Length != 1)
                throw new InvalidOperationException("Cancelling Save as modified a file or dismissed the preview.");
        }
        finally
        {
            menu.Close(true); artworkSaveDialog = oldDialog; windowClipboard = oldClipboard;
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("ARTWORK_MENU_OK: page/preview actions, original image URL, saved PNG pixels and cancelled save");
    }
}
