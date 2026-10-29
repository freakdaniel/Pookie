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
    private RenderDeviceIdentity? deviceIdentity;
    private BackdropBlur? blurred;
    private Win32BackdropBlur? gpuBlur;
    private NativeBackdropBlur? nativeBlur;
    private double amount;
    internal double GlassAmount => amount;
    internal bool HasBlurredBackdrop => blurred != null || gpuBlur != null || nativeBlur != null;
    internal bool UsesGpuBlur => gpuBlur != null || nativeBlur?.Result != null;

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
        var graphics = content.CaptureGraphics;
        // Retained scene recording can call OnRender without a current GPU
        // context. Activate one before drawing and reading the backdrop strip.
        using var rendering = graphics.AcquireBackgroundRenderScope();
        var currentContext = Win32BackdropBlur.CurrentContextIdentity;
        var currentDevice = (graphics as IRenderDevice)?.RenderIdentity;
        // Textures can be shared by the worker and window contexts; FBOs
        // cannot. Never bind an intermediate framebuffer in a foreign context.
        if (raster == null || raster.PixelWidth != width || raster.PixelHeight != height || raster.DpiScale != scale || factory != graphics || contextIdentity != currentContext || deviceIdentity != currentDevice)
        {
            ClearRaster(); factory = graphics; contextIdentity = currentContext;
            raster = content.CreateCaptureSurface(graphics, width, height, scale, "ShellChromeGlass");
            renderer = graphics.CreateContext(raster);
            deviceIdentity = (graphics as IRenderDevice)?.RenderIdentity;
            if (currentContext != 0 && raster is IGpuTextureSource && raster is IExternalWritableGpuSurface)
            {
                try { gpuBlur = new Win32BackdropBlur(graphics, width, height, scale, BlurRadius * scale / 3); }
                catch (NotSupportedException error)
                { Pookie.Logging.AppLog.For("Pookie.Rendering").Warning(error, "GPU backdrop blur unavailable; using bounded CPU blur"); }
            }
            if (gpuBlur == null && !(BackdropSurface.UsesCpuCapture && raster is IGpuTextureSource))
                blurred = new BackdropBlur(graphics, width, height);
        }
        var native = BackdropSurface.UsesCpuCapture && raster is IGpuTextureSource;
        // The native Gaussian has a transparent halo outside its source. Keep
        // that halo beyond every visible edge, including the left/right edges.
        var padding = native ? BlurRadius * 2 : BlurRadius;
        var region = native
            ? new Rect(Bounds.X - padding, Bounds.Y - padding, Bounds.Width + padding * 2, Bounds.Height + padding * 2)
            : new Rect(Bounds.X, Bounds.Y - padding, width / scale, height / scale);
        // Warm resources on the resting frame instead of allocating them on
        // the first wheel event. The native path never asks for CPU pixels.
        if (amount <= .001)
        {
            if (native && nativeBlur?.Result == null)
            {
                nativeBlur ??= new NativeBackdropBlur(graphics, BlurRadius);
                nativeBlur.Update(content.CaptureBackdrop(region, surface), region);
            }
            else if (BackdropSurface.UsesCpuCapture && blurred is { Ready: false })
                blurred.UpdateReduced((ICpuPixelSurface)content.CaptureBackdrop(region, surface), BlurRadius * scale / 3);
            context.FillRectangle(Bounds, surface); return;
        }
        // Refresh the narrow strip only on a requested UI frame, so late artwork and
        // animations remain live without starting a separate repaint loop.
        // Finish capture/reduction before beginning the destination pass. D2D
        // offscreen surfaces on one factory share its thread's draw context.
        var captured = content.CaptureBackdrop(region, surface);
        if (native && captured is IGpuTextureSource)
        {
            nativeBlur ??= new NativeBackdropBlur(graphics, BlurRadius);
            nativeBlur.Update(captured, region);
        }
        else if (BackdropSurface.UsesCpuCapture)
        {
            nativeBlur?.Dispose(); nativeBlur = null;
            // Keep exact area sampling on Windows. GPU bilinear reductions
            // round moving pixel footprints differently at fractional DPI.
            // The independent capture can finish before its one readback.
            blurred ??= new BackdropBlur(graphics, width, height);
            blurred.UpdateReduced((ICpuPixelSurface)captured, BlurRadius * scale / 3);
        }
        else
        {
            var prepared = content.PrepareBackdrop(captured, region, surface, scale);
            renderer!.BeginFrame(raster!);
            try
            {
                renderer.Clear(surface);
                renderer.Translate(-Bounds.X, BlurRadius - Bounds.Y);
                renderer.SetClip(region);
                renderer.DrawImage(prepared.Image, prepared.Bounds);
            }
            finally { renderer.EndFrame(); }
            if (gpuBlur != null) gpuBlur.Update(raster!);
            else blurred!.Update((ICpuPixelSurface)raster!, BlurRadius * scale / 3);
        }
        context.Save();
        try
        {
            context.SetClip(Bounds);
            context.DrawImage(nativeBlur?.Result?.AsImage() ?? gpuBlur?.Image ?? blurred!.Image,
                nativeBlur?.Result?.Bounds ?? region);
            // The settled header remains translucent; the bottom edge has no hard rule.
            context.FillRectangle(Bounds, Color.FromArgb((byte)Math.Round(255 - 88 * amount), surface.R, surface.G, surface.B));
        }
        finally { context.Restore(); }
    }

    private void ClearRaster()
    {
        nativeBlur?.Dispose(); nativeBlur = null;
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
