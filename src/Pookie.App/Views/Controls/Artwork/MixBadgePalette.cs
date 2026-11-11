using Aprillz.MewUI;

namespace Pookie.App;

internal sealed record MixBadgePalette(Color Surface, Color Foreground)
{
    internal static MixBadgePalette? FromArtwork(ImageSource source)
    {
        try
        {
            var width = source.PixelWidth; var height = source.PixelHeight;
            if (width < 24 || height < 24 || (long)width * height > 4_000_000) return null;
            var pixels = new byte[width * height * 4];
            source.CopyPixels(pixels, width * 4);
            var samples = new List<Color>();
            var bins = new Dictionary<int, List<Color>>();
            // The MIX label occupies the bottom band of numbered mixes. Its
            // right half is clear of the black lettering and rounded corners.
            for (var y = 0; y < 12; y++)
            for (var x = 0; x < 32; x++)
            {
                var sx = Math.Min(width - 1, (int)(width * (.55 + (x + .5) / 32 * .35)));
                var sy = Math.Min(height - 1, (int)(height * (.86 + (y + .5) / 12 * .08)));
                var offset = (sy * width + sx) * 4;
                if (pixels[offset + 3] < 240) continue;
                var color = Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
                samples.Add(color);
                var key = (color.R >> 4) << 8 | (color.G >> 4) << 4 | color.B >> 4;
                if (!bins.TryGetValue(key, out var bin)) bins[key] = bin = [];
                bin.Add(color);
            }
            if (samples.Count < 100) return null;
            static Color Average(IEnumerable<Color> colors) => Color.FromRgb(
                (byte)Math.Round(colors.Average(color => (double)color.R)),
                (byte)Math.Round(colors.Average(color => (double)color.G)),
                (byte)Math.Round(colors.Average(color => (double)color.B)));
            var seed = Average(bins.MaxBy(pair => pair.Value.Count).Value);
            var flat = samples.Where(color => Math.Pow(color.R - seed.R, 2) + Math.Pow(color.G - seed.G, 2) +
                Math.Pow(color.B - seed.B, 2) <= 32 * 32).ToArray();
            // Artwork without a flat label keeps the ordinary neutral button.
            if (flat.Length < samples.Count * .6) return null;
            var surface = Average(flat);
            static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
            var luminance = .2126 * Linear(surface.R) + .7152 * Linear(surface.G) + .0722 * Linear(surface.B);
            var foreground = (luminance + .05) / .05 >= 1.05 / (luminance + .05) ? Color.FromRgb(0, 0, 0) : Color.White;
            return new(surface, foreground);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { return null; }
    }
}
