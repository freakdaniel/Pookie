using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyWaveformRevealAsync(TrackWaveform waveform, string name)
    {
        timer.Stop(); RouteWaveformPointer(new Point(2, 2));
        using var fixture = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.MalchikWaveform.json")!;
        using var json = JsonDocument.Parse(fixture);
        var values = WaveformData.Parse(json.RootElement);
        var claims = new HashSet<string>();
        bool RevealOnce() => claims.Add(name);
        var bounds = waveform.Bounds;
        var early = false; var middle = false; var frames = 0;
        double leftInk = 0, rightInk = 0;
        void Sample()
        {
            frames++;
            if (!early && waveform.RevealProgress is >= .12 and < .27)
            {
                var pixels = CaptureWaveformReveal(waveform);
                leftInk = Ink(pixels, .1); rightInk = Ink(pixels, .9); early = true;
            }
            if (!middle && waveform.RevealProgress is >= .45 and < .8)
            {
                var before = waveform.RevealProgress;
                waveform.SetSamples(values, RevealOnce);
                if (before != waveform.RevealProgress) throw new InvalidOperationException("Repeated waveform data restarted its entrance.");
                middle = true;
            }
        }
        try
        {
            waveform.SetSamples([]); waveform.IsPlaying = false; waveform.Progress = 0;
            waveform.SetSamples(values, RevealOnce);
            Window.FrameRendered += Sample;
            var first = CaptureWaveformReveal(waveform);
            if (Ink(first, .1) != 0 || claims.Count != 1)
                throw new InvalidOperationException($"The waveform appeared immediately or missed its paint: {name}, ink={Ink(first,.1)}, claims={claims.Count}, reveal={waveform.RevealProgress}, bounds={waveform.Bounds}.");
            await WaitForLikedLayoutAsync(() => waveform.RevealProgress == 1);
            var final = CaptureWaveformReveal(waveform);
            if (!early || !middle || frames < 5 || leftInk <= 0 || rightInk > Ink(final, .9) * .05 || Ink(final, .9) <= 0)
                throw new InvalidOperationException($"Waveform did not reveal left to right: {name}, early={early}, middle={middle}, frames={frames}, ink={leftInk}/{rightInk}.");
            if (waveform.Bounds != bounds) throw new InvalidOperationException("Waveform entrance changed its layout rectangle.");
            waveform.SetSamples([]);
            if (waveform.IsAnimating || waveform.HasSamples) throw new InvalidOperationException("A recycled waveform retained its animation or samples.");
            waveform.SetSamples(values, RevealOnce);
            var rebound = CaptureWaveformReveal(waveform);
            if (waveform.RevealProgress != 1 || !final.Pixels.SequenceEqual(rebound.Pixels))
                throw new InvalidOperationException("A seen waveform replayed its entrance or final pixels differ from the static raster.");
            Console.WriteLine($"WAVEFORM_REVEAL_OK {name}: left-to-right pixel reveal, intermediate frames, stable geometry, no restart, identical final pixels and stopped recycled animation");
        }
        finally { Window.FrameRendered -= Sample; }
    }

    private (byte[] Pixels, int Width, int Height, int Stride, int Baseline) CaptureWaveformReveal(TrackWaveform waveform)
    {
        var scale = Window.DpiScale;
        var width = (int)Math.Ceiling(waveform.Bounds.Width * scale);
        var height = (int)Math.Ceiling(waveform.Bounds.Height * scale);
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        try { context.Clear(Color.Transparent); context.Translate(-waveform.Bounds.X, -waveform.Bounds.Y); waveform.Render(context); }
        finally { context.EndFrame(); }
        var cpu = (ICpuPixelSurface)surface;
        return (cpu.CopyPixels(), width, height, cpu.StrideBytes, (int)Math.Floor(waveform.Bounds.Height * .7 * scale));
    }

    private static double Ink((byte[] Pixels, int Width, int Height, int Stride, int Baseline) frame, double at)
    {
        double ink = 0;
        // Exclude the loading baseline, including its fractional-DPI coverage.
        for (var y = 0; y < frame.Baseline; y++)
        for (var x = Math.Max(0, (int)(frame.Width * (at - .025))); x < Math.Min(frame.Width, (int)(frame.Width * (at + .025))); x++)
            ink += frame.Pixels[y * frame.Stride + x * 4 + 3];
        return ink;
    }
}
