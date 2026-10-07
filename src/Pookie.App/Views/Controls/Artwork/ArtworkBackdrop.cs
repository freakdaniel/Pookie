using Aprillz.MewUI;

namespace Pookie.App;

internal static class ArtworkBackdrop
{
    internal static ImageSource? Create(ImageSource source)
    {
        var sourceWidth = source.PixelWidth; var sourceHeight = source.PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0 || (long)sourceWidth * sourceHeight > 4_000_000) return null;
        var pixels = new byte[sourceWidth * sourceHeight * 4];
        source.CopyPixels(pixels, sourceWidth * 4);
        // Reduce before blurring: work is bounded and small details cannot remain sharp.
        var scale = Math.Min(1, 96d / Math.Max(sourceWidth, sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var reduced = new float[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var left = x * sourceWidth / width; var right = (x + 1) * sourceWidth / width;
            var top = y * sourceHeight / height; var bottom = (y + 1) * sourceHeight / height;
            var count = (right - left) * (bottom - top);
            for (var sy = top; sy < bottom; sy++)
            for (var sx = left; sx < right; sx++)
            {
                var input = (sy * sourceWidth + sx) * 4;
                var alpha = pixels[input + 3] / 255f;
                for (var channel = 0; channel < 3; channel++)
                    reduced[(y * width + x) * 3 + channel] += (pixels[input + channel] * alpha + 29 * (1 - alpha)) / count;
            }
        }
        const int radius = 18;
        var weights = Enumerable.Range(-radius, radius * 2 + 1).Select(i => Math.Exp(-i * i / 72d)).ToArray();
        var total = weights.Sum();
        for (var i = 0; i < weights.Length; i++) weights[i] /= total;
        var horizontal = Blur(reduced, width, height, weights, horizontal: true);
        var blurred = Blur(horizontal, width, height, weights, horizontal: false);
        var result = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            for (var channel = 0; channel < 3; channel++)
                result[i * 4 + channel] = (byte)Math.Round(blurred[i * 3 + channel] * .24 + 29 * .76);
            result[i * 4 + 3] = 255;
        }
        return ImageSource.FromBgraPixels(width, height, result, hasAlpha: false);
    }

    private static float[] Blur(float[] pixels, int width, int height, double[] weights, bool horizontal)
    {
        var result = new float[pixels.Length];
        var radius = weights.Length / 2;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        for (var channel = 0; channel < 3; channel++)
        {
            double value = 0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var sx = horizontal ? Math.Clamp(x + offset, 0, width - 1) : x;
                var sy = horizontal ? y : Math.Clamp(y + offset, 0, height - 1);
                value += pixels[(sy * width + sx) * 3 + channel] * weights[offset + radius];
            }
            result[(y * width + x) * 3 + channel] = (float)value;
        }
        return result;
    }
}
