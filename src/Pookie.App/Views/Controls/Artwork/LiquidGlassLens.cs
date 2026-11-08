using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

// Edge refraction and RGB dispersion adapted from Liquid Glass Studio's
// fragment-main shader (MIT, Copyright (c) 2024 Charles Yin).
// https://github.com/iyinchao/liquid-glass-studio/blob/f7b28c36305a862f5cffed3ddd51511cf1204f56/src/shaders/fragment-main.glsl
internal static class LiquidGlassOptics
{
    internal const float Thickness = 9;
    private const int Steps = 128;
    private static readonly EdgeSample[] Profile = Enumerable.Range(0, Steps + 1).Select(index =>
    {
        var depth = (float)index / Steps;
        var rim = 1 - depth;
        var incidence = MathF.Asin(rim * rim);
        var transmission = MathF.Asin(MathF.Sin(incidence) / 1.48f);
        var bend = 8 * MathF.Tan(incidence - transmission);
        var fresnel = .16f * MathF.Pow(1 - MathF.Cos(incidence), 5);
        return new EdgeSample(bend, .48f * rim * rim, fresnel, rim * rim * rim);
    }).ToArray();

    internal static EdgeSample At(float depth) =>
        Profile[(int)MathF.Round(Math.Clamp(depth, 0, Thickness) * Steps / Thickness)];

    internal readonly record struct EdgeSample(float Bend, float Clarity, float Fresnel, float Glare)
    {
        internal float Reflection(Vector2 normal)
        {
            // Light from above and left, with a weaker reflection opposite it.
            var light = Vector2.Dot(normal, new(-.55f, -.835f));
            var directional = light * light;
            return Fresnel + Glare * directional * directional * (light >= 0 ? .28f : .11f);
        }
    }
}

// Each control caches its own lens. A scroll that moves the control together
// with its backdrop keeps the same sampling coordinates and reuses the image.
internal sealed class LiquidGlassLens : IDisposable
{
    private readonly GlassGeometry geometry = new();
    private WriteableBitmap? bitmap;
    private IImage? image;
    private ImageBrush? brush;
    private IGraphicsFactory? factory;
    private bool hasAlpha;
    private BackdropBlur? paintedSource;
    private PlayerGradient? paintedGradient;
    private int paintedVersion;
    private Rect paintedRegion;
    private Size paintedSourceSize;
    private CornerRadius paintedCorners;
    private LiquidGlassLens? outgoing;
    internal LiquidGlassLens Outgoing => outgoing ??= new();

    internal void ClearOutgoing()
    {
        outgoing?.Dispose(); outgoing = null;
    }

