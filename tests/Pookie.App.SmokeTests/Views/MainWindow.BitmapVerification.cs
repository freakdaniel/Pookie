using Aprillz.MewUI;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
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
