using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

internal static class BitmapImage
{
    internal static IImage Create(IGraphicsFactory factory, WriteableBitmap bitmap) =>
        OperatingSystem.IsWindows() && bitmap.HasAlpha
            ? factory.CreateImageView(new PremultipliedPixels(bitmap))
            : ((IImageSource)bitmap).CreateImage(factory);

    internal static IImage CreatePremultipliedView(IGraphicsFactory factory, IPixelBufferSource pixels) =>
        factory.CreateImageView(new PremultipliedView(pixels));

    // A Direct2D DC render target already contains premultiplied ink, despite
    // advertising straight-alpha pixels. Preserve those pixels when sharing a
    // completed transparent capture with another target.
    private sealed class PremultipliedView(IPixelBufferSource source) : IPixelBufferSource
    {
        public int PixelWidth => source.PixelWidth;
        public int PixelHeight => source.PixelHeight;
        public int Version => source.Version;
        public int StrideBytes => source.StrideBytes;
        public bool HasAlpha => source.HasAlpha;
        public bool IsPremultiplied => true;
        public LockMode LockMode => source.LockMode;
        public bool IsStreaming => source.IsStreaming;
        public PixelBufferLock Lock() => source.Lock();
    }

    // Direct2D discards its temporary premultiplied copy after each upload.
    // The header and window use different targets, so an unchanged translucent
    // raster otherwise allocates and converts its full pixel array twice per
    // frame. Retain one conversion per bitmap version using the public source
    // contract; the bitmap still owns its straight-alpha writable pixels.
    internal sealed class PremultipliedPixels(WriteableBitmap bitmap) : IPixelBufferSource
    {
        private readonly object gate = new();
        private byte[] pixels = [];
        private int version = -1;
        private Action? release;
        public int PixelWidth => bitmap.PixelWidth;
        public int PixelHeight => bitmap.PixelHeight;
        public int StrideBytes => bitmap.StrideBytes;
        public int Version => bitmap.Version;
        public bool HasAlpha => bitmap.HasAlpha;
        public bool IsPremultiplied => true;

        public PixelBufferLock Lock()
        {
            Monitor.Enter(gate);
            try
            {
                using var source = ((IPixelBufferSource)bitmap).Lock();
                if (version != source.Version)
                {
                    if (pixels.Length != source.Buffer.Length) pixels = new byte[source.Buffer.Length];
                    var input = source.Buffer;
                    for (var i = 0; i < input.Length; i += 4)
                    {
                        var alpha = input[i + 3];
                        pixels[i] = (byte)((input[i] * alpha + 127) / 255);
                        pixels[i + 1] = (byte)((input[i + 1] * alpha + 127) / 255);
                        pixels[i + 2] = (byte)((input[i + 2] * alpha + 127) / 255);
                        pixels[i + 3] = alpha;
                    }
                    version = source.Version;
                }
                release ??= () => Monitor.Exit(gate);
                return new PixelBufferLock(pixels, PixelWidth, PixelHeight, StrideBytes, version, source.DirtyRegion, release);
            }
            catch { Monitor.Exit(gate); throw; }
        }
    }
}