    internal void Draw(IGraphicsContext context, IGraphicsFactory graphics, BackdropBlur? source, Rect sourceBounds,
        Rect bounds, CornerRadius corners, PathGeometry? shape = null, double minimumScale = 1)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || sourceBounds.Width <= 0 || sourceBounds.Height <= 0) return;
        var (width, height) = PixelSize(bounds.Size, Math.Max(context.DpiScale, minimumScale));
        EnsureBitmap(graphics, width, height, needsAlpha: source == null);
        var region = new Rect(bounds.X - sourceBounds.X, bounds.Y - sourceBounds.Y, bounds.Width, bounds.Height);
        var version = source?.Version ?? -1;
        if (paintedGradient != null || paintedSource != source || paintedVersion != version || paintedRegion != region ||
            paintedSourceSize != sourceBounds.Size || paintedCorners != corners)
        {
            using var write = bitmap!.LockForWrite();
            if (source == null)
                Render(write.PixelsBgra32, width, height, [], [], 0, 0, region, sourceBounds.Size, corners);
            else
            {
                using var pixels = source.LockPublishedPixels();
                Render(write.PixelsBgra32, width, height, source.SharpPixels, pixels.Buffer, pixels.PixelWidth, pixels.PixelHeight,
                    region, sourceBounds.Size, corners);
                version = pixels.Version;
            }
            paintedSource = source;
            paintedGradient = null;
            paintedVersion = version;
            paintedRegion = region;
            paintedSourceSize = sourceBounds.Size;
            paintedCorners = corners;
        }
        DrawPrepared(context, bounds, corners, shape);
    }

    internal void DrawPlayerGradient(IGraphicsContext context, PlayerBackdrop backdrop, Rect bounds,
        CornerRadius corners, PathGeometry shape, double minimumScale)
    {
        var surface = backdrop.Bounds;
        if (surface.Width <= 0 || bounds.IsEmpty) return;
        var scale = Math.Max(context.DpiScale, minimumScale);
        var (width, height) = PixelSize(bounds.Size, scale);
        EnsureBitmap(Application.Current.GraphicsFactory, width, height);
        var region = new Rect(bounds.X - surface.X, 0, bounds.Width, bounds.Height);
        var gradient = backdrop.CurrentGradient;
        if (paintedGradient != gradient || paintedRegion != region || paintedSourceSize.Width != surface.Width || paintedCorners != corners)
        {
            // The player gradient is constant along Y and already soft on this
            // scale. Sample one row with a halo for the lens's displaced rays.
            const double halo = 16;
            var sampleSize = new Size(bounds.Width + 2 * halo, bounds.Height);
            var (sampleWidth, _) = PixelSize(sampleSize, scale);
            Span<byte> pixels = stackalloc byte[sampleWidth * 4];
            for (var x = 0; x < sampleWidth; x++)
            {
                var sample = gradient.Sample((region.X - halo + (x + .5) / sampleWidth * sampleSize.Width) / surface.Width);
                var i = x * 4;
                pixels[i] = (byte)Math.Round(sample.Z);
                pixels[i + 1] = (byte)Math.Round(sample.Y);
                pixels[i + 2] = (byte)Math.Round(sample.X);
                pixels[i + 3] = 255;
            }
            using var write = bitmap!.LockForWrite();
            Render(write.PixelsBgra32, width, height, pixels, pixels, sampleWidth, 1,
                new Rect(halo, 0, bounds.Width, bounds.Height), sampleSize, corners);
            paintedGradient = gradient;
            paintedRegion = region;
            paintedSourceSize = new Size(surface.Width, 1);
            paintedCorners = corners;
        }
        DrawPrepared(context, bounds, corners, shape);
    }

    internal bool DrawPrepared(IGraphicsContext context, Rect bounds, CornerRadius corners, PathGeometry? shape = null)
    {
        if (image == null) return false;
        // Fill geometry uses the backend's edge coverage. The stencil used by
        // SetClipPath cuts the texture at whole pixels on MewVG.
        if (brush == null || brush.DestinationRect != bounds)
            brush = new(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight), bounds, TileMode.None);
        context.FillPath(shape ?? geometry.Get(bounds, corners), brush);
        return true;
    }

    internal void Upload(IGraphicsFactory graphics, Bgra32PixelBuffer pixels)
    {
        EnsureBitmap(graphics, pixels.WidthPx, pixels.HeightPx);
        using var write = bitmap!.LockForWrite();
        pixels.Data.CopyTo(write.PixelsBgra32);
    }

    private void EnsureBitmap(IGraphicsFactory graphics, int width, int height, bool needsAlpha = false)
    {
        if (bitmap != null && factory == graphics && bitmap.PixelWidth == width && bitmap.PixelHeight == height &&
            (!needsAlpha || hasAlpha)) return;
        ClearBitmap();
        hasAlpha = needsAlpha;
        bitmap = new WriteableBitmap(width, height, clear: false, hasAlpha: hasAlpha);
        image = hasAlpha ? BitmapImage.CreatePremultipliedView(graphics, bitmap) : ((IImageSource)bitmap).CreateImage(graphics);
        factory = graphics;
    }

    internal static (int Width, int Height) PixelSize(Size size, double scale)
    {
        scale = Math.Min(Math.Min(2, scale), 640 / Math.Max(size.Width, size.Height));
        return (Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
    }

    internal static void Render(Span<byte> output, int width, int height, ReadOnlySpan<byte> sharp, ReadOnlySpan<byte> blurred,
        int sourceWidth, int sourceHeight, Rect region, Size sourceSize, CornerRadius corners)
    {
        var halfWidth = (float)region.Width / 2;
        var halfHeight = (float)region.Height / 2;
        var pixelWidth = (float)region.Width / width;
        var pixelHeight = (float)region.Height / height;
        var scaleX = sourceWidth / (float)sourceSize.Width;
        var scaleY = sourceHeight / (float)sourceSize.Height;
        var topLeft = (float)corners.TopLeft;
        var topRight = (float)corners.TopRight;
        var bottomLeft = (float)corners.BottomLeft;
        var bottomRight = (float)corners.BottomRight;
        var maxRadius = MathF.Min(halfWidth, halfHeight);
        var source = new BackdropSampler(sharp, blurred, sourceWidth, sourceHeight);
        var hasBackdrop = !blurred.IsEmpty;
        for (var y = 0; y < height; y++)
        {
            var localY = (y + .5f) * pixelHeight;
            var dy = localY - halfHeight;
            var sy = ((float)region.Y + localY) * scaleY - .5f;
            for (var x = 0; x < width; x++)
            {
                var localX = (x + .5f) * pixelWidth;
                var dx = localX - halfWidth;
                var sx = ((float)region.X + localX) * scaleX - .5f;
                var radius = Math.Clamp(dy < 0 ? dx < 0 ? topLeft : topRight : dx < 0 ? bottomLeft : bottomRight, 0, maxRadius);
                var edgeX = MathF.Abs(dx) - halfWidth + radius;
                var edgeY = MathF.Abs(dy) - halfHeight + radius;
                var corner = edgeX > 0 && edgeY > 0;
                var distance = corner ? MathF.Sqrt(edgeX * edgeX + edgeY * edgeY) : MathF.Max(edgeX, edgeY);
                var index = (y * width + x) * 4;
                var depth = radius - distance;
                if (depth >= LiquidGlassOptics.Thickness)
                {
                    var pixel = output.Slice(index, 4);
                    if (hasBackdrop) source.CopyBlurred(pixel, sx, sy);
                    else pixel.Clear();
                    continue;
                }
                // Analytic normal of the rounded rectangle's distance field.
                var normal = corner ? new Vector2(MathF.CopySign(edgeX / distance, dx), MathF.CopySign(edgeY / distance, dy)) :
                    edgeX > edgeY ? new(MathF.CopySign(1, dx), 0) : new(0, MathF.CopySign(1, dy));
                var optics = LiquidGlassOptics.At(depth);
                var reflection = optics.Reflection(normal);
                if (!hasBackdrop)
                {
                    // The card already paints the scene beneath this lens. Keep
                    // its specular rim visible while the first sample is pending.
                    output.Slice(index, 4).Fill((byte)(255 * reflection + .5f));
                    continue;
                }
                var bendX = -normal.X * optics.Bend * scaleX;
                var bendY = -normal.Y * optics.Bend * scaleY;
                for (var channel = 0; channel < 3; channel++)
                {
                    // BGRA order: blue, green and red sample different rays.
                    var dispersion = .86f + channel * .14f;
                    var value = source.Sample(sx + bendX * dispersion, sy + bendY * dispersion, channel, optics.Clarity);
                    output[index + channel] = (byte)(value + (255 - value) * reflection + .5f);
                }
                output[index + 3] = 255;
            }
        }
    }

    private readonly ref struct BackdropSampler(ReadOnlySpan<byte> sharp, ReadOnlySpan<byte> blurred, int width, int height)
    {
        private readonly ReadOnlySpan<byte> sharpPixels = sharp, blurredPixels = blurred;

        internal void CopyBlurred(Span<byte> output, float x, float y)
        {
            var (index, right, bottom, fx, fy) = Footprint(x, y);
            if (fx == 0 && fy == 0)
            {
                blurredPixels.Slice(index, 4).CopyTo(output);
                return;
            }
            for (var channel = 0; channel < 3; channel++)
                output[channel] = (byte)(Interpolate(blurredPixels, index + channel, right, bottom, fx, fy) + .5f);
            output[3] = 255;
        }

        internal float Sample(float x, float y, int channel, float clarity)
        {
            var (index, right, bottom, fx, fy) = Footprint(x, y);
            index += channel;
            var soft = Interpolate(blurredPixels, index, right, bottom, fx, fy);
            var clear = Interpolate(sharpPixels, index, right, bottom, fx, fy);
            return soft + (clear - soft) * clarity;
        }

        private (int Index, int Right, int Bottom, float X, float Y) Footprint(float x, float y)
        {
            x = Math.Clamp(x, 0, width - 1);
            y = Math.Clamp(y, 0, height - 1);
            var left = (int)x;
            var top = (int)y;
            return ((top * width + left) * 4, left < width - 1 ? 4 : 0, top < height - 1 ? width * 4 : 0, x - left, y - top);
        }

        private static float Interpolate(ReadOnlySpan<byte> pixels, int index, int right, int bottom, float x, float y)
        {
            var top = pixels[index] + (pixels[index + right] - pixels[index]) * x;
            var low = pixels[index + bottom] + (pixels[index + bottom + right] - pixels[index + bottom]) * x;
            return top + (low - top) * y;
        }
    }

    public void Dispose()
    {
        ClearOutgoing();
        ClearBitmap();
    }

    private void ClearBitmap()
    {
        brush = null;
        image?.Dispose(); image = null;
        bitmap?.Dispose(); bitmap = null;
        factory = null; paintedSource = null;
        paintedGradient = null;
        paintedVersion = int.MinValue;
    }
}
