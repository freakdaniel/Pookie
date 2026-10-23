using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;
using System.Xml.Linq;
using System.Numerics;

namespace Pookie.App;

internal static class Icons
{
    private static readonly Dictionary<(string, uint), IVectorImageSource> sources = [];
    private static IVectorImageSource? logoSource;
    private static PathGeometry? logoGeometry;

    internal static IVectorImageSource Source(string name, bool dark = false) =>
        Source(name, dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(222, 222, 222));

    internal static IVectorImageSource Source(string name, Color color)
    {
        var key = (name, color.ToArgb());
        if (sources.TryGetValue(key, out var cached)) return cached;
        var assembly = typeof(Icons).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Assets.Icons.{name}.svg", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var tint = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var svg = new ImmutableIconSource(SimpleSvgSource.FromString(reader.ReadToEnd().Replace("currentColor", tint)));
        sources.Add(key, svg);
        return svg;
    }

    internal static Image View(string name, double size = 22, bool dark = false) =>
        new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Source(Source(name, dark)).Width(size).Height(size).StretchMode(Stretch.Uniform);

    internal static Image View(string name, double size, Color color) =>
        new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Source(Source(name, color)).Width(size).Height(size).StretchMode(Stretch.Uniform);

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
            logoSource = new ImmutableIconSource(SimpleSvgSource.FromString(reader.ReadToEnd()));
        }
        return new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Source(logoSource).Width(width).Height(height).StretchMode(Stretch.Uniform);
    }

    // These cached SVGs never mutate. Exposing SimpleSvgSource's Changed event would let
    // each Image subscribe to a process-wide source and retain its removed parent card.
    private sealed class ImmutableIconSource(SimpleSvgSource source) : IVectorImageSource
    {
        private readonly List<(IGraphicsFactory Factory, int Width, int Height, IRenderSurface Surface, IImage Image)> rasters = [];
        public Size IntrinsicSize => source.IntrinsicSize;
        public void Render(IGraphicsContext context, Rect destRect)
        {
            if (destRect.Width <= 0 || destRect.Height <= 0) return;
            // Direct2D target bitmaps cannot be shared with every independent
            // offscreen target. Its native path cache already retains geometry;
            // draw those paths directly for both the window and glass captures.
            if (OperatingSystem.IsWindows()) { source.Render(context, destRect); return; }
            var transform = context.GetTransform();
            var scale = context.DpiScale * Math.Max(Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12),
                Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22));
            var width = Math.Clamp((int)Math.Ceiling(destRect.Width * scale), 1, 2048);
            var height = Math.Clamp((int)Math.Ceiling(destRect.Height * scale), 1, 2048);
            var graphics = Application.Current.GraphicsFactory;
            lock (rasters)
                if (DrawRaster(context, graphics, width, height, destRect)) return;
            // Render outside the cache lock. A vector worker may already own
            // the GL scope; waiting for it while holding this lock would invert
            // the lock order between that worker and the UI thread.
            IRenderSurface raster;
            IImage image;
            using (graphics.AcquireBackgroundRenderScope())
            {
                // One immutable raster per icon/colour/size, shared by all rows.
                // Per-Image SVG caches otherwise create a render target and
                // repeat path tessellation for every newly revealed comment.
                raster = graphics.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1, debugName: "SharedIcon"));
                try
                {
                    using var renderer = graphics.CreateContext(raster);
                    renderer.BeginFrame(raster);
                    try { renderer.Clear(Color.Transparent); source.Render(renderer, new Rect(0, 0, width, height)); }
                    finally { renderer.EndFrame(); }
                    image = graphics.CreateImageView(raster);
                }
                catch { raster.Dispose(); throw; }
            }
            lock (rasters)
            {
                if (DrawRaster(context, graphics, width, height, destRect)) { image.Dispose(); raster.Dispose(); return; }
                if (rasters.Count == 8)
                {
                    rasters[0].Image.Dispose(); rasters[0].Surface.Dispose(); rasters.RemoveAt(0);
                }
                rasters.Add((graphics, width, height, raster, image));
                context.DrawImage(image, destRect);
            }
        }

        private bool DrawRaster(IGraphicsContext context, IGraphicsFactory graphics, int width, int height, Rect destRect)
        {
            var index = rasters.FindIndex(entry => entry.Factory == graphics && entry.Width == width && entry.Height == height);
            if (index < 0) return false;
            var entry = rasters[index];
            rasters.RemoveAt(index); rasters.Add(entry);
            context.DrawImage(entry.Image, destRect);
            return true;
        }
        public IImage CreateImage(IGraphicsFactory factory) => source.CreateImage(factory);

        internal void ReleaseRasters()
        {
            lock (rasters)
            {
                foreach (var entry in rasters) { entry.Image.Dispose(); entry.Surface.Dispose(); }
                rasters.Clear();
            }
        }
    }

    internal static void ReleaseRasters()
    {
        foreach (var source in sources.Values.OfType<ImmutableIconSource>()) source.ReleaseRasters();
        (logoSource as ImmutableIconSource)?.ReleaseRasters();
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
