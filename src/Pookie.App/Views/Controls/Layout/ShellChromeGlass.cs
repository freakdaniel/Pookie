using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

internal sealed class ShellChromeGlass(GlassContentFrame content, Color surface) : Control
{
    internal const double HeaderHeight = 64;
    private const double BlurRadius = 24;
    private IRenderSurface? raster;
    private IGraphicsContext? renderer;
    private IGraphicsFactory? factory;
    private nint contextIdentity;
    private BackdropBlur? blurred;
    private Win32BackdropBlur? gpuBlur;
    private double amount;
    internal double GlassAmount => amount;
    internal bool HasBlurredBackdrop => blurred != null || gpuBlur != null;
    internal bool UsesGpuBlur => gpuBlur != null;

    internal void Update(double offset)
    {
        var value = Math.Clamp(offset / 48, 0, 1);
        var next = value * value * (3 - 2 * value);
        if (amount == next && offset == lastOffset) return;
        lastOffset = offset; amount = next;
        InvalidateVisual();
    }
    private double lastOffset = -1;
    internal void RefreshBackdrop() => InvalidateVisual();

    protected override void OnRender(IGraphicsContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        const double scale = .25;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling((Bounds.Height + BlurRadius * 2) * scale));
        var graphics = Application.Current.GraphicsFactory;
        // Retained scene recording can call OnRender without a current GPU
        // context. Activate one before drawing and reading the backdrop strip.
        using var rendering = graphics.AcquireBackgroundRenderScope();
        var currentContext = Win32BackdropBlur.CurrentContextIdentity;
        // Textures can be shared by the worker and window contexts; FBOs
        // cannot. Never bind an intermediate framebuffer in a foreign context.
        if (raster == null || raster.PixelWidth != width || raster.PixelHeight != height || raster.DpiScale != scale || factory != graphics || contextIdentity != currentContext)
        {
            ClearRaster(); factory = graphics; contextIdentity = currentContext;
            raster = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(width, height, scale, debugName: "ShellChromeGlass"));
            renderer = graphics.CreateContext(raster);
            if (currentContext != 0 && raster is IGpuTextureSource && raster is IExternalWritableGpuSurface)
            {
                try { gpuBlur = new Win32BackdropBlur(graphics, width, height, scale, BlurRadius * scale / 3); }
                catch (NotSupportedException error)
                { Pookie.Logging.AppLog.For("Pookie.Rendering").Warning(error, "GPU backdrop blur unavailable; using bounded CPU blur"); }
            }
            if (gpuBlur == null) blurred = new BackdropBlur(graphics, width, height);
        }
        // Compile the small blur pipeline on the first resting frame rather
        // than on the user's first scroll input.
        if (amount <= .001) { context.FillRectangle(Bounds, surface); return; }
        // Refresh the narrow strip only on a requested UI frame, so late artwork and
        // animations remain live without starting a separate repaint loop.
        renderer!.BeginFrame(raster!);
        try
        {
            renderer.Clear(surface);
            renderer.Translate(-Bounds.X, BlurRadius - Bounds.Y);
            renderer.SetClip(new Rect(Bounds.X, Bounds.Y - BlurRadius, width / scale, height / scale));
            content.DrawBackdrop(renderer, new Rect(Bounds.X, Bounds.Y - BlurRadius, width / scale, height / scale));
        }
        finally { renderer.EndFrame(); }
        if (gpuBlur != null) gpuBlur.Update(raster!);
        else blurred!.Update((ICpuPixelSurface)raster!, BlurRadius * scale / 3);
        context.Save();
        try
        {
            context.SetClip(Bounds);
            context.DrawImage(gpuBlur?.Image ?? blurred!.Image, new Rect(Bounds.X, Bounds.Y - BlurRadius, width / scale, height / scale));
            // The settled header remains translucent; the bottom edge has no hard rule.
            context.FillRectangle(Bounds, Color.FromArgb((byte)Math.Round(255 - 88 * amount), surface.R, surface.G, surface.B));
        }
        finally { context.Restore(); }
    }

    private void ClearRaster()
    {
        blurred?.Dispose(); blurred = null;
        gpuBlur?.Dispose(); gpuBlur = null;
        renderer?.Dispose(); renderer = null; raster?.Dispose(); raster = null;
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) ClearRaster();
    }

    protected override void OnDispose() { ClearRaster(); base.OnDispose(); }
}

internal sealed class ContentBottomFade(Color surface) : Control
{
    internal static readonly MewProperty<double> StrengthProperty = MewProperty<double>.Register<ContentBottomFade>("Strength", 1);
    internal double Strength { get => GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    internal const double FadeHeight = 40;
    internal const double PlayerOpacity = .5;
    private readonly GradientStop[] stops = Enumerable.Range(0, 17).Select(index =>
    {
        var position = index / 16d;
        var alpha = (byte)Math.Round(255 * position * position * (3 - 2 * position));
        return new GradientStop(position, Color.FromArgb(alpha, surface.R, surface.G, surface.B));
    }).ToArray();

    private double paintedStrength = double.NaN;
    private GradientStop[] paintedStops = [];

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
        }
        context.FillRectangle(Bounds, new LinearGradientBrush(new Point(Bounds.X, Bounds.Y), new Point(Bounds.X, Bounds.Bottom), paintedStops));
    }
}
