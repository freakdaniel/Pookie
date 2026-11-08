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
    private DecorationRaster? raster;
    private IGraphicsFactory? rasterFactory;
    private double rasterHeight, rasterScale;

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == StrengthProperty) InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (paintedStrength != Strength)
        {
            paintedStrength = Strength;
            paintedStops = stops.Select(stop => new GradientStop(stop.Offset,
                Color.FromArgb((byte)Math.Round(stop.Color.A * Math.Clamp(Strength, 0, 1)), surface.R, surface.G, surface.B))).ToArray();
            ClearRaster();
        }
        if (OperatingSystem.IsWindows() && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var graphics = Application.Current.GraphicsFactory;
            var scale = Math.Max(context.DpiScale, GetDpi() / 96d);
            if (raster == null || rasterFactory != graphics || rasterHeight != Bounds.Height || rasterScale != scale)
            {
                ClearRaster();
                // A vertical gradient is identical in every column. Cache a
                // narrow strip at native DPI instead of rebuilding a Direct2D
                // gradient brush on every scrolling frame.
                raster = new DecorationRaster(graphics, new Size(1 / scale, Bounds.Height), scale, (target, bounds) =>
                    target.FillRectangle(bounds, new LinearGradientBrush(new Point(0, 0), new Point(0, bounds.Bottom), paintedStops)));
                rasterFactory = graphics; rasterHeight = Bounds.Height; rasterScale = scale;
            }
            context.DrawImage(raster.Image, Bounds);
            return;
        }
        context.FillRectangle(Bounds, new LinearGradientBrush(new Point(Bounds.X, Bounds.Y), new Point(Bounds.X, Bounds.Bottom), paintedStops));
    }

    private void ClearRaster() { raster?.Dispose(); raster = null; rasterFactory = null; }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) ClearRaster();
    }
    protected override void OnDispose() { ClearRaster(); base.OnDispose(); }
}
