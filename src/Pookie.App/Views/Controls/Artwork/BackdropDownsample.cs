namespace Pookie.App;

// Area sampling includes every source pixel in the destination footprint. A
// bilinear sample alone skips fine details when a native-DPI strip is reduced
// to quarter-DIP resolution, making moving artwork stick and jump under glass.
internal sealed class BackdropDownsample
{
    private Sample[] columns = [], rows = [];
    private float[] intermediate = [];
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
            intermediate = new float[checked(reducedWidth * height * 4)];
            sourceWidth = width; sourceHeight = height; targetWidth = reducedWidth; targetHeight = reducedHeight;
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < reducedWidth; x++)
        {
            var sample = columns[x];
            var index = y * stride + sample.First * 4;
            var target = (y * reducedWidth + x) * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var sum = source[index + channel] * sample.FirstWeight;
                for (var pixel = sample.First + 1; pixel < sample.Last; pixel++) sum += source[y * stride + pixel * 4 + channel];
                if (sample.Last != sample.First) sum += source[y * stride + sample.Last * 4 + channel] * sample.LastWeight;
                intermediate[target + channel] = sum * sample.InverseWidth;
            }
        }
        for (var y = 0; y < reducedHeight; y++)
        for (var x = 0; x < reducedWidth; x++)
        {
            var sample = rows[y];
            var index = (sample.First * reducedWidth + x) * 4;
            var target = (y * reducedWidth + x) * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var sum = intermediate[index + channel] * sample.FirstWeight;
                for (var pixel = sample.First + 1; pixel < sample.Last; pixel++) sum += intermediate[(pixel * reducedWidth + x) * 4 + channel];
                if (sample.Last != sample.First) sum += intermediate[(sample.Last * reducedWidth + x) * 4 + channel] * sample.LastWeight;
                destination[target + channel] = (byte)Math.Clamp((int)MathF.Round(sum * sample.InverseWidth), 0, 255);
            }
        }
    }

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
