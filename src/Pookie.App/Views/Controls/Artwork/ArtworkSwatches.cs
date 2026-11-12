using Aprillz.MewUI;

namespace Pookie.App;

internal static class ArtworkSwatches
{
    internal static readonly Color[] Fallback = [Color.FromRgb(255, 85, 0), Color.FromRgb(75, 133, 218),
        Color.FromRgb(184, 91, 155), Color.FromRgb(80, 159, 128), Color.FromRgb(154, 116, 205)];
    private readonly record struct Sample(double R, double G, double B, int Count)
    {
        internal Color Color => Color.FromRgb((byte)Math.Round(R), (byte)Math.Round(G), (byte)Math.Round(B));
        internal double Distance(Sample other) => (R - other.R) * (R - other.R) + (G - other.G) * (G - other.G) + (B - other.B) * (B - other.B);
    }

    internal static Color[] FromArtwork(ImageSource source)
    {
        var width = source.PixelWidth; var height = source.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > 4_000_000) return Fallback.ToArray();
        var pixels = new byte[width * height * 4]; source.CopyPixels(pixels, width * 4);
        var bins = new Dictionary<int, (int Count, long R, long G, long B)>();
        var columns = Math.Min(width, 64); var rows = Math.Min(height, 64);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
        {
            var offset = ((int)((y + .5) * height / rows) * width + (int)((x + .5) * width / columns)) * 4;
            if (pixels[offset + 3] < 224) continue;
            int r = pixels[offset + 2], g = pixels[offset + 1], b = pixels[offset];
            var key = (r >> 4) << 8 | (g >> 4) << 4 | b >> 4;
            var bin = bins.GetValueOrDefault(key);
            bins[key] = (bin.Count + 1, bin.R + r, bin.G + g, bin.B + b);
        }
        if (bins.Count == 0) return Fallback.ToArray();
        var samples = bins.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key)
            .Select(pair => new Sample((double)pair.Value.R / pair.Value.Count, (double)pair.Value.G / pair.Value.Count,
                (double)pair.Value.B / pair.Value.Count, pair.Value.Count)).ToArray();
        var centers = new List<Sample> { samples[0] };
        static int Nearest(Sample sample, List<Sample> centers)
        {
            var winner = 0; var distance = double.PositiveInfinity;
            for (var index = 0; index < centers.Count; index++)
            { var next = centers[index].Distance(sample); if (next < distance) { distance = next; winner = index; } }
            return winner;
        }
        // Seed separated, frequent families, then average nearby shades by
        // population. Texture/noise must not occupy all five preset slots.
        while (centers.Count < Math.Min(5, samples.Length))
        {
            var next = samples[0]; var weight = -1d;
            foreach (var sample in samples)
            {
                var score = centers[Nearest(sample, centers)].Distance(sample) * Math.Sqrt(sample.Count);
                if (score > weight) { weight = score; next = sample; }
            }
            if (centers[Nearest(next, centers)].Distance(next) < 1) break;
            centers.Add(next);
        }
        for (var pass = 0; pass < 8; pass++)
        {
            var sums = new (double R, double G, double B, int Count)[centers.Count];
            foreach (var sample in samples)
            {
                var index = Nearest(sample, centers);
                ref var sum = ref sums[index];
                sum.R += sample.R * sample.Count; sum.G += sample.G * sample.Count; sum.B += sample.B * sample.Count; sum.Count += sample.Count;
            }
            for (var index = 0; index < centers.Count; index++)
                if (sums[index].Count > 0) centers[index] = new(sums[index].R / sums[index].Count,
                    sums[index].G / sums[index].Count, sums[index].B / sums[index].Count, sums[index].Count);
        }
        var result = new List<Color>();
        static double Distance(Color left, Color right) => (left.R - right.R) * (left.R - right.R) +
            (left.G - right.G) * (left.G - right.G) + (left.B - right.B) * (left.B - right.B);
        void Add(Color color) { if (result.Count < 5 && result.All(other => Distance(color, other) >= 18 * 18)) result.Add(color); }
        foreach (var center in centers.OrderByDescending(center => center.Count)) Add(center.Color);
        var dominant = result[0];
        foreach (var amount in new[] { .2, .4, .6, .8, 1 })
        { Add(dominant.Lerp(Color.White, amount)); Add(dominant.Lerp(Color.Black, amount)); }
        return result.ToArray();
    }
}
