using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Capture in the window's paint order, before the glass and its foreground.
// Repainting any part of the sample must clear and paint its entire footprint.
internal sealed class LiveGlassBackdrop(FrameworkElement surface, Action<IGraphicsContext, Element> renderBehind,
    ChatPopupLayer? popup = null, Color? tint = null, Func<Rect>? captureArea = null, double maximumSampleSide = 512) : IDisposable
{
    private static readonly HashSet<LiveGlassBackdrop> active = [];
    private static readonly HashSet<Window> warming = [];
    private static readonly Dictionary<Window, OpenGlPopupLens> glLenses = [];
    private readonly Dictionary<GlassCard, CardInk> cards = [];
    private const double Halo = 40;
    private Window? window;
    private DialogSnapshot? sample;
    private Rect capturedBounds;
    private bool capturing;
    internal long Version { get; private set; }
    internal bool Ready => sample != null && capturedBounds == SampleBounds;
    internal bool HasGpuOptics => Ready && cards.Values.Any(ink => ink.Version == Version && ink.Output?.Image != null);
    internal Rect CaptureBounds => Ready ? capturedBounds : Rect.Empty;
    private Rect SurfaceBounds => popup?.SurfaceBounds ?? captureArea?.Invoke() ?? surface.Bounds;
    private Rect SampleBounds => SurfaceBounds.Inflate(Halo, Halo);
    private Color Tint => tint ?? Color.FromArgb(174, 22, 22, 27);
    private bool Visible
    {
        get
        {
            for (Element? ancestor = surface; ancestor is UIElement element; ancestor = ancestor.Parent)
                if (!element.IsVisible || element.Opacity <= 0) return false;
            return true;
        }
    }
    internal void Register(GlassCard card) => cards.Add(card, new());
    internal void Unregister(GlassCard card)
    {
        if (cards.Remove(card, out var ink)) ink.Output?.Dispose();
    }

    internal static void WarmWindow(Window owner)
    {
        if (!OperatingSystem.IsLinux()) return;
        warming.Add(owner);
        WindowPresentation.EnableLiveGlass(true);
        owner.Closed += () =>
        {
            if (warming.Remove(owner)) ReleaseHooksIfIdle();
            if (glLenses.Remove(owner, out var lens)) lens.Dispose();
        };
    }

    internal static void WarmInFrame(Window owner, IGraphicsContext frame)
    {
        if (!warming.Remove(owner)) return;
        try
        {
            if (OpenGlBackdropCopy.GetCurrentContext() == 0) return;
            var graphics = owner.GraphicsFactory;
            var lens = new OpenGlPopupLens(graphics);
            try
            {
                // Specialize both the filter and its composition in the
                // window's GL context while the startup screen is up.
                // Real menu textures have arbitrary dimensions. Warm a
                // non-power-of-two sample and output, like the actual lens.
                const int warmWidth = 67, warmHeight = 53;
                var dpi = frame.DpiScale;
                var bounds = new Rect(0, 0, warmWidth / dpi, warmHeight / dpi);
                using var warm = new DialogSnapshot(graphics, bounds, dpi * .5);
                using var output = new OpenGlPopupLens.Output(bounds.Size);
                if (!warm.CopyBackdrop(frame, new Rect(0, 0, warmWidth, warmHeight)))
                    warm.Capture(static _ => { }, PopupAppearance.AppBackground);
                warm.Blur(20);
                if (lens.Paint(output, warm, bounds, 8, dpi, Color.FromArgb(174, 22, 22, 27)))
                {
                    using var composite = new DialogSnapshot(graphics, bounds, 1);
                    composite.Capture(context =>
                    {
                        var shape = GlassSheen.Shape(bounds, new CornerRadius(8));
                        context.FillPath(shape, new ImageBrush(output.Image!, bounds, bounds, TileMode.None));
                    }, Color.Transparent);
                }
                glLenses.Add(owner, lens);
            }
            catch { lens.Dispose(); throw; }
        }
        catch (NotSupportedException error)
        {
            Pookie.Logging.AppLog.For("Pookie.Rendering").Warning(error, "GPU popup refraction unavailable");
        }
        finally { ReleaseHooksIfIdle(); }
    }

    private static void ReleaseHooksIfIdle()
    {
        if (active.Count == 0 && warming.Count == 0) WindowPresentation.EnableLiveGlass(false);
    }

    internal void Reset() => capturedBounds = Rect.Empty;

    internal void Attach(Window? source)
    {
        if (window == source) return;
        Dispose();
        window = source;
        if (source != null && cards.Count != 0 && active.Add(this) && active.Count == 1)
            WindowPresentation.EnableLiveGlass(true);
    }

    internal static void ExpandDamage(Window owner, IReadOnlyList<Rect> damage, Action<Rect> addDamage)
    {
        if (damage.Count == 0) return;
        bool changed;
        do
        {
            changed = false;
            foreach (var backdrop in active)
            {
                if (backdrop.window != owner || !backdrop.Visible) continue;
                var bounds = backdrop.SampleBounds.Intersect(new Rect(Point.Zero, owner.ClientSize));
                if (bounds.IsEmpty || !damage.Any(bounds.IntersectsWith) ||
                    damage.Any(area => area.Contains(bounds))) continue;
                addDamage(bounds);
                changed = true;
            }
        } while (changed);
    }

    internal void CaptureLive(IGraphicsContext context)
    {
        if (capturing || !Visible || popup is { Reveal: <= 0 } || SurfaceBounds.IsEmpty ||
            cards.Count == 0 || window?.Content is not { } root ||
            context.GetClipBoundsLocal() is { } clip && !clip.IntersectsWith(SampleBounds)) return;

        var owner = window;
        var requested = SampleBounds;
        var dpi = context.DpiScale;
        var left = Math.Max(0, Math.Floor(requested.X * dpi));
        var top = Math.Max(0, Math.Floor(requested.Y * dpi));
        var right = Math.Min(Math.Round(owner.ClientSize.Width * dpi), Math.Ceiling(requested.Right * dpi));
        var bottom = Math.Min(Math.Round(owner.ClientSize.Height * dpi), Math.Ceiling(requested.Bottom * dpi));
        if (right <= left || bottom <= top) return;
        var pixels = new Rect(left, top, right - left, bottom - top);
        var bounds = new Rect(left / dpi, top / dpi, pixels.Width / dpi, pixels.Height / dpi);
        var scale = Math.Min(dpi * .5, maximumSampleSide / Math.Max(bounds.Width, bounds.Height));

        capturing = true;
        try
        {
            if (sample == null || sample.Bounds != bounds || sample.Scale != scale)
            {
                sample?.Dispose();
                sample = new(owner.GraphicsFactory, bounds, scale);
            }
            var copied = context.GetTransform().IsIdentity && sample.CopyBackdrop(context, pixels);
            if (!copied)
            {
                sample.Capture(target =>
                {
                    target.SetClip(bounds);
                    renderBehind(target, root);
                }, PopupAppearance.AppBackground);
            }
            sample.Blur(20);
            capturedBounds = requested;
            Version++;
            if (copied && glLenses.TryGetValue(owner, out var lens))
            {
                foreach (var (card, ink) in cards)
                {
                    if (!card.IsVisible || card.Bounds.IsEmpty) continue;
                    var visual = PresentedBounds(card.Bounds);
                    if (ink.Output?.Size != card.Bounds.Size)
                    {
                        ink.Output?.Dispose();
                        ink.Output = new(card.Bounds.Size);
                    }
                    if (lens.Paint(ink.Output, sample, visual, card.CornerRadius * visual.Width / card.Bounds.Width, dpi, Tint))
                        ink.Version = Version;
                }
            }
        }
        finally { capturing = false; }
    }

    internal void Draw(GlassCard card, IGraphicsContext context, bool presented)
    {
        if (popup is { Reveal: <= 0 } || !cards.TryGetValue(card, out var ink) || card.Bounds.IsEmpty) return;
        var local = card.Bounds;
        var shape = ink.Geometry.Get(local, new CornerRadius(card.CornerRadius));
        if (!Ready)
        {
            if (popup == null) context.FillPath(shape, Tint);
            return;
        }
        if (ink.Version == Version && ink.Output?.Image is { } optical)
        {
            context.FillPath(shape, new ImageBrush(optical, new Rect(0, 0, optical.PixelWidth, optical.PixelHeight), local, TileMode.None));
            if (presented) card.PopupBackdropPaintVersion = Version;
            return;
        }
        var image = sample!.Image;
        var imageBounds = sample.ImageBounds;
        var visual = PresentedBounds(local);
        var scale = visual.Width / local.Width;
        // Convert window coordinates back to card coordinates before the
        // compositor applies the reveal transform to the card and its ink.
        var sampleX = local.X + (imageBounds.X - visual.X) / scale;
        var sampleY = local.Y + (imageBounds.Y - visual.Y) / scale;
        var centerX = local.X + local.Width / 2;
        var centerY = local.Y + local.Height / 2;
        var magnifyX = 1 + Math.Min(4, local.Width * .1) * 2 / local.Width;
        var magnifyY = 1 + Math.Min(4, local.Height * .1) * 2 / local.Height;
        var warped = new Rect(centerX + (sampleX - centerX) * magnifyX,
            centerY + (sampleY - centerY) * magnifyY,
            imageBounds.Width * magnifyX / scale, imageBounds.Height * magnifyY / scale);
        var source = new Rect(0, 0, image.PixelWidth / sample.Scale, image.PixelHeight / sample.Scale);
        context.FillPath(shape, new ImageBrush(image, source, warped, TileMode.None));
        context.FillPath(shape, Tint);
        if (presented) card.PopupBackdropPaintVersion = Version;
    }

    private Rect PresentedBounds(Rect bounds) => popup?.PresentedBounds(bounds) ?? bounds;

    public void Dispose()
    {
        if (active.Remove(this)) ReleaseHooksIfIdle();
        window = null;
        sample?.Dispose();
        sample = null;
        foreach (var ink in cards.Values) { ink.Output?.Dispose(); ink.Output = null; }
        capturedBounds = Rect.Empty;
    }

    private sealed class CardInk
    {
        internal readonly GlassGeometry Geometry = new();
        internal OpenGlPopupLens.Output? Output;
        internal long Version;
    }
}
