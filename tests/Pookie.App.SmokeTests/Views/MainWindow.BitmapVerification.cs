using Aprillz.MewUI;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyBackdropBlurPixels()
    {
        foreach (var (width, height) in new[] { (1, 1), (7, 9), (2, 17), (19, 11) })
        foreach (var sigma in new[] { 0, .5, 2, 12, 80 })
        {
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height));
            var source = (ICpuPixelSurface)surface;
            var input = source.GetWritablePixelSpan();
            for (var i = 0; i < input.Length; i++) input[i] = (byte)((i * 73 + i / 7 * 19) % 256);
            var expected = input.ToArray();
            source.CommitCpuWrite();
            var scratch = new byte[expected.Length];
            var lower = Math.Max(1, (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1)) | 1);
            if (lower > Math.Sqrt(4 * sigma * sigma + 1)) lower = Math.Max(1, lower - 2);
            var count = (int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4d * lower - 4));
            // Independent direct box sums also cover kernels larger than the
            // strip, where every out-of-bounds sample must clamp to an edge.
            void Reference(byte[] from, byte[] to, int radius, bool horizontal)
            {
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0;
                    for (var offset = -radius; offset <= radius; offset++)
                    {
                        var sx = horizontal ? Math.Clamp(x + offset, 0, width - 1) : x;
                        var sy = horizontal ? y : Math.Clamp(y + offset, 0, height - 1);
                        sum += from[(sy * width + sx) * 4 + channel];
                    }
                    to[(y * width + x) * 4 + channel] = (byte)((sum + radius) / (radius * 2 + 1));
                }
            }
            for (var pass = 0; pass < 3; pass++)
            {
                var radius = ((pass < count ? lower : lower + 2) - 1) / 2;
                Reference(expected, scratch, radius, true); Reference(scratch, expected, radius, false);
            }
            using var blur = new BackdropBlur(Window.GraphicsFactory, width, height);
            blur.Capture(source); blur.Process(sigma);
            if (!blur.Pixels.SequenceEqual(expected))
                throw new InvalidOperationException($"SIMD blur changed integer-rounded BGRA pixels: {width}x{height}, sigma={sigma}.");
        }
        Console.WriteLine("BACKDROP_BLUR_PIXELS_OK: SIMD output exactly matches direct clamped BGRA box sums");
    }

    private static void VerifyLiquidGlassPixels()
    {
        const int size = 48;
        var backdrop = new byte[size * size * 4];
        var lens = new byte[backdrop.Length];
        var bounds = new Rect(0, 0, size, size);
        var corners = new CornerRadius(12, 5, 12, 5);
        int Pixel(int y, int channel) => (y * size + size / 2) * 4 + channel;
        void Paint() => LiquidGlassLens.Render(lens, size, size, backdrop, backdrop, size, size, bounds, bounds.Size, corners);

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var index = (y * size + x) * 4;
            backdrop[index] = backdrop[index + 1] = backdrop[index + 2] = (byte)(4 * y);
            backdrop[index + 3] = 255;
        }
        Paint();
        if (lens[Pixel(0, 2)] <= lens[Pixel(0, 0)] + 3 ||
            lens[Pixel(size - 1, 0)] <= lens[Pixel(size - 1, 2)] + 3 ||
            lens[Pixel(size / 2, 1)] != backdrop[Pixel(size / 2, 1)])
            throw new InvalidOperationException("The lens did not displace RGB channels around its edges while preserving its center.");

        for (var i = 0; i < backdrop.Length; i += 4)
            backdrop[i] = backdrop[i + 1] = backdrop[i + 2] = 80;
        Paint();
        if (lens[Pixel(0, 1)] != 80 || lens[Pixel(size - 1, 1)] != 80 ||
            lens[Pixel(size / 2, 1)] != 80 || lens[Pixel(0, 3)] != 255)
            throw new InvalidOperationException("The backdrop lens baked in a second reflection beneath the material tint.");
        Console.WriteLine("LIQUID_GLASS_PIXELS_OK: RGB refraction, undistorted center, reflection kept out of backdrop");

        // The shared contour must have opposing light lobes and a narrow falloff,
        // including at the small radii used by connected buttons. This distinguishes
        // it from both a uniform outline and the previous broad double bevel.
        foreach (var scale in new[] { 1d, 110 / 96d, 1.5, 2 })
        foreach (var shape in new[] { new CornerRadius(22), new CornerRadius(22, 5, 5, 22) })
        {
            const double w = 116, h = 44;
            var width = (int)Math.Ceiling(w * scale); var height = (int)Math.Ceiling(h * scale);
            var ink = new byte[width * height * 4];
            GlassSheenRaster.FillPixels(ink, width, height, new Size(w, h), shape, .35, premultiplied: true);
            int Alpha(double x, double y) => ink[((int)(y / h * height) * width + (int)(x / w * width)) * 4 + 3];
            var upper = Alpha(w / 2, .5); var lower = Alpha(w / 2, h - .6);
            if (upper < lower || lower < 6 || upper < Alpha(.5, h / 2) + 4 ||
                Alpha(w / 2, 4) > 9 || Alpha(w / 2, h - 4) > 9 || Alpha(w / 2, h / 2) > 6)
                throw new InvalidOperationException($"Glass contour became a uniform stroke or wide bevel at scale {scale}: upper={upper}, lower={lower}, side={Alpha(.5, h / 2)}, interior={Alpha(w / 2, 4)}.");
            for (var index = 0; index < ink.Length; index += 4)
            {
                if (ink[index + 3] > 68)
                    throw new InvalidOperationException("Neutral dark glass has an overbright reflection contour.");
                if (ink[index] != ink[index + 3] || ink[index + 1] != ink[index + 3] || ink[index + 2] != ink[index + 3])
                    throw new InvalidOperationException("Glass highlight pixels are not neutral premultiplied light.");
            }
        }
        Console.WriteLine("GLASS_RIM_OK: narrow opposing highlights, quiet interior, connected corners and 96/110/144/192 DPI");
    }

    private void VerifyLiquidGlassEdgeCoverage()
    {
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var lens = new LiquidGlassLens();
        var pixels = Enumerable.Repeat((byte)255, 40 * 40 * 4).ToArray();
        lens.Upload(Window.GraphicsFactory, new(40, 40, pixels, HasAlpha: false));
        var bounds = new Rect(5.35, 6.25, 40, 40);
        foreach (var scale in new[] { 1d, 110 / 96d, 1.5, 2 })
        {
            var size = (int)Math.Ceiling(55 * scale);
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(size, size, scale));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface);
            context.Clear(Color.Black);
            lens.DrawPrepared(context, bounds, new CornerRadius(18, 7, 15, 4));
            context.EndFrame();
            var raster = (ICpuPixelSurface)surface;
            var data = raster.GetReadOnlyPixelSpan();
            var partial = 0;
            for (var y = (int)(7 * scale); y < (int)(20 * scale); y++)
            for (var x = (int)(6 * scale); x < (int)(19 * scale); x++)
                if (data[y * raster.StrideBytes + x * 4] is > 4 and < 251) partial++;
            if (partial < 5 || data[0] != 0 || data[size / 2 * raster.StrideBytes + size / 2 * 4] < 251)
                throw new InvalidOperationException($"The curved glass outline did not receive subpixel coverage at {scale:P0} DPI: {partial} pixels.");
        }
        Console.WriteLine("LIQUID_GLASS_EDGE_OK: antialiased curved corners at 96/110/144/192 DPI and fractional positions");
    }

    private void VerifyGlassHighlightSampling()
    {
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        var worstMean = 0d; var worstPixel = 0;
        foreach (var scale in new[] { 1d, 110 / 96d, 1.25, 1.5, 2 })
        foreach (var (phase, translated) in new[] { (0d, false), (.4, false), (.4, true) })
        foreach (var size in new[] { new Size(44, 44), new Size(116, 36) })
        foreach (var filled in new[] { false, true })
        {
            var offset = translated ? 0 : phase / scale;
            var bounds = new Rect(4 + offset, 4 + offset, size.Width, size.Height);
            var corners = new CornerRadius(size.Height / 2, 5, 5, size.Height / 2);
            var width = (int)Math.Ceiling((size.Width + 10) * scale);
            var height = (int)Math.Ceiling((size.Height + 10) * scale);
            byte[] Render(int detail)
            {
                using var sheen = new GlassSheenRaster();
                using var pigment = new TintedGlassRaster();
                using var target = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width * detail, height * detail, scale * detail));
                using var context = Window.GraphicsFactory.CreateContext(target);
                context.BeginFrame(target); context.Clear(Color.Black);
                if (translated) context.Translate(phase / scale, phase / scale);
                if (filled) pigment.Draw(context, bounds, size.Height / 2, Color.FromRgb(255, 101, 0));
                else sheen.Draw(context, bounds, corners, .35);
                context.EndFrame();
                return ((ICpuPixelSurface)target).GetReadOnlyPixelSpan().ToArray();
            }
            var actual = Render(1); var reference = Render(8);
            var count = 0; var sumError = 0d; var maximum = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                for (var sy = 0; sy < 8; sy++)
                for (var sx = 0; sx < 8; sx++)
                    sum += reference[((y * 8 + sy) * width * 8 + x * 8 + sx) * 4 + (filled ? 2 : 0)];
                var expected = (sum + 32) / 64;
                var painted = actual[(y * width + x) * 4 + (filled ? 2 : 0)];
                if (expected < 8 && painted < 8 || filled && expected > 247 && painted > 247) continue;
                var error = Math.Abs(painted - expected);
                maximum = Math.Max(maximum, error); sumError += error; count++;
            }
            var mean = sumError / Math.Max(1, count);
            worstMean = Math.Max(worstMean, mean); worstPixel = Math.Max(worstPixel, maximum);
            if (mean > 2 || maximum > 12)
                throw new InvalidOperationException($"Glass highlight sampling failed: dpi={scale}, phase={phase}, translated={translated}, size={size}, filled={filled}, mean={mean:F2}, max={maximum}.");
        }
        Console.WriteLine($"GLASS_HIGHLIGHT_AA_OK: neutral/filled rims match an 8x reference at 96/110/120/144/192 DPI and fractional positions; mean={worstMean:F2}, max={worstPixel}");
    }

    private void VerifyGlassMotionCache()
    {
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var sheen = new GlassSheenRaster();
        using var filled = new TintedGlassRaster();
        using var target = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(380, 180, 1.25));
        using var context = Window.GraphicsFactory.CreateContext(target);
        for (var frame = 0; frame < 30; frame++)
        {
            context.BeginFrame(target); context.Clear(Color.Black);
            context.Save(); context.Translate(frame * .17, frame * .23);
            var bounds = new Rect(10 + frame * .19, 10 + frame * .13, 240, 64);
            sheen.Draw(context, bounds, new CornerRadius(20), frame / 29d, minimumScale: 2);
            filled.Draw(context, bounds, 20, FixtureMixBadgeColor(1), minimumScale: 2);
            context.Restore(); context.EndFrame();
        }
        using var capture = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(300, 100, 1));
        using var captureContext = Window.GraphicsFactory.CreateContext(capture);
        captureContext.BeginFrame(capture); captureContext.Clear(Color.Black);
        sheen.Draw(captureContext, new Rect(10, 10, 240, 64), new CornerRadius(20), minimumScale: 2);
        filled.Draw(captureContext, new Rect(10, 10, 240, 64), 20, FixtureMixBadgeColor(1), minimumScale: 2);
        captureContext.EndFrame();
        if (sheen.Rasterizations != 1 || filled.Rasterizations != 1)
            throw new InvalidOperationException($"Moving/hovering glass or a smaller backdrop capture rebuilt optical rasters: neutral={sheen.Rasterizations}, filled={filled.Rasterizations}.");
        Console.WriteLine($"GLASS_MOTION_CACHE_OK: 30 fractional scroll/translation/hover frames and a smaller backdrop capture reuse neutral/filled optical images ({sheen.PixelBytes} bytes)");
    }

    private void VerifyShadowRasterPixels()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var shadow = new BoxShadowRaster(6, 2, Color.FromArgb(90, 0, 0, 0));
        foreach (var size in new[] { new Size(280, 44), new Size(320, 180), new Size(900, 620) })
        {
            var bounds = new Rect(16, 16, size.Width, size.Height);
            var width = (int)Math.Ceiling((size.Width + 32) * Window.DpiScale);
            var height = (int)Math.Ceiling((size.Height + 32) * Window.DpiScale);
            byte[] Draw(bool cached)
            {
                using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, Window.DpiScale));
                using var context = Window.GraphicsFactory.CreateContext(surface);
                context.BeginFrame(surface); context.Clear(Color.White);
                if (cached) shadow.Draw(context, bounds, 20);
                else context.DrawBoxShadow(bounds, 20, 6, Color.FromArgb(90, 0, 0, 0), offsetY: 2);
                context.EndFrame();
                return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
            }
            var expected = Draw(false); var actual = Draw(true);
            double error = 0; var maximum = 0; var samplingError = 0;
            for (var i = 0; i < actual.Length; i += 4)
            {
                var delta = Math.Abs(actual[i] - expected[i]); error += delta; maximum = Math.Max(maximum, delta);
                // Native gradients and bitmap edges round at different stages.
                // Accept one device pixel of edge sampling, but reject seams
                // in flat regions or a shadow displaced beyond that pixel.
                var x = i / 4 % width; var y = i / 4 / width;
                var minimum = 255; var upper = 0;
                for (var sy = Math.Max(0, y - 1); sy <= Math.Min(height - 1, y + 1); sy++)
                for (var sx = Math.Max(0, x - 1); sx <= Math.Min(width - 1, x + 1); sx++)
                { var value = expected[(sy * width + sx) * 4]; minimum = Math.Min(minimum, value); upper = Math.Max(upper, value); }
                samplingError = Math.Max(samplingError, Math.Max(minimum - actual[i], actual[i] - upper));
            }
            var mean = error / (width * height);
            Console.WriteLine($"SHADOW_RASTER_PIXELS size={size} mean={mean:F3} max={maximum} samplingError={samplingError} bytes={shadow.PixelBytes}");
            // DC coverage, eight-bit alpha and bitmap interpolation each round.
            if (mean > 1.5 || samplingError > 8 || shadow.PixelBytes > 256 * 1024)
            {
                var path = Path.Combine(Path.GetTempPath(), "pookie-shadow-" + (int)size.Height);
                File.WriteAllBytes(path + "-expected.bgra", expected); File.WriteAllBytes(path + "-actual.bgra", actual);
                Console.WriteLine($"SHADOW_DIAGNOSTIC {path} {width}x{height}");
                throw new InvalidOperationException("Bounded shadow raster changed the shadow shape or grew with the card.");
            }
        }
        var windowRasterBytes = shadow.PixelBytes;
        using var capture = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(64, 64, Window.DpiScale / 2));
        using var captureContext = Window.GraphicsFactory.CreateContext(capture);
        captureContext.BeginFrame(capture);
        try { shadow.Draw(captureContext, new Rect(16, 16, 900, 620), 20, rasterScale: Window.DpiScale); }
        finally { captureContext.EndFrame(); }
        if (shadow.PixelBytes != windowRasterBytes)
            throw new InvalidOperationException("The half-resolution header capture replaced the window's shadow raster.");
        Console.WriteLine("SHADOW_MIXED_DPI_OK: window and header capture share one raster");
    }

    private static void VerifyBackdropPixelBuffers()
    {
        var resize = new BackdropDownsample();
        foreach (var (width, height, targetWidth, targetHeight) in new[] { (7, 9, 3, 2), (33, 17, 8, 4), (1, 1, 1, 1), (16, 16, 4, 4) })
        {
            var stride = width * 4 + 12;
            var source = new byte[stride * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            for (var c = 0; c < 4; c++) source[y * stride + x * 4 + c] = (byte)((x * 23 + y * 7 + c * 31) % 256);
            var actual = new byte[targetWidth * targetHeight * 4];
            resize.Resize(source, width, height, stride, actual, targetWidth, targetHeight);
            for (var y = 0; y < targetHeight; y++)
            for (var x = 0; x < targetWidth; x++)
            for (var c = 0; c < 4; c++)
            {
                double sum = 0;
                var left = x * width / (double)targetWidth; var right = (x + 1) * width / (double)targetWidth;
                var top = y * height / (double)targetHeight; var bottom = (y + 1) * height / (double)targetHeight;
                for (var sy = 0; sy < height; sy++)
                for (var sx = 0; sx < width; sx++)
                    sum += source[sy * stride + sx * 4 + c] * Math.Max(0, Math.Min(sx + 1, right) - Math.Max(sx, left)) * Math.Max(0, Math.Min(sy + 1, bottom) - Math.Max(sy, top));
                var expected = (int)Math.Round(sum / ((right - left) * (bottom - top)));
                if (Math.Abs(actual[(y * targetWidth + x) * 4 + c] - expected) > 1)
                    throw new InvalidOperationException("Header area sampling lost pixel coverage or included row padding.");
            }
            source.AsSpan().Fill(73);
            resize.Resize(source, width, height, stride, actual, targetWidth, targetHeight);
            if (actual.Any(value => value != 73)) throw new InvalidOperationException("Header reduction changed a constant color.");
        }
        using var bitmap = new WriteableBitmap(2, 1);
        bitmap.WritePixels(0, 0, 2, 1, new byte[] { 180, 120, 60, 128, 255, 100, 20, 0 }, 8);
        IPixelBufferSource pixels = new BitmapImage.PremultipliedPixels(bitmap);
        byte[] first;
        using (var read = pixels.Lock())
        {
            first = read.Buffer;
            if (!first.SequenceEqual(new byte[] { 90, 60, 30, 128, 0, 0, 0, 0 }))
                throw new InvalidOperationException("Cached alpha conversion produced incorrect translucent pixels.");
        }
        for (var i = 0; i < 100; i++)
        {
            using var read = pixels.Lock();
            if (!ReferenceEquals(first, read.Buffer)) throw new InvalidOperationException("An unchanged raster allocated another pixel array.");
        }
        bitmap.WritePixels(0, 0, 1, 1, new byte[] { 30, 90, 120, 255 }, 4);
        using (var read = pixels.Lock())
        {
            if (!ReferenceEquals(first, read.Buffer) || !read.Buffer.AsSpan(0, 4).SequenceEqual(new byte[] { 30, 90, 120, 255 }))
                throw new InvalidOperationException("The retained conversion ignored a bitmap version change.");
        }
        using (var read = ((IPixelBufferSource)bitmap).Lock())
            if (!read.Buffer.AsSpan(4, 4).SequenceEqual(new byte[] { 255, 100, 20, 0 }))
                throw new InvalidOperationException("The rendering cache modified the writable source pixels.");
        Console.WriteLine("BACKDROP_BUFFERS_OK: exact area coverage, fractional reductions, padded stride, resize reuse, retained alpha conversion and bitmap version updates");
    }
}
