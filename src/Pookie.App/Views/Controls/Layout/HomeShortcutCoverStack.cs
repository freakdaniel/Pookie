using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Moves the newly played artwork from the left onto the southwest front
// of the stack while the previous cover recedes and becomes translucent.
internal sealed class HomeShortcutCoverStack : Panel
{
    private sealed record Layer(Border Frame, Image Image);

    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(520), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly IncomingCoverRaster incomingRaster = new();
    private Layer rear, front, incoming;
    private IImageSource? targetFront;
    private double amount;

    internal IImageSource? FrontSource => targetFront ?? front.Image.Source;

    internal HomeShortcutCoverStack()
    {
        Width = Height = 48;
        ClipToBounds = true;
        rear = CreateLayer(); front = CreateLayer(); incoming = CreateLayer();
        SetLayerOrder(rear, front, incoming);
        SetRestingPositions();
        motion.TickCallback = value => { amount = value; InvalidateArrange(); };
        motion.CompletedCallback = FinishMotion;
    }

    internal void SetCovers(IImageSource? frontSource, IImageSource? rearSource, bool animate)
    {
        if (motion.IsRunning) FinishMotion();
        if (!animate || frontSource == null || front.Image.Source == null)
        {
            targetFront = frontSource;
            front.Image.Source = frontSource;
            rear.Image.Source = rearSource;
            incoming.Image.Source = null;
            SetLayerOrder(rear, front, incoming);
            SetRestingPositions();
            InvalidateArrange();
            return;
        }

        incoming.Image.Source = frontSource;
        targetFront = frontSource;
        // The new cover must finish on top. Start it outside the stack so it
        // does not abruptly cover the old artwork on the first animation frame.
        SetLayerOrder(rear, front, incoming);
        amount = 0;
        InvalidateArrange();
        motion.Start();
    }

    internal void SetRear(IImageSource? source)
    {
        if (motion.IsRunning) FinishMotion();
        rear.Image.Source = source;
        rear.Frame.Opacity = source == null ? 0 : .45;
        InvalidateVisual();
    }

    private static Layer CreateLayer()
    {
        var image = new Image().StretchMode(Stretch.UniformToFill);
        var frame = new Border().CornerRadius(5).ClipToBounds().Background(Color.FromRgb(43, 43, 43)).Child(image);
        return new(frame, image);
    }

