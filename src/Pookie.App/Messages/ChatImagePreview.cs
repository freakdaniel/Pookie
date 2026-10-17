using Aprillz.MewUI;
using StbImageSharp;

namespace Pookie.App.Messages;

internal static class ChatImagePreview
{
    // Includes 4K and 12 MP photos, while bounding the transient RGBA decode to 64 MiB.
    private const long MaxPixels = 16 * 1024 * 1024;

    internal static Size? ReadSize(byte[] bytes)
    {
        if (!ChatMediaCrypto.IsRasterImage(bytes)) return null;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            var info = ImageInfo.FromStream(input);
            if (info == null || info.Value.Width is <= 0 or > 8192 || info.Value.Height is <= 0 or > 8192 ||
                (long)info.Value.Width * info.Value.Height > MaxPixels) return null;
            return new(info.Value.Width, info.Value.Height);
        }
        catch { return null; }
    }

    internal static Size Fit(Size size, double maxWidth = 300, double maxHeight = 260)
    {
        var scale = Math.Min(1, Math.Min(maxWidth / size.Width, maxHeight / size.Height));
        return new(Math.Max(1, Math.Floor(size.Width * scale)), Math.Max(1, Math.Floor(size.Height * scale)));
    }

    internal static double MessageWidth(Size size, double? captionWidth) => captionWidth == null ? Fit(size).Width :
        Math.Min(440, Math.Max(Fit(size).Width, Math.Max(220, captionWidth.Value + 24)));

    internal static ImageSource? Decode(byte[] bytes, int maxWidth = 520, int maxHeight = 380)
    {
        if (ReadSize(bytes) is not { } size) return null;
        try
        {
            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            var target = Fit(size, maxWidth, maxHeight);
            var width = (int)target.Width; var height = (int)target.Height;
            var pixels = new byte[width * height * 4];
            try
            {
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var from = ((y * image.Height / height) * image.Width + x * image.Width / width) * 4;
                    var to = (y * width + x) * 4;
                    pixels[to] = image.Data[from + 2]; pixels[to + 1] = image.Data[from + 1];
                    pixels[to + 2] = image.Data[from]; pixels[to + 3] = image.Data[from + 3];
                }
                return ImageSource.FromBgraPixels(width, height, pixels);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(image.Data); }
        }
        catch { return null; }
    }
}
