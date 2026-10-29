using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// One continuous colour field behind the panel title, avatar and name.
internal sealed class ChatProfileBackdrop : GlassCard
{
    private readonly GlassGeometry geometry = new();
    private const int Rows = 96;
    private readonly Color surface;
    private readonly AnimationClock reveal = new(TimeSpan.FromMilliseconds(650), Easing.CubicBezier(.2, 0, 0, 1));
    private PlayerGradient current, from, target;
    private WriteableBitmap? raster;
    private IImage? image;
    private IGraphicsFactory? factory;
    private PlayerGradient painted;
    private float[] noise = [];
    private byte[] alpha = [];
    private bool dirty = true;
    private double scrollOffset;
    internal double ScrollOffset
    {
        get => scrollOffset;
        set
        {
            var next = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 270);
            if (scrollOffset == next) return;
            scrollOffset = next; InvalidateVisual();
        }
    }
    internal bool Animating => reveal.IsRunning;
    internal PlayerPalette Target => target.ToPalette();

    internal ChatProfileBackdrop(Color surface) : base(surface)
    {
        this.surface = surface; Background = surface;
        current = from = target = PlayerGradient.FromPalette(new(surface, surface, surface));
        reveal.TickCallback = t => { current = from.Lerp(target, t); InvalidateVisual(); };
    }
    internal void SetPalette(PlayerPalette palette)
    {
        var next = PlayerGradient.FromPalette(palette);
        if (target == next) return;
        reveal.Stop(); from = current; target = next; reveal.Start();
    }
    internal void Reset()
    {
        reveal.Stop(); current = from = target = PlayerGradient.FromPalette(new(surface, surface, surface));
        ScrollOffset = 0;
        dirty = true; InvalidateVisual();
    }
    protected override void RenderGlassBackground(IGraphicsContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || scrollOffset >= 270) return;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * Math.Max(context.DpiScale, GetDpi() / 96d)));
        var graphics = Application.Current.GraphicsFactory;
        if (image == null || image.PixelWidth != width || factory != graphics)
        {
            ClearRaster(); factory = graphics;
            raster = new WriteableBitmap(width, Rows, clear: false, hasAlpha: true);
            image = OperatingSystem.IsWindows() ? BitmapImage.CreatePremultipliedView(graphics, raster) : BitmapImage.Create(graphics, raster);
            noise = new float[width * Rows];
            alpha = new byte[width * Rows];
            for (var y = 0; y < Rows; y++)
            {
                // Coverage and grain depend only on the raster size, not the
                // animated palette. Compute them once per size/device.
                var fade = Math.Clamp((y / (Rows - 1d) - .45) / .55, 0, 1);
                fade = fade * fade * (3 - 2 * fade);
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    noise[i] = (float)PlayerGradient.Noise(x, y);
                    alpha[i] = (byte)Math.Clamp((1 - fade) * 255 + noise[i], 0, 255);
                }
            }
            dirty = true;
        }
        if (dirty || painted != current)
        {
            // Direct2D consumes premultiplied pixels; write that format here
            // instead of allocating/converting the entire image on every tick.
            var premultiplied = OperatingSystem.IsWindows();
            using var write = raster!.LockForWrite();
            var pixels = write.PixelsBgra32;
            for (var x = 0; x < width; x++)
            {
                var color = current.Sample((x + .5) / width);
                for (var y = 0; y < Rows; y++)
                {
                    var i = y * width + x; var grain = noise[i];
                    var a = alpha[i];
                    var b = (byte)(color.Z + grain); var g = (byte)(color.Y + grain); var r = (byte)(color.X + grain);
                    pixels[i * 4] = premultiplied ? (byte)((b * a + 127) / 255) : b;
                    pixels[i * 4 + 1] = premultiplied ? (byte)((g * a + 127) / 255) : g;
                    pixels[i * 4 + 2] = premultiplied ? (byte)((r * a + 127) / 255) : r;
                    // Fade into the actual glass, including its reflection and
                    // hover state, rather than an approximation of its base colour.
                    pixels[i * 4 + 3] = a;
                }
            }
            dirty = false; painted = current;
        }
        // The colour belongs to the profile header, so it leaves with the avatar.
        // Keep the glass itself anchored to the card while shifting only this layer.
        context.Save();
        try
        {
            context.SetClipPath(geometry.Get(Bounds, new CornerRadius(CornerRadius)));
            context.DrawImage(image!, new Rect(Bounds.X, Bounds.Y-scrollOffset, Bounds.Width, Math.Min(270, Bounds.Height)));
        }
        finally { context.Restore(); }
    }
    private void ClearRaster() { image?.Dispose(); image = null; raster?.Dispose(); raster = null; }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) ClearRaster();
    }

    protected override void OnDispose() { reveal.Stop(); ClearRaster(); base.OnDispose(); }
}
