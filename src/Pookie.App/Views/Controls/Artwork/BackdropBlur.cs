using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Three sliding box passes approximate a Gaussian in linear time. The small
// backdrop strips must never wait on ThreadPool work while owning a GL context:
// vector-image workers in that same pool can be waiting for that context.
internal sealed class BackdropBlur : IDisposable
{
    private readonly WriteableBitmap bitmap;
    private readonly byte[] first, second;
    private readonly int width, height;
    private BackdropDownsample? downsample;
    internal IImage Image { get; }
    internal bool Ready { get; private set; }
    internal ReadOnlySpan<byte> Pixels => first;

    internal BackdropBlur(IGraphicsFactory factory, int width, int height)
    {
        this.width = width; this.height = height;
        first = new byte[width * height * 4]; second = new byte[first.Length];
        bitmap = new WriteableBitmap(width, height, clear: false);
        Image = ((IImageSource)bitmap).CreateImage(factory);
    }

    internal void Update(ICpuPixelSurface source, double sigma)
    {
        var pixels = source.GetReadOnlyPixelSpan();
        for (var y = 0; y < height; y++) pixels.Slice(y * source.StrideBytes, width * 4).CopyTo(first.AsSpan(y * width * 4));
        Process(sigma); Upload();
    }
    internal void UpdateReduced(ICpuPixelSurface source, double sigma)
    {
        downsample ??= new BackdropDownsample();
        downsample.Resize(source.GetReadOnlyPixelSpan(), source.PixelWidth, source.PixelHeight, source.StrideBytes, first, width, height);
        Process(sigma); Upload();
    }
    // Capture while the render scope is current; process after it is released.
    // Only one job may own these scratch arrays at a time.
    internal void Capture(ICpuPixelSurface source)
    {
        var pixels = source.GetReadOnlyPixelSpan();
        for (var y = 0; y < height; y++) pixels.Slice(y * source.StrideBytes,width * 4).CopyTo(first.AsSpan(y * width * 4));
    }
    internal void Process(double sigma)
    {
        var lower = Math.Max(1, (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1)) | 1);
        if (lower > Math.Sqrt(4 * sigma * sigma + 1)) lower = Math.Max(1, lower - 2);
        var count = (int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4d * lower - 4));
        for (var pass = 0; pass < 3; pass++)
        {
            var radius = ((pass < count ? lower : lower + 2) - 1) / 2;
            Box(first, second, radius, horizontal: true);
            Box(second, first, radius, horizontal: false);
        }
    }
    internal void Upload() { using var write = bitmap.LockForWrite(); first.CopyTo(write.PixelsBgra32); Ready = true; }

    private void Box(byte[] input, byte[] output, int radius, bool horizontal)
    {
        var length = horizontal ? width : height;
        var lines = horizontal ? height : width;
        var stride = horizontal ? 4 : width * 4;
        var diameter = radius * 2 + 1;
        for (var line = 0; line < lines; line++)
        for (var channel = 0; channel < 4; channel++)
        {
            var start = (horizontal ? line * width * 4 : line * 4) + channel;
            var sum = input[start] * (radius + 1);
            for (var i = 1; i <= radius; i++) sum += input[start + Math.Min(length - 1, i) * stride];
            for (var i = 0; i < length; i++)
            {
                output[start + i * stride] = (byte)((sum + diameter / 2) / diameter);
                sum += input[start + Math.Min(length - 1, i + radius + 1) * stride] - input[start + Math.Max(0, i - radius) * stride];
            }
        }
    }

    public void Dispose() { Image.Dispose(); bitmap.Dispose(); }
}
