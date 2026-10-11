using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Keep the editor and its draft in the same subtree while reclaiming the space
// occupied by the actions. Retarget from the visible position on interruption.
internal sealed class TrackCommentActionRow : Panel
{
    private readonly FrameworkElement leading, trailing, composer;
    private readonly AnimationClock clock = new(TimeSpan.FromMilliseconds(280), Easing.CubicBezier(.2, 0, 0, 1));
    private double amount, from, target, leadingWidth, trailingWidth;
    private readonly ActionRaster leadingRaster = new(), trailingRaster = new();
    internal bool Expanded { get; private set; }
    internal double Expansion => amount;

    internal TrackCommentActionRow(FrameworkElement leading, FrameworkElement composer, FrameworkElement trailing)
    {
        this.leading = leading; this.trailing = trailing; this.composer = composer;
        Height = 44; ClipToBounds = true;
        Add(leading); Add(composer); Add(trailing);
        clock.TickCallback = value =>
        {
            amount = from + (target - from) * value;
            ApplyActionState(); InvalidateMeasure();
        };
    }

    internal void SetExpanded(bool expanded, bool animate = true)
    {
        if (Expanded == expanded && animate) return;
        Expanded = expanded; clock.Stop(); from = amount; target = expanded ? 1 : 0;
        ApplyActionState();
        if (animate && Math.Abs(amount - target) > .000001) clock.Start();
        else { amount = target; ApplyActionState(); InvalidateMeasure(); }
    }

    private void ApplyActionState()
    {
        leading.Opacity = trailing.Opacity = 1 - amount;
        leading.IsHitTestVisible = trailing.IsHitTestVisible = !Expanded && amount < .000001;
        // Fully hidden actions must also leave keyboard navigation. Keep their
        // measured widths below so hiding does not change the animation geometry.
        leading.IsVisible = trailing.IsVisible = !Expanded || amount < .999999;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 640;
        if (leading.IsVisible) { leading.Measure(new Size(double.PositiveInfinity, 44)); leadingWidth = leading.DesiredSize.Width; }
        if (trailing.IsVisible) { trailing.Measure(new Size(double.PositiveInfinity, 44)); trailingWidth = trailing.DesiredSize.Width; }
        composer.Measure(new Size(Math.Max(0, width - (leadingWidth + trailingWidth + 2 * TrackButtons.ActionSpacing) * (1 - amount)), 44));
        return new Size(width, 44);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = (leadingWidth + TrackButtons.ActionSpacing) * (1 - amount);
        var right = (trailingWidth + TrackButtons.ActionSpacing) * (1 - amount);
        leading.Arrange(new Rect(Bounds.X - leadingWidth * amount, Bounds.Y, leadingWidth, 44));
        trailing.Arrange(new Rect(Bounds.Right - trailingWidth + trailingWidth * amount, Bounds.Y, trailingWidth, 44));
        composer.Arrange(new Rect(Bounds.X + left, Bounds.Y, Math.Max(0, finalSize.Width - left - right), 44));
        return finalSize;
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (amount <= 0 || amount >= 1) { base.RenderSubtree(context); return; }
        context.Save();
        try
        {
            context.IntersectClip(Bounds);
            // Only the moving actions receive the alpha mask. The editor stays
            // sharp, and the real artwork backdrop shows through the fade.
            var fadeWidth = 32 * Math.Min(1, amount / .2);
            leadingRaster.Draw(context, leading, Bounds, fadeWidth);
            trailingRaster.Draw(context, trailing, Bounds, fadeWidth);
            composer.Render(context);
        }
        finally { context.Restore(); }
    }

    protected override void OnDispose()
    {
        clock.Stop(); leadingRaster.Dispose(); trailingRaster.Dispose(); base.OnDispose();
    }

    // Render each small action group once, then apply a per-pixel alpha ramp.
    // Reuse the surface and image across frames instead of replaying the whole
    // button subtree for every strip of a gradient mask.
    private sealed class ActionRaster : IDisposable
    {
        private IGraphicsFactory? factory;
        private ICpuPixelSurface? surface;
        private IGraphicsContext? renderer;
        private IImage? image;
        private double dpi;

        internal void Draw(IGraphicsContext context, FrameworkElement actions, Rect viewport, double fadeWidth)
        {
            if (!actions.IsVisible || actions.Opacity <= 0) return;
            var scale = context.DpiScale;
            var x = Math.Floor(actions.Bounds.X * scale) / scale;
            var y = Math.Floor(actions.Bounds.Y * scale) / scale;
            var width = Math.Max(1, (int)Math.Ceiling((actions.Bounds.Right - x) * scale));
            var height = Math.Max(1, (int)Math.Ceiling((actions.Bounds.Bottom - y) * scale));
            var graphics = Application.Current.GraphicsFactory;
            if (surface == null || factory != graphics || dpi != scale || surface.PixelWidth != width || surface.PixelHeight != height)
            {
                Dispose(); factory = graphics; dpi = scale;
                surface = (ICpuPixelSurface)graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, scale,
                    premultiplied: true, debugName: "CommentActionFade"));
                renderer = graphics.CreateContext(surface);
                image = graphics.CreateImageView(surface);
            }
            renderer!.BeginFrame(surface);
            renderer.Clear(Color.Transparent);
            renderer.Translate(-x, -y);
            actions.Render(renderer);
            renderer.EndFrame();
            var pixels = surface.GetWritablePixelSpan();
            var premultiplied = surface.Capabilities.HasFlag(SurfaceCapabilities.Premultiplied);
            for (var column = 0; column < width; column++)
            {
                var position = x + (column + .5) / scale;
                var distance = Math.Min(position - viewport.X, viewport.Right - position);
                var fraction = Math.Clamp(distance / fadeWidth, 0, 1);
                var alpha = fraction * fraction * (3 - 2 * fraction);
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
