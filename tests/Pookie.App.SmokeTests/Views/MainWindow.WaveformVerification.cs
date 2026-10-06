using System.Reflection;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyWaveformMotionAsync(LikedTrackRow row)
    {
        using var fixture = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.MalchikWaveform.json")!;
        using var json = JsonDocument.Parse(fixture);
        var waveform = row.Waveform;
        var savedProgress = waveform.Progress; var savedPlaying = waveform.IsPlaying;
        timer.Stop();
        try
        {
            RouteWaveformPointer(new Point(2, 2));
            waveform.SetSamples(WaveformData.Parse(json.RootElement));
            waveform.Progress = 0; waveform.IsPlaying = false;
            CaptureWaveformBar(waveform, .2);
            async Task Fade(Action change, int from, int to, string label)
            {
                var colors = new List<int>();
                void Sample() => colors.Add(CaptureWaveformBar(waveform, .2));
                Window.FrameRendered += Sample;
                try
                {
                    change();
                    var immediate = CaptureWaveformBar(waveform, .2);
                    if (Math.Abs(immediate - from) > 2)
                        throw new InvalidOperationException($"Waveform {label} jumped immediately: {from} -> {immediate}");
                    await Task.Delay(450, lifetime.Token);
                    var final = CaptureWaveformBar(waveform, .2);
                    if (Math.Abs(final - to) > 2 || !colors.Any(color => color > Math.Min(from, to) + 1 && color < Math.Max(from, to) - 1))
                        throw new InvalidOperationException($"Waveform {label} missed intermediate rendered colors: final={final}, expected={to}, frames={string.Join(',', colors)}");
                }
                finally { Window.FrameRendered -= Sample; }
            }
            Point At(double fraction) => new(waveform.Bounds.X + waveform.Bounds.Width * fraction,
                waveform.Bounds.Y + waveform.Bounds.Height * .45);
            await Fade(() => RouteWaveformPointer(At(.6)), 131, 196, "hover");
            CaptureUiPreview("waveform-hover");
            await Fade(() => RouteWaveformPointer(new Point(2, 2)), 196, 131, "mouse leave");
            await Fade(() => waveform.IsPlaying = true, 131, 146, "play");
            await Fade(() => waveform.Progress = .4, 146, 235, "playback progress");
            CaptureUiPreview("waveform-played");
            await Fade(() => waveform.Progress = .1, 235, 146, "backward seek");
            // Retarget a hover before its first transition completes. It must keep
            // the displayed color instead of flashing to either hover endpoint.
            RouteWaveformPointer(At(.8));
            await Task.Delay(50, lifetime.Token);
            var beforeRetarget = CaptureWaveformBar(waveform, .2);
            RouteWaveformPointer(At(.15));
            if (Math.Abs(CaptureWaveformBar(waveform, .2) - beforeRetarget) > 2)
                throw new InvalidOperationException("Rapid waveform hover restarted from an endpoint");
            await Task.Delay(450, lifetime.Token);
            RouteWaveformPointer(new Point(2, 2));
            await Task.Delay(450, lifetime.Token);
            await Fade(() => RouteWaveformClick(At(.7)), 146, 235, "click seek");
            await WaitForLikedLayoutAsync(() => !seeking);
            if (Math.Abs(waveform.Progress - .7) > .01)
                throw new InvalidOperationException("Waveform click did not update its seek position immediately");
            CaptureUiPreview("waveform-clicked");
            waveform.SetSamples([]);
            if (waveform.HasSamples) throw new InvalidOperationException("Recycled waveform retained the previous track's samples");
            Console.WriteLine("UI_WAVEFORM_OK: real SoundCloud silhouette, intermediate rendered hover/play/progress/seek colors, continuous retargeting and recycled-row reset");
        }
        finally
        {
            RouteWaveformPointer(new Point(2, 2));
            waveform.SetSamples(WaveformData.Parse(json.RootElement));
            waveform.Progress = savedProgress; waveform.IsPlaying = savedPlaying;
            timer.Start();
        }
    }

    private int CaptureWaveformBar(TrackWaveform waveform, double fraction)
    {
        var scale = Window.DpiScale;
        var width = (int)Math.Ceiling(waveform.Bounds.Width * scale);
        var height = (int)Math.Ceiling(waveform.Bounds.Height * scale);
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Translate(-waveform.Bounds.X, -waveform.Bounds.Y);
        waveform.Render(context);
        context.EndFrame();
        var cpu = (ICpuPixelSurface)surface;
        var count = (int)Math.Ceiling(waveform.Bounds.Width / 3);
        var bar = (int)(count * fraction);
        var x = (int)Math.Floor((bar * waveform.Bounds.Width / count + .7) * scale);
        var y = (int)Math.Floor((waveform.Bounds.Height * .7 - 4) * scale);
        var pixels = cpu.GetReadOnlyPixelSpan();
        return pixels[y * cpu.StrideBytes + x * 4 + 2];
    }

    private void RouteWaveformPointer(Point point)
    {
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", throwOnError: true)!;
        router.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(method => method.Name == "MouseMove" && method.GetParameters().Length == 7)
            .Invoke(null, [Window, point, new Point(0, 0), false, false, false, ModifierKeys.None]);
    }

    private void RouteWaveformClick(Point point)
    {
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", throwOnError: true)!;
        var method = router.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "MouseButton" && method.GetParameters().Length == 10);
        method.Invoke(null, [Window, point, new Point(0, 0), MouseButton.Left, true, true, false, false, 1, ModifierKeys.None]);
        method.Invoke(null, [Window, point, new Point(0, 0), MouseButton.Left, false, false, false, false, 1, ModifierKeys.None]);
    }
}
