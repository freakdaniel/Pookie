using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;
using System.Xml.Linq;
using System.Numerics;

namespace Pookie.App;

internal static class Icons
{
    internal const string MissingTrackArtwork = "missing-track-artwork";
    private static readonly Dictionary<(string, uint, int), IVectorImageSource> sources = [];
    private static readonly Dictionary<string, string> templates = [];
    internal static int CachedSourceCount => sources.Count;
    internal static long RasterBuildCount { get; private set; }
    private static IVectorImageSource? logoSource;
    private static PathGeometry? logoGeometry;

    internal static IVectorImageSource Source(string name, bool dark = false) =>
        Source(name, dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(222, 222, 222));

    internal static IVectorImageSource Source(string name, Color color, bool dimmed = false)
    {
        var key = (name, color.ToArgb(), dimmed ? -1 : 1);
        if (sources.TryGetValue(key, out var cached)) return cached;
        var svg = CreateTintedSource(name, color, cacheRaster: true, dimmed: dimmed);
        sources.Add(key, svg);
        return svg;
    }

    // Intermediate hover/selection colours belong to one animation frame. A
    // process-wide entry for each shade retained its SVG and GPU rasters forever.
    internal static IVectorImageSource TransitionSource(string name, Color color, bool dimmed = false) =>
        CreateTintedSource(name, color, cacheRaster: false, dimmed: dimmed);

    private static IVectorImageSource CreateTintedSource(string name, Color color, bool cacheRaster, int rasterBucket = 1, bool dimmed = false)
    {
        if (!templates.TryGetValue(name, out var template))
        {
            var assembly = typeof(Icons).Assembly;
            var resource = $"Pookie.App.Assets.Icons.{name}.svg";
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            templates[name] = template = reader.ReadToEnd();
        }
        var tint = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var markup = template.Replace("currentColor", tint);
        if (dimmed)
        {
            // Blend the SVG once into its shared raster, including the
            // overlapping paths, instead of blending a layer on every row.
            var root = XElement.Parse(markup);
            var ink = new XElement(root.Name.Namespace + "g", new XAttribute("opacity", "0.38"), root.Nodes());
            root.ReplaceNodes(ink);
            markup = root.ToString(SaveOptions.DisableFormatting);
        }
        return new ImmutableIconSource(SimpleSvgSource.FromString(markup), cacheRaster, rasterBucket);
    }

    internal static IVectorImageSource? CountryFlag(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var country = code.Trim().ToLowerInvariant();
        country = country switch { "uk" => "gb", "el" => "gr", _ => country };
        if (country.Length != 2 || !country.All(char.IsAsciiLetter)) return null;
        var key = ("flag:" + country, 0u, 1);
        if (sources.TryGetValue(key, out var cached)) return cached;
        using var stream = typeof(Icons).Assembly.GetManifestResourceStream($"Pookie.App.Assets.Flags.{country}.svg");
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return sources[key] = new ImmutableIconSource(SimpleSvgSource.FromString(reader.ReadToEnd()));
    }

    internal static Image View(string name, double size = 22, bool dark = false) =>
        new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Source(Source(name, dark)).Width(size).Height(size).StretchMode(Stretch.Uniform);

    internal static Image View(string name, double size, Color color) =>
        new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Source(Source(name, color)).Width(size).Height(size).StretchMode(Stretch.Uniform);

    internal static Image TintableView(string name, double size, out SimpleSvgSource source)
    {
        source = SimpleSvgSource.FromResource(typeof(Icons).Assembly, $"Pookie.App.Assets.Icons.{name}.svg");
        return new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Source(source).Width(size).Height(size).StretchMode(Stretch.Uniform);
    }

    // A few oversampled sizes cover continuous scale changes without creating
    // an SVG surface for every intermediate physical pixel width.
    internal static Image ScalingView(string name, double size, bool dark = false)
    {
        var color = dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(222, 222, 222);
        var key = (name, color.ToArgb(), 16);
        if (!sources.TryGetValue(key, out var source))
            sources[key] = source = CreateTintedSource(name, color, cacheRaster: true, rasterBucket: 16);
        return new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Source(source).Width(size).Height(size).StretchMode(Stretch.Uniform);
    }

    internal static PathShape PlaybackDisc(bool pause, double size, double glyphSize, Color color)
    {
        var name = pause ? "pause-solid" : "play-solid";
        var assembly = typeof(Icons).Assembly;
        var resource = $"Pookie.App.Assets.Icons.{name}.svg";
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
            var resource = "Pookie.App.Assets.logo.svg";
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            logoSource = new ImmutableIconSource(SimpleSvgSource.FromString(reader.ReadToEnd()));
        }
        return new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Source(logoSource).Width(width).Height(height).StretchMode(Stretch.Uniform);
    }

    // These cached SVGs never mutate. Exposing SimpleSvgSource's Changed event would let
    // each Image subscribe to a process-wide source and retain its removed parent card.
    private sealed class ImmutableIconSource(SimpleSvgSource source, bool cacheRaster = true, int rasterBucket = 1) : IVectorImageSource
    {
        private readonly List<(IGraphicsFactory Factory, int Width, int Height, IRenderSurface Surface, IImage Image)> rasters = [];
        public Size IntrinsicSize => source.IntrinsicSize;
        public void Render(IGraphicsContext context, Rect destRect)
        {
            if (destRect.Width <= 0 || destRect.Height <= 0) return;
            if (!cacheRaster) { source.Render(context, destRect); return; }
            var transform = context.GetTransform();
            var scale = context.DpiScale * Math.Max(Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12),
                Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22));
            var width = Math.Clamp((int)Math.Ceiling(destRect.Width * scale), 1, 2048);
            var height = Math.Clamp((int)Math.Ceiling(destRect.Height * scale), 1, 2048);
            if (rasterBucket > 1)
            {
                width = Math.Min(2048, (width + rasterBucket - 1) / rasterBucket * rasterBucket);
                height = Math.Min(2048, (height + rasterBucket - 1) / rasterBucket * rasterBucket);
            }
            // SVG drawing replaces the current transform. Record the shared
            // bitmap instead so a moving row can replay the icon at its new
            // position, including inside the action strip's opacity layer.
            if (OperatingSystem.IsWindows() && rasterBucket == 1 && Math.Max(width, height) <= 64 &&
                !WindowPresentation.IsRecording(context))
            {
                source.Render(context, destRect);
                return;
            }
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
                var cpu = BackdropSurface.UsesCpuCapture;
                raster = graphics.CreateSurface(cpu
                    ? RenderSurfaceDescriptor.CpuPixels(width, height, 1, debugName: "SharedIcon")
                    : RenderSurfaceDescriptor.CachedImage(width, height, 1, debugName: "SharedIcon"));
                try
                {
                    using var renderer = graphics.CreateContext(raster);
                    renderer.BeginFrame(raster);
                    try { renderer.Clear(Color.Transparent); source.Render(renderer, new Rect(0, 0, width, height)); }
                    finally { renderer.EndFrame(); }
                    image = cpu && raster is IPixelBufferSource pixels
                        ? BitmapImage.CreatePremultipliedView(graphics, pixels) : graphics.CreateImageView(raster);
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
                RasterBuildCount++;
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
            var resource = "Pookie.App.Assets.logo.svg";
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
