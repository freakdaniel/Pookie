using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyEmojiSampling()
    {
        if (ChatEmojiArtwork.View("🥀").ImageScaleQuality != ImageScaleQuality.HighQuality)
            throw new InvalidOperationException("Standalone emoji images lost high-quality sampling.");
        var changed = 0;
        foreach (var dpi in new[] { 96d, 110d, 144d, 192d })
        foreach (var emoji in new[] { "🥀", "😆" })
        foreach (var size in new[] { 18.2, 22d, 28d })
        {
            var scale = dpi / 96;
            var side = (int)Math.Ceiling(48 * scale);
            using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var image = (ChatEmojiArtwork.Source(emoji) ?? throw new InvalidOperationException("Emoji artwork missing."))
                .CreateImage(Window.GraphicsFactory, 72, 72);
            byte[] Capture(ImageScaleQuality quality, bool production)
            {
                using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(side, side, scale));
                using var context = Window.GraphicsFactory.CreateContext(surface);
                context.BeginFrame(surface); context.Clear(Color.FromRgb(32, 33, 38));
                context.ImageScaleQuality = quality;
                var bounds = new Rect(3.25, 3.5, size, size);
                if (production) ChatEmojiArtwork.Draw(context, emoji, bounds);
                else context.DrawImage(image, bounds);
                if (context.ImageScaleQuality != quality) throw new InvalidOperationException("Emoji drawing leaked its sampling mode.");
                context.EndFrame();
                var pixels = new byte[side * side * 4];
                if (!Window.GraphicsFactory.TryReadPixels(surface, pixels, side * 4))
                    throw new InvalidOperationException("Could not read the GPU emoji quality fixture.");
                var directory = Environment.GetEnvironmentVariable("POOKIE_UI_PREVIEW");
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    var name = $"emoji-{(emoji == "🥀" ? "rose" : "face")}-{dpi}-{size.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{(production ? "production" : quality)}";
                    using var file = File.Create(Path.Combine(directory, name + ".ppm"));
                    file.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{side} {side}\n255\n"));
                    for (var index = 0; index < pixels.Length; index += 4)
                    { file.WriteByte(pixels[index + 2]); file.WriteByte(pixels[index + 1]); file.WriteByte(pixels[index]); }
                }
                return pixels;
            }
            var normal = Capture(ImageScaleQuality.Default, false);
            var reference = Capture(ImageScaleQuality.HighQuality, false);
            var actual = Capture(ImageScaleQuality.Fast, true);
            var difference = normal.Zip(reference).Count(pair => pair.First != pair.Second);
            changed += difference;
            var error = actual.Zip(reference).Select(pair => Math.Abs(pair.First - pair.Second)).Max();
            Console.WriteLine($"EMOJI_QUALITY_SAMPLE dpi={dpi} emoji={emoji} size={size} changed_channels={difference} production_error={error}");
            if (error > 1) throw new InvalidOperationException($"Emoji pixels differ from high-quality sampling at DPI {dpi}: error={error}.");
        }
        if (changed == 0) throw new InvalidOperationException("Quality fixture did not exercise distinct GPU sampling paths.");
        MeasureEmojiSamplingCost();
        Console.WriteLine("EMOJI_QUALITY_OK: GPU pixels at 96/110/144/192 DPI, fractional bounds, small/picker sizes, shared artwork and restored sampling state");
    }

    private void MeasureEmojiSamplingCost()
    {
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(640, 480, 1));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        using var image = ChatEmojiArtwork.Source("🥀")!.CreateImage(Window.GraphicsFactory, 72, 72);
        var pixels = new byte[640 * 480 * 4];
        foreach (var quality in new[] { ImageScaleQuality.Default, ImageScaleQuality.HighQuality })
        {
            var samples = new List<double>();
            for (var frame = 0; frame < 64; frame++)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                context.BeginFrame(surface); context.Clear(Color.FromRgb(32, 33, 38));
                context.ImageScaleQuality = quality;
                for (var index = 0; index < 160; index++)
                    context.DrawImage(image, new Rect(index % 16 * 38 + frame % 4 * .25, index / 16 * 38, 22, 22));
                context.EndFrame();
                if (!Window.GraphicsFactory.TryReadPixels(surface, pixels, 640 * 4))
                    throw new InvalidOperationException("Could not complete GPU sampling cost measurement.");
                if (frame >= 8) samples.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            samples.Sort();
            Console.WriteLine(FormattableString.Invariant($"EMOJI_SAMPLING_COST mode={quality} count=160 p50_ms={samples[samples.Count / 2]:F3} p95_ms={samples[(int)(samples.Count * .95)]:F3} including_gpu_readback=true"));
        }
    }
}
