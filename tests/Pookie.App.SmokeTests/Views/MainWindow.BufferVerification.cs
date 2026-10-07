using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Pookie.Audio;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyBufferUiAsync()
    {
        var original = player;
        var fixture = new BufferFixturePlayer();
        timer.Stop();
        player = fixture;
        try
        {
            await PlayAsync(tracks[0]);
            fixture.State = new(30, 120, true, false, false) { BufferedEnd = 90 };
            Poll();
            await WaitForLikedLayoutAsync(() => progress.ActualWidth > 100 && playerChrome.Opacity == 1 && playerChrome.Height == PlayerBarHeight);
            if (bufferedTrack.IsHitTestVisible || bufferedTrack.Bounds != progress.Bounds || bufferedTrack.EndFraction != .75 || progress.Value != 30)
                throw new InvalidOperationException("The buffer did not align behind the interactive progress slider.");
            var colors = SampleTimelineColors();
            if (!(colors[0] > colors[1] + 25 && colors[1] > colors[2] + 25))
                throw new InvalidOperationException($"Played, buffered and empty rail are not distinct: {string.Join(',', colors)}");
            CaptureUiPreview("player-buffered");
            await VerifyTimelineAnimationAsync(fixture);
            BeginSeekDrag();
            progress.Value = 80;
            Poll();
            if (progress.Value != 80) throw new InvalidOperationException("Buffer polling overwrote a seek preview.");
            EndSeekDrag();
            if (bufferedTrack.EndFraction != .5 || !bufferedTrack.IsVisible)
                throw new InvalidOperationException("Seek cleared or hid the already drawn buffer.");
            await WaitForLikedLayoutAsync(() => !seeking);
            Poll();
            await WaitForLikedLayoutAsync(() => !bufferedTrack.Animating);
            if (bufferedTrack.EndFraction != 100d / 120)
                throw new InvalidOperationException("Seek retained the old buffer endpoint.");
            updatingProgress = true;
            try
            {
                progress.Value = 0;
                var afterSeek = SampleTimelineColors();
                if (Math.Abs(afterSeek[0] - afterSeek[1]) > 1 || afterSeek[1] < afterSeek[2] + 25)
                    throw new InvalidOperationException("Buffered rail did not extend continuously from the beginning after seeking.");
            }
            finally { progress.Value = fixture.State.Position; updatingProgress = false; }
            fixture.State = fixture.State with { Buffering = true, BufferedStart = 0, BufferedEnd = 0 };
            Poll();
            if (!bufferedTrack.IsVisible || !progress.IsVisible || loadingTrack.IsVisible || bufferedTrack.EndFraction != 100d / 120)
                throw new InvalidOperationException("A seek/underrun replaced or cleared the progress layers.");
            BeginSeekDrag();
            progress.Value = 40;
            EndSeekDrag();
            await WaitForLikedLayoutAsync(() => !seeking);
            if (fixture.State.Position != 40) throw new InvalidOperationException("The visible slider could not seek while waiting for audio.");
            fixture.State = fixture.State with { Buffering = false, BufferedStart = 0, BufferedEnd = 0 };
            Poll();
            await WaitForLikedLayoutAsync(() => !bufferedTrack.Animating);
            if (!bufferedTrack.IsVisible || !progress.IsVisible || bufferedTrack.EndFraction != 0)
                throw new InvalidOperationException("Empty buffer did not restore the normal progress rail.");
            fixture.State = fixture.State with { BufferedEnd = 100 };
            Poll();
            await PlayAsync(tracks[1]);
            if (bufferedTrack.EndFraction != 0) throw new InvalidOperationException("Track switch retained the previous buffer.");
            Console.WriteLine("BUFFER_UI_OK: distinct rail layers; smooth progress, buffer growth/shrink, no seek feedback, preserved buffer during seek/underrun, empty buffer and track reset");
        }
        catch (Exception error) { Environment.ExitCode = 1; Console.Error.WriteLine("BUFFER_UI_FAILED: " + error); }
        finally { player = original; Window.Close(); }
    }

    private async Task VerifyTimelineAnimationAsync(BufferFixturePlayer fixture)
    {
        var frames = new List<(double Progress, double Buffer)>();
        void RecordFrame() => frames.Add((progress.Value, bufferedTrack.EndFraction));
        Window.FrameRendered += RecordFrame;
        try
        {
            fixture.State = fixture.State with { Position = 36, BufferedEnd = 110 };
            Poll();
            if (progress.Value != 30 || bufferedTrack.EndFraction != .75)
                throw new InvalidOperationException("A new playback sample jumped directly to its target.");
            await WaitForLikedLayoutAsync(() => !progressAnimation.IsRunning && !bufferedTrack.Animating);
            if (frames.Count(frame => frame.Progress is > 30 and < 36) < 3 ||
                frames.Count(frame => frame.Buffer is > .75 and < (110d / 120)) < 3 ||
                Math.Abs(progress.Value - 36) > .000001 || Math.Abs(bufferedTrack.EndFraction - 110d / 120) > .000001 ||
                pendingSeek != null || seeking || fixture.SeekCount != 0)
                throw new InvalidOperationException("Timeline smoothing missed intermediate rendered frames or generated a seek.");
            frames.Clear();
            fixture.State = fixture.State with { BufferedEnd = 60 };
            Poll();
            await WaitForLikedLayoutAsync(() => !bufferedTrack.Animating);
            if (frames.Count(frame => frame.Buffer > .5 && frame.Buffer < 110d / 120) < 3)
                throw new InvalidOperationException("Buffer contraction jumped instead of animating.");
            // Pointer input interrupts an in-flight playback animation immediately.
            fixture.State = fixture.State with { Position = 42 };
            Poll();
            BeginSeekDrag();
            progress.Value = 50;
            await WaitForLoginFrameAsync();
            await WaitForLoginFrameAsync();
            if (progressAnimation.IsRunning || progress.Value != 50)
                throw new InvalidOperationException("Playback animation moved the pointer's seek preview.");
            EndSeekDrag();
            await WaitForLikedLayoutAsync(() => !seeking);
        }
        finally { Window.FrameRendered -= RecordFrame; }
    }

    private byte[] SampleTimelineColors()
    {
        var scale = Window.DpiScale;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(Window.ClientSize.Width * scale), (int)Math.Ceiling(Window.ClientSize.Height * scale), scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        Window.Content!.Render(context);
        context.EndFrame();
        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var colors = new byte[3];
        var fractions = new[] { .125, .5, .875 };
        for (var i = 0; i < colors.Length; i++)
        {
            var x = (int)((progress.Bounds.X + 7 + (progress.Bounds.Width - 14) * fractions[i]) * scale);
            var y = (int)((progress.Bounds.Y + progress.Bounds.Height / 2) * scale);
            colors[i] = pixels[y * cpu.StrideBytes + x * 4 + 2];
        }
        return colors;
    }

    private sealed class BufferFixturePlayer : IAudioPlayer
    {
        public AudioState State = new(0, 0, false, false, false);
        public int SeekCount;
        public AudioState Poll() => State;
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default)
        { State = new(0, source.Duration, true, false, false); return Task.CompletedTask; }
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default)
        { SeekCount++; State = State with { Position = seconds, BufferedStart = seconds, BufferedEnd = Math.Min(State.Duration, seconds + 20) }; return Task.CompletedTask; }
        public void Pause(bool paused) => State = State with { Playing = !paused };
        public void Stop() => State = new(0, 0, false, false, false);
        public void Volume(double percent) { }
        public void Dispose() => Stop();
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
