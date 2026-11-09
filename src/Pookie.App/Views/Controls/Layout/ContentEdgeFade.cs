using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

internal sealed class ContentEdgeFade(Color surface, bool fromTop = false) : Control
{
    internal static readonly MewProperty<double> StrengthProperty = MewProperty<double>.Register<ContentEdgeFade>("Strength", 1);
    internal double Strength { get => GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    internal const double BottomHeight = 40;
    internal const double PlayerOpacity = .5;
    private readonly GradientStop[] stops = Enumerable.Range(0, 17).Select(index =>
    {
        var position = index / 16d;
        var amount = fromTop ? 1 - position : position;
        var alpha = (byte)Math.Round(255 * amount * amount * (3 - 2 * amount));
        return new GradientStop(position, Color.FromArgb(alpha, surface.R, surface.G, surface.B));
    }).ToArray();

    private double paintedStrength = double.NaN;
    private GradientStop[] paintedStops = [];
    private WriteableBitmap? raster;
    private IImage? rasterImage;
    private IGraphicsFactory? rasterFactory;
    private double rasterHeight, rasterScale;

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == StrengthProperty) InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var strengthChanged = paintedStrength != Strength;
        if (strengthChanged)
        {
            paintedStrength = Strength;
            paintedStops = stops.Select(stop => new GradientStop(stop.Offset,
                Color.FromArgb((byte)Math.Round(stop.Color.A * Math.Clamp(Strength, 0, 1)), surface.R, surface.G, surface.B))).ToArray();
        }
        if (OperatingSystem.IsWindows() && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var graphics = Application.Current.GraphicsFactory;
            var scale = Math.Max(context.DpiScale, GetDpi() / 96d);
            var resized = raster == null || rasterFactory != graphics || rasterHeight != Bounds.Height || rasterScale != scale;
            if (resized)
            {
                ClearRaster();
                raster = new WriteableBitmap(1, (int)Math.Ceiling(Bounds.Height * scale), clear: false);
                rasterImage = BitmapImage.CreatePremultipliedView(graphics, raster);
                rasterFactory = graphics; rasterHeight = Bounds.Height; rasterScale = scale;
            }
            if (resized || strengthChanged)
            {
                // Each row is one premultiplied pixel. Updating the strip
                // directly avoids a new Direct2D target on each fade tick.
                using var write = raster!.LockForWrite();
                var pixels = write.PixelsBgra32;
                for (var y = 0; y < raster.PixelHeight; y++)
                {
                    var stop = Math.Min(16, (y + .5) / (rasterHeight * rasterScale) * 16);
                    var i = Math.Min(15, (int)stop);
                    var first = paintedStops[i].Color.A;
                    var alpha = (byte)Math.Round(first + (paintedStops[i + 1].Color.A - first) * (stop - i));
                    pixels[4 * y] = (byte)((surface.B * alpha + 127) / 255);
                    pixels[4 * y + 1] = (byte)((surface.G * alpha + 127) / 255);
                    pixels[4 * y + 2] = (byte)((surface.R * alpha + 127) / 255);
                    pixels[4 * y + 3] = alpha;
                }
            }
            context.DrawImage(rasterImage!, Bounds);
            return;
        }
        context.FillRectangle(Bounds, new LinearGradientBrush(new Point(Bounds.X, Bounds.Y), new Point(Bounds.X, Bounds.Bottom), paintedStops));
    }

    private void ClearRaster()
    {
        rasterImage?.Dispose(); rasterImage = null;
        raster?.Dispose(); raster = null; rasterFactory = null;
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) ClearRaster();
    }
    protected override void OnDispose() { ClearRaster(); base.OnDispose(); }
}
