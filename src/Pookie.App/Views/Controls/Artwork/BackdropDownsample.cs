using System.Numerics;

namespace Pookie.App;

// Area sampling includes every source pixel in the destination footprint. A
// bilinear sample alone skips fine details when a native-DPI strip is reduced
// to quarter-DIP resolution, making moving artwork stick and jump under glass.
internal sealed class BackdropDownsample
{
    private Sample[] columns = [], rows = [];
    private Vector4[] intermediate = [];
    private int sourceWidth, sourceHeight, targetWidth, targetHeight;

    internal void Resize(ReadOnlySpan<byte> source, int width, int height, int stride,
        Span<byte> destination, int reducedWidth, int reducedHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, reducedWidth);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, reducedHeight);
        ArgumentOutOfRangeException.ThrowIfLessThan(reducedWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(reducedHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, checked(width * 4));
        if (source.Length < checked((height - 1) * stride + width * 4) || destination.Length < checked(reducedWidth * reducedHeight * 4))
            throw new ArgumentException("Backdrop pixel buffer is smaller than its geometry.");
        if (sourceWidth != width || sourceHeight != height || targetWidth != reducedWidth || targetHeight != reducedHeight)
        {
            columns = Map(width, reducedWidth); rows = Map(height, reducedHeight);
            intermediate = new Vector4[checked(reducedWidth * height)];
            sourceWidth = width; sourceHeight = height; targetWidth = reducedWidth; targetHeight = reducedHeight;
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < reducedWidth; x++)
        {
            var sample = columns[x];
            var index = y * stride + sample.First * 4;
            // One pixel footprint and one SIMD sum for BGRA, rather than
            // traversing the same source row independently for every channel.
            var sum = Read(source, index) * sample.FirstWeight;
            for (var pixel = sample.First + 1; pixel < sample.Last; pixel++) sum += Read(source, y * stride + pixel * 4);
            if (sample.Last != sample.First) sum += Read(source, y * stride + sample.Last * 4) * sample.LastWeight;
            intermediate[y * reducedWidth + x] = sum * sample.InverseWidth;
        }
        for (var y = 0; y < reducedHeight; y++)
        for (var x = 0; x < reducedWidth; x++)
        {
            var sample = rows[y];
            var index = sample.First * reducedWidth + x;
            var target = (y * reducedWidth + x) * 4;
            var sum = intermediate[index] * sample.FirstWeight;
            for (var pixel = sample.First + 1; pixel < sample.Last; pixel++) sum += intermediate[pixel * reducedWidth + x];
            if (sample.Last != sample.First) sum += intermediate[sample.Last * reducedWidth + x] * sample.LastWeight;
            var value = Vector4.Clamp(Vector4.Round(sum * sample.InverseWidth), Vector4.Zero, new Vector4(255));
            destination[target] = (byte)value.X; destination[target + 1] = (byte)value.Y;
            destination[target + 2] = (byte)value.Z; destination[target + 3] = (byte)value.W;
        }
    }

    private static Vector4 Read(ReadOnlySpan<byte> pixels, int index) =>
        new(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);

    private static Sample[] Map(int source, int target)
    {
        var samples = new Sample[target];
        var scale = source / (double)target;
        for (var i = 0; i < target; i++)
        {
            var start = i * scale; var end = Math.Min(source, (i + 1) * scale);
            var first = (int)Math.Floor(start); var last = Math.Min(source - 1, (int)Math.Ceiling(end) - 1);
            samples[i] = new(first, last, (float)(first == last ? end - start : first + 1 - start),
                (float)(end - last), (float)(1 / (end - start)));
        }
        return samples;
    }
    private readonly record struct Sample(int First, int Last, float FirstWeight, float LastWeight, float InverseWidth);
}
