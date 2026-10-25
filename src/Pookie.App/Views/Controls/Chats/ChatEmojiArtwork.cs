using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Messages;
using Pookie.App.Storage;

namespace Pookie.App;

internal static class ChatEmojiArtwork
{
    // At most 192 decoded 72px images (~3.8 MiB), plus visible controls' ownership.
    private static readonly ResourceCache<string, ImageSource> images = new(192, new CacheBudget(4 * 1024 * 1024), _ => 72 * 72 * 4);
    private static readonly Dictionary<string, (IImage Image, LinkedListNode<string> Node)> textures = [];
    private static readonly LinkedList<string> recency = [];
    private static readonly object textureSync = new();
    internal static int CachedCount => images.Count;
    internal static int TextureCount { get { lock (textureSync) return textures.Count; } }
    internal static ImageSource? Source(string value)
    {
        var asset = ChatEmojiCatalog.Find(value)?.Asset;
        if (string.IsNullOrEmpty(asset)) return null;
        if (images.TryGetValue(asset, out var source)) return source;
        using var stream = typeof(ChatEmojiArtwork).Assembly.GetManifestResourceStream($"Pookie.App.Assets.Emoji.{asset}.png");
        if (stream == null) return null;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        // Decode only the small, offline 72px artwork needed by visible controls.
        var decoded = StbImageSharp.ImageResult.FromMemory(bytes.ToArray(), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        for (var i = 0; i < decoded.Data.Length; i += 4) (decoded.Data[i], decoded.Data[i + 2]) = (decoded.Data[i + 2], decoded.Data[i]);
        images[asset] = source = ImageSource.FromBgraPixels(decoded.Width, decoded.Height, decoded.Data);
        return source;
    }
    internal static Image View(string value, double size = 28) => new Image().Source(Source(value)).Width(size).Height(size).StretchMode(Stretch.Uniform);
    internal static void Draw(IGraphicsContext context, string value, Rect bounds)
    {
        var asset = ChatEmojiCatalog.Find(value)?.Asset;
        if (string.IsNullOrEmpty(asset)) return;
        lock (textureSync)
        {
            if (!textures.TryGetValue(asset, out var entry))
            {
                if (Source(value) is not { } source) return;
                entry = (source.CreateImage(Application.Current.GraphicsFactory, 72, 72), recency.AddLast(asset));
                textures.Add(asset, entry);
                while (textures.Count > 192)
                {
                    var oldest = recency.First!; recency.RemoveFirst();
                    textures.Remove(oldest.Value, out var removed); removed.Image.Dispose();
                }
            }
            else { recency.Remove(entry.Node); recency.AddLast(entry.Node); }
            context.DrawImage(entry.Image, bounds);
        }
    }
    internal static void Clear()
    {
        lock (textureSync)
        {
            foreach (var entry in textures.Values) entry.Image.Dispose();
            textures.Clear(); recency.Clear(); images.Clear();
        }
    }
}
