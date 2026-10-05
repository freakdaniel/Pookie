using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

internal static class Icons
{
    private static readonly Dictionary<(string, uint), SimpleSvgSource> sources = [];
    private static SimpleSvgSource? logoSource;

    internal static SimpleSvgSource Source(string name, bool dark = false) =>
        Source(name, dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(222, 222, 222));

    internal static SimpleSvgSource Source(string name, Color color)
    {
        var key = (name, color.ToArgb());
        if (sources.TryGetValue(key, out var cached)) return cached;
        var assembly = typeof(Icons).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Assets.Icons.{name}.svg", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var tint = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var svg = SimpleSvgSource.FromString(reader.ReadToEnd().Replace("currentColor", tint));
        sources.Add(key, svg);
        return svg;
    }

    internal static Image View(string name, double size = 22, bool dark = false) =>
        new Image().Source(Source(name, dark)).Width(size).Height(size).StretchMode(Stretch.Uniform);

    internal static Image View(string name, double size, Color color) =>
        new Image().Source(Source(name, color)).Width(size).Height(size).StretchMode(Stretch.Uniform);

    internal static Image LogoView(double width, double height)
    {
        if (logoSource == null)
        {
            var assembly = typeof(Icons).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Assets.logo.svg", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            logoSource = SimpleSvgSource.FromString(reader.ReadToEnd());
        }
        return new Image().Source(logoSource).Width(width).Height(height).StretchMode(Stretch.Uniform);
    }
}
