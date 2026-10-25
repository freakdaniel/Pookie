using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using System.Numerics;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifySmoothBackgroundGradients()
    {
        using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var sheen = new GlassSheenRaster();
        foreach (var scale in new[] { 1d, 1.5, 2 })
        {
            var width = (int)(1600 * scale); var height = (int)(160 * scale);
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, scale));
            using var renderer = Window.GraphicsFactory.CreateContext(surface);
            var cpu = (ICpuPixelSurface)surface;
            byte[] Draw()
            {
                renderer.BeginFrame(surface);
                renderer.Clear(Color.FromRgb(24, 24, 24));
                sheen.Draw(renderer, new Rect(0, 0, 1600, 160), new CornerRadius(20));
                renderer.EndFrame();
                return cpu.GetReadOnlyPixelSpan().ToArray();
            }
            var pixels = Draw();
            if (!pixels.SequenceEqual(Draw())) throw new InvalidOperationException("Glass gradient noise moved between identical frames.");
            var mixed = 0; var count = (int)(1000 * scale);
            for (var x = (int)(200 * scale); x < (int)(1200 * scale); x++)
            {
                var minimum = 255; var maximum = 0;
                for (var y = (int)(48 * scale); y < (int)(112 * scale); y++)
                {
                    var i = y * cpu.StrideBytes + x * 4;
                    var level = pixels[i]; minimum = Math.Min(minimum, level); maximum = Math.Max(maximum, level);
                    if (pixels[i] != pixels[i + 1] || pixels[i] != pixels[i + 2] || pixels[i + 3] != 255)
                        throw new InvalidOperationException("Glass dither changed the neutral tint or surface opacity.");
                }
                if (maximum - minimum > 2) throw new InvalidOperationException("Glass gradient introduced visible grain.");
                if (maximum > minimum) mixed++;
            }
            if (mixed < count * .75) throw new InvalidOperationException($"Glass still contains broad flat bands at {scale}x DPI: {mixed}/{count} columns blend shades; renderer={renderer.GetType().Name}, samples={pixels[80 * cpu.StrideBytes + 200 * 4]}/{pixels[80 * cpu.StrideBytes + 800 * 4]}/{pixels[80 * cpu.StrideBytes + 1200 * 4]}.");
        }
        var samples = Enumerable.Range(0, 160 * 64).Select(i => new Vector3(20 + i % 160 / 159f * 20)).ToArray();
        var wave = new byte[1600 * 96 * 4];
        WaveBackdrop.FillPixels(wave, 1600, 96, samples);
        var mixedWave = 0;
        for (var x = 20; x < 1580; x++)
        {
            var levels = Enumerable.Range(0, 96).Select(y => wave[(y * 1600 + x) * 4]).ToArray();
            if (levels.Max() - levels.Min() > 1) throw new InvalidOperationException("Wave gradient introduced visible noise.");
            if (levels.Max() != levels.Min()) mixedWave++;
        }
        if (mixedWave < 1400) throw new InvalidOperationException("Upscaling the wave gradient lost its fractional colours.");
        Console.WriteLine("SMOOTH_GRADIENTS_OK: native glass pixels at 1x/1.5x/2x DPI, stable neutral one-level dithering and fractional wave upscaling");
    }
}
