using Aprillz.MewUI;

namespace Pookie.App;

// A local approximation of the directional contour shown in Apple's material
// references, not Apple's private shader. Keep specular light above the tint;
// refraction belongs to the backdrop below it. Two grazing lights describe the
// curved silhouette without a uniform stroke or a wide, inset bevel.
internal static class GlassRim
{
    private const double Width = 4;
    private static readonly (double Edge, double Shoulder)[] profile = Enumerable.Range(0, 257).Select(index =>
    {
        var depth = index / 256d * Width;
        return (.58 * Math.Exp(-Math.Pow((depth - .38) / .48, 2)),
            .105 * Math.Exp(-Math.Pow((depth - 1.15) / 1.05, 2)));
    }).ToArray();

    internal static (double Reflection, double Transmission, double Coverage) SamplePixel(double x, double y, double pixelWidth, double pixelHeight,
        Size size, CornerRadius corners, double emphasis)
    {
        var centre = Sample(x, y, size, corners, emphasis);
        // The high-frequency contour needs an area filter, not just silhouette
        // AA. Skip the flat interior so large glass surfaces stay cheap to cache.
        var reach = (pixelWidth + pixelHeight) / 2;
        if (centre.Depth > Width + reach) return (0, 0, 1);
        if (centre.Depth < -reach) return default;
        if (Math.Abs(centre.Depth) < reach)
            return SampleContourPixel(x, y, pixelWidth, pixelHeight, size, corners, emphasis);
        double reflection = 0, transmission = 0;
        var inside = 0;
        const int samples = 4;
        for (var sy = 0; sy < samples; sy++)
        for (var sx = 0; sx < samples; sx++)
        {
            var sample = Sample(x + ((sx + .5) / samples - .5) * pixelWidth,
                y + ((sy + .5) / samples - .5) * pixelHeight, size, corners, emphasis);
            if (sample.Depth < 0) continue;
            inside++;
            reflection += sample.Reflection; transmission += sample.Transmission;
        }
        return (reflection / (samples * samples), transmission / (samples * samples), inside / (double)(samples * samples));
    }

    private static (double Reflection, double Transmission, double Coverage) SampleContourPixel(double x, double y,
        double pixelWidth, double pixelHeight, Size size, CornerRadius corners, double emphasis)
    {
        var top = Math.Max(0, y - pixelHeight / 2);
        var bottom = Math.Min(size.Height, y + pixelHeight / 2);
        if (bottom <= top) return default;
        double reflection = 0, transmission = 0, coverage = 0;
        const int rows = 8, columns = 4;
        var maxRadius = Math.Min(size.Width, size.Height) / 2;
        // Exact horizontal intersections avoid the discrete coverage levels of a
        // small square sample grid, especially along fractional straight edges.
        double Inset(double radius, double fromEnd)
        {
            radius = Math.Clamp(radius, 0, maxRadius);
            return fromEnd >= radius ? 0 : radius - Math.Sqrt(Math.Max(0, fromEnd * (2 * radius - fromEnd)));
        }
        for (var row = 0; row < rows; row++)
        {
            var sy = top + (row + .5) / rows * (bottom - top);
            var upper = sy < size.Height / 2;
            var fromEnd = upper ? sy : size.Height - sy;
            var left = Inset(upper ? corners.TopLeft : corners.BottomLeft, fromEnd);
            var right = size.Width - Inset(upper ? corners.TopRight : corners.BottomRight, fromEnd);
            var start = Math.Max(x - pixelWidth / 2, left);
            var end = Math.Min(x + pixelWidth / 2, right);
            if (end <= start) continue;
            var weight = (end - start) / pixelWidth * (bottom - top) / pixelHeight / rows;
            coverage += weight;
            for (var column = 0; column < columns; column++)
            {
                var sample = Sample(start + (column + .5) / columns * (end - start), sy, size, corners, emphasis);
                reflection += sample.Reflection * weight / columns;
                transmission += sample.Transmission * weight / columns;
            }
        }
        return (reflection, transmission, Math.Clamp(coverage, 0, 1));
    }

    private static (double Reflection, double Transmission, double Depth) Sample(double x, double y, Size size, CornerRadius corners, double emphasis)
    {
        var dx = x - size.Width / 2;
        var dy = y - size.Height / 2;
        var radius = Math.Clamp(dy < 0 ? dx < 0 ? corners.TopLeft : corners.TopRight :
            dx < 0 ? corners.BottomLeft : corners.BottomRight, 0, Math.Min(size.Width, size.Height) / 2);
        var qx = Math.Abs(dx) - size.Width / 2 + radius;
        var qy = Math.Abs(dy) - size.Height / 2 + radius;
        var corner = qx > 0 && qy > 0;
        var distance = corner ? Math.Sqrt(qx * qx + qy * qy) : Math.Max(qx, qy);
        var depth = radius - distance;
        if (depth > Width) return (0, 0, depth);
        var nx = corner ? Math.CopySign(qx / distance, dx) : qx > qy ? Math.CopySign(1, dx) : 0;
        var ny = corner ? Math.CopySign(qy / distance, dy) : qx > qy ? 0 : Math.CopySign(1, dy);
        var light = -nx * .55 - ny * .835;
        var alignment = light * light;
        alignment *= alignment;
        var lobe = (light >= 0 ? 1 : .78) * alignment;
        var position = Math.Clamp(depth / Width * 256, 0, 256);
        var lower = (int)position;
        var a = profile[lower]; var b = profile[Math.Min(lower + 1, 256)];
        var fraction = position - lower;
        var band = (Edge: a.Edge + (b.Edge - a.Edge) * fraction,
            Shoulder: a.Shoulder + (b.Shoulder - a.Shoulder) * fraction);
        var reflection = (band.Edge * (.025 + .975 * lobe) + band.Shoulder * lobe) * (.85 + .15 * Math.Clamp(emphasis, 0, 1));
        return (reflection, .12 * Math.Clamp(1 - depth, 0, 1), depth);
    }
}
