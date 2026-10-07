using Aprillz.MewUI;

namespace Pookie.App;

internal readonly record struct PlayerPalette(Color Start, Color Middle, Color End)
{
    internal static readonly PlayerPalette Neutral = new(Color.FromRgb(34, 34, 34), Color.FromRgb(34, 34, 34), Color.FromRgb(34, 34, 34));

    internal PlayerPalette Lerp(PlayerPalette other, double amount) =>
        new(Start.Lerp(other.Start, amount), Middle.Lerp(other.Middle, amount), End.Lerp(other.End, amount));

    internal static PlayerPalette FromArtwork(ImageSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > 4_000_000) return Neutral;
        var pixels = new byte[width * height * 4];
        source.CopyPixels(pixels, width * 4);
        var bins = new (double Weight, double R, double G, double B)[25];
        var columns = Math.Min(64, width);
        var rows = Math.Min(64, height);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
        {
            var offset = ((int)((y + .5) * height / rows) * width + (int)((x + .5) * width / columns)) * 4;
            if (pixels[offset + 3] < 230) continue;
            double r = pixels[offset + 2] / 255d, g = pixels[offset + 1] / 255d, b = pixels[offset] / 255d;
            var maximum = Math.Max(r, Math.Max(g, b));
            var delta = maximum - Math.Min(r, Math.Min(g, b));
            if (maximum < .06) continue;
            var saturation = delta / maximum;
            var hue = delta == 0 ? 0 : maximum == r ? (g - b) / delta : maximum == g ? 2 + (b - r) / delta : 4 + (r - g) / delta;
            hue = (hue + 6) % 6;
            var index = saturation < .15 ? 24 : (int)Math.Round(hue * 4) % 24;
            var weight = Math.Sqrt(maximum) * (.35 + .65 * saturation);
            ref var bin = ref bins[index];
            bin.Weight += weight; bin.R += r * weight; bin.G += g * weight; bin.B += b * weight;
        }

        var families = new (double Weight, double R, double G, double B)[25];
        for (var i = 0; i < 25; i++)
        {
            foreach (var index in i == 24 ? [24] : new[] { (i + 23) % 24, i, (i + 1) % 24 })
            {
                families[i].Weight += bins[index].Weight;
                families[i].R += bins[index].R; families[i].G += bins[index].G; families[i].B += bins[index].B;
            }
        }
        var dominant = Enumerable.Range(0, 25).MaxBy(i => families[i].Weight);
        var main = families[dominant];
        if (main.Weight == 0) return Neutral;
        var primary = Darken(main.R / main.Weight, main.G / main.Weight, main.B / main.Weight);
        var secondary = primary;
        var runner = Enumerable.Range(0, 24)
            .Where(i => dominant == 24 || Math.Min(Math.Abs(i - dominant), 24 - Math.Abs(i - dominant)) > 3)
            .OrderByDescending(i => families[i].Weight).First();
        var alternate = families[runner];
        if (dominant != 24 && alternate.Weight > main.Weight * .2)
            secondary = primary.Lerp(Darken(alternate.R / alternate.Weight, alternate.G / alternate.Weight, alternate.B / alternate.Weight), .55);
        return new(primary.Lerp(Neutral.Start, .2), primary, secondary.Lerp(Color.FromRgb(22, 22, 22), .3));
    }

    private static Color Darken(double r, double g, double b)
    {
        var maximum = Math.Max(r, Math.Max(g, b));
        r = 12 + r / maximum * 70; g = 12 + g / maximum * 70; b = 12 + b / maximum * 70;
        static double Linear(double channel) => channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);

        double Luminance(double scale) => .2126 * Linear(r * scale / 255) + .7152 * Linear(g * scale / 255) + .0722 * Linear(b * scale / 255);
        double low = 0, high = 1;
        if (Luminance(1) <= .027) low = 1;
        else for (var i = 0; i < 16; i++)
        {
            var scale = (low + high) / 2;
            if (Luminance(scale) > .027) high = scale;
            else low = scale;
        }
        return Color.FromRgb((byte)(r * low), (byte)(g * low), (byte)(b * low));
    }
}
