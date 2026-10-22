using System.Buffers.Binary;
using Aprillz.MewUI;

namespace Pookie.App.Storage;

// Fetch the site's own conventional favicon, cache it with other image data and
// decode small PNG/ICO images off the UI thread. No page text or URL path is sent.
internal sealed class FaviconLoader(ImageDiskCache disk, HttpMessageHandler? handler = null) : IDisposable
{
    private readonly HttpClient client = new(handler ?? new HttpClientHandler
    { UseCookies = false, MaxAutomaticRedirections = 3 }) { Timeout = TimeSpan.FromSeconds(6) };
    private readonly Dictionary<string, ImageSource?> cache = [];
    private readonly object gate = new();

    internal Task<ImageSource?> LoadAsync(Uri link, CancellationToken token) => Task.Run(async () =>
    {
        var origin = link.GetLeftPart(UriPartial.Authority);
        lock (gate) if (cache.TryGetValue(origin, out var value)) return value;
        ImageSource? source = null;
        foreach (var path in new[] { "/favicon.ico", "/favicon.png" })
        {
            var url = origin + path;
            try
            {
                var bytes = await disk.ReadAsync(url, token);
                if (bytes != null) source = Decode(bytes);
                if (source != null) break;
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 256 * 1024) continue;
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var data = new MemoryStream(); var buffer = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(buffer, token)) > 0)
                {
                    if (data.Length + count > 256 * 1024) break;
                    data.Write(buffer, 0, count);
                }
                bytes = data.ToArray(); source = Decode(bytes);
                if (source == null) continue;
                await disk.WriteAsync(url, bytes, token); break;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception error) when (error is HttpRequestException or IOException or ArgumentException or NotSupportedException) { }
        }
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.Count >= 128) cache.Remove(cache.Keys.First());
            cache[origin] = source;
        }
        return source;
    }, token);

    internal static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            if (bytes.Length >= 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0)
            {
                var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
                if (count == 0 || 6 + count * 16 > bytes.Length) return null;
                // Prefer a 32px entry over a large, expensive icon.
                var entries = Enumerable.Range(0, count).OrderBy(i => Math.Abs((bytes[6 + i * 16] == 0 ? 256 : bytes[6 + i * 16]) - 32));
                foreach (var i in entries)
                {
                    var entry = bytes.AsSpan(6 + i * 16, 16);
                    var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                    var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
                    if (length < 40 || offset > bytes.Length || length > bytes.Length - offset) continue;
                    var data = bytes.AsSpan((int)offset, (int)length);
                    if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return Decode(data.ToArray());
                    if (BinaryPrimitives.ReadUInt32LittleEndian(data) != 40) continue;
                    var width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
                    var height = BinaryPrimitives.ReadInt32LittleEndian(data[8..]) / 2;
                    var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
                    if (width is <= 0 or > 256 || height is <= 0 or > 256 || bits != 32 || BinaryPrimitives.ReadInt32LittleEndian(data[16..]) != 0) continue;
                    var pixelBytes = width * height * 4;
                    if (40 + pixelBytes > data.Length) continue;
                    var pixels = new byte[pixelBytes]; var alpha = false;
                    for (var y = 0; y < height; y++)
                        data.Slice(40 + (height - 1 - y) * width * 4, width * 4).CopyTo(pixels.AsSpan(y * width * 4));
                    for (var a = 3; a < pixels.Length; a += 4) alpha |= pixels[a] != 0;
                    // Older 32-bit icons use only the separate 1-bit transparency mask.
                    var stride = ((width + 31) / 32) * 4;
                    var mask = data[(40 + pixelBytes)..];
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var hidden = mask.Length >= stride * height && (mask[(height - 1 - y) * stride + x / 8] & (128 >> (x % 8))) != 0;
                        var a = (y * width + x) * 4 + 3;
                        if (hidden || !alpha) pixels[a] = hidden ? (byte)0 : (byte)255;
                    }
                    return ImageSource.FromBgraPixels(width, height, pixels);
                }
                return null;
            }
            using var image = ImageSource.FromBytes(bytes);
            if (image.PixelWidth is <= 0 or > 256 || image.PixelHeight is <= 0 or > 256) return null;
            var decoded = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(decoded, image.PixelWidth * 4);
            return ImageSource.FromBgraPixels(image.PixelWidth, image.PixelHeight, decoded);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or OverflowException) { return null; }
    }
    public void Dispose() => client.Dispose();
}