    private void SetRestingPositions()
    {
        rear.Frame.Opacity = rear.Image.Source == null ? 0 : .45;
        front.Frame.Opacity = front.Image.Source == null ? 0 : 1;
        incoming.Frame.Opacity = 0;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        foreach (var layer in Children) layer.Measure(new Size(48, 48));
        return new Size(48, 48);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var eased = amount;
        Arrange(rear.Frame, Mix(8, 14, eased), 0, Mix(40, 34, eased), .45 * (1 - eased));
        // The old front ends exactly at the normal rear-cover position. Keeping
        // its size constant avoids a resize jump as the animation completes.
        Arrange(front.Frame, Mix(0, 8, eased), Mix(8, 0, eased), 40, Mix(1, .45, eased));
        Arrange(incoming.Frame, Mix(-40, 0, eased), 8, 40, eased);
        return finalSize;

        void Arrange(Border frame, double x, double y, double size, double opacity)
        {
            frame.Width = frame.Height = size;
            frame.Opacity = Math.Clamp(opacity, 0, 1);
            frame.Arrange(new Rect(Bounds.X + x, Bounds.Y + y, size, size));
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (amount <= 0 || amount >= 1)
        {
            base.RenderSubtree(context);
            return;
        }

        context.Save();
        try
        {
            context.IntersectClip(Bounds);
            rear.Frame.Render(context);
            front.Frame.Render(context);
            // Blend the mask itself in and out. It must reach zero before the
            // raster is replaced by normal rendering at the resting position.
            var maskStrength = SmoothStep(amount / .2) * SmoothStep((1 - amount) / .3);
            incomingRaster.Draw(context, incoming.Frame, Bounds, 12, maskStrength);
        }
        finally { context.Restore(); }
    }

    private static double Mix(double from, double to, double amount) => from + (to - from) * amount;

    private static double SmoothStep(double value)
    {
        var clamped = Math.Clamp(value, 0, 1);
        return clamped * clamped * (3 - 2 * clamped);
    }

    private void FinishMotion()
    {
        motion.Stop();
        var expiredRear = rear;
        rear = front;
        front = incoming;
        incoming = expiredRear;
        incoming.Image.Source = null;
        targetFront = front.Image.Source;
        SetLayerOrder(incoming, rear, front);
        amount = 0;
        SetRestingPositions();
        InvalidateArrange();
    }

    private void SetLayerOrder(params Layer[] layers)
    {
        var children = ChildrenList;
        children.Clear();
        foreach (var layer in layers) children.Add(layer.Frame);
    }

    protected override void OnDispose()
    {
        motion.Stop(); incomingRaster.Dispose();
        base.OnDispose();
    }

    // Fade the incoming cover at the stack's left clipping edge so it enters
    // smoothly instead of revealing a hard vertical crop while sliding in.
    private sealed class IncomingCoverRaster : IDisposable
    {
        private IGraphicsFactory? factory;
        private ICpuPixelSurface? surface;
        private IGraphicsContext? renderer;
        private IImage? image;
        private double dpi;

        internal void Draw(IGraphicsContext context, FrameworkElement cover, Rect viewport, double fadeWidth, double maskStrength)
        {
            if (!cover.IsVisible || cover.Opacity <= 0) return;
            var scale = context.DpiScale;
            var x = Math.Floor(cover.Bounds.X * scale) / scale;
            var y = Math.Floor(cover.Bounds.Y * scale) / scale;
            var width = Math.Max(1, (int)Math.Ceiling((cover.Bounds.Right - x) * scale));
            var height = Math.Max(1, (int)Math.Ceiling((cover.Bounds.Bottom - y) * scale));
            var graphics = Application.Current.GraphicsFactory;
            if (surface == null || factory != graphics || dpi != scale || surface.PixelWidth != width || surface.PixelHeight != height)
            {
                Dispose(); factory = graphics; dpi = scale;
                surface = (ICpuPixelSurface)graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, scale,
                    premultiplied: true, debugName: "HomeCoverEdgeFade"));
                renderer = graphics.CreateContext(surface);
                image = graphics.CreateImageView(surface);
            }

            renderer!.BeginFrame(surface);
            renderer.Clear(Color.Transparent);
            renderer.Translate(-x, -y);
            cover.Render(renderer);
            renderer.EndFrame();

            var pixels = surface.GetWritablePixelSpan();
            var premultiplied = surface.Capabilities.HasFlag(SurfaceCapabilities.Premultiplied);
            for (var column = 0; column < width; column++)
            {
                var position = x + (column + .5) / scale;
                var fraction = Math.Clamp((position - viewport.X) / fadeWidth, 0, 1);
                var alpha = 1 - maskStrength * (1 - SmoothStep(fraction));
                if (alpha == 1) continue;
                for (var row = 0; row < height; row++)
                {
                    var index = row * surface.StrideBytes + column * 4;
                    if (premultiplied)
                        for (var channel = 0; channel < 3; channel++) pixels[index + channel] = (byte)Math.Round(pixels[index + channel] * alpha);
                    pixels[index + 3] = (byte)Math.Round(pixels[index + 3] * alpha);
                }
            }
            surface.IncrementVersion(); surface.CommitCpuWrite();
            context.DrawImage(image!, new Rect(x, y, width / scale, height / scale));
        }

        public void Dispose()
        {
            image?.Dispose(); renderer?.Dispose(); surface?.Dispose();
            image = null; renderer = null; surface = null; factory = null;
        }
    }
}
