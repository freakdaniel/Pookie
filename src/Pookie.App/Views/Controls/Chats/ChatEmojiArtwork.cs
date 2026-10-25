using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.App.Storage;
using System.Text;

namespace Pookie.App;

internal static class ChatEmojiArtwork
{
    // At most 192 decoded 72px images (~3.8 MiB), plus visible controls' ownership.
    private static readonly ResourceCache<string, ImageSource> images = new(192, new CacheBudget(4 * 1024 * 1024), _ => 72 * 72 * 4);
    internal static int CachedCount => images.Count;
    internal static ImageSource? Source(string value)
    {
        var asset = ChatEmojiCatalog.LoadedAsset(value) ?? string.Join('-', value.EnumerateRunes().Where(r => value.Contains('\u200d') || r.Value != 0xfe0f).Select(r => r.Value.ToString("x", System.Globalization.CultureInfo.InvariantCulture)));
        if (images.TryGetValue(asset, out var source)) return source;
        using var stream = typeof(ChatEmojiArtwork).Assembly.GetManifestResourceStream($"Pookie.App.Assets.Emoji.{asset}.png");
        if (stream == null) return null;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        // One upstream PNG uses Adam7 interlacing, unsupported by the native
        // lightweight decoder. Decode these tiny, trusted assets consistently.
        var decoded = StbImageSharp.ImageResult.FromMemory(bytes.ToArray(), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        for (var i = 0; i < decoded.Data.Length; i += 4) (decoded.Data[i], decoded.Data[i + 2]) = (decoded.Data[i + 2], decoded.Data[i]);
        images[asset] = source = ImageSource.FromBgraPixels(decoded.Width, decoded.Height, decoded.Data);
        return source;
    }
    internal static Image View(string value, double size = 28) => new Image().Source(Source(value)).Width(size).Height(size).StretchMode(Stretch.Uniform);
    internal static void Clear() => images.Clear();
}
