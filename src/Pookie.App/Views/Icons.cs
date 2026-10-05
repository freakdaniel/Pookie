using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;
using System.Xml.Linq;
using System.Numerics;

namespace Pookie.App;

internal static class Icons
{
    private static readonly Dictionary<(string, uint), SimpleSvgSource> sources = [];
    private static SimpleSvgSource? logoSource;
    private static PathGeometry? logoGeometry;

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

    internal static PathShape PlaybackDisc(bool pause, double size, double glyphSize, Color color)
    {
        var name = pause ? "pause-solid" : "play-solid";
        var assembly = typeof(Icons).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Assets.Icons.{name}.svg", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        var svg = XDocument.Load(stream);
        var glyph = PathGeometry.Parse(svg.Descendants().Single(element => element.Name.LocalName == "path").Attribute("d")!.Value);
        var inset = (float)((size - glyphSize) / 2);
        var transform = Matrix3x2.CreateScale((float)(glyphSize / 256)) * Matrix3x2.CreateTranslation(inset, inset);
        // Even-odd filling leaves an actual transparent hole, including along antialiased edges.
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
        geometry.AddPath(PathGeometry.FromCircle(size / 2, size / 2, size / 2));
        geometry.AddPath(glyph.Transform(transform));
        geometry.Freeze();
        return new PathShape().Data(geometry).Width(size).Height(size).Stretch(Stretch.Uniform).Fill(color);
    }

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

    internal static PathShape LogoMark(double width, double height)
    {
        if (logoGeometry == null)
        {
            var assembly = typeof(Icons).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Assets.logo.svg", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var svg = XDocument.Load(stream);
            var path = svg.Descendants().Single(element => element.Name.LocalName == "path");
            logoGeometry = new PathGeometry { FillRule = FillRule.EvenOdd };
            logoGeometry.AddPath(PathGeometry.Parse(path.Attribute("d")!.Value));
            logoGeometry.Freeze();
        }
        return new PathShape().Data(logoGeometry).Width(width).Height(height).Stretch(Stretch.Uniform).Fill(Color.White);
    }

    internal static Image SoundCloudMark(double size) => new Image()
        .Source(ImageSource.FromResource<MainWindow>("Pookie.App.Assets.Branding.soundcloud-mark-white.png"))
        .Width(size).Height(size).StretchMode(Stretch.Uniform);
}
