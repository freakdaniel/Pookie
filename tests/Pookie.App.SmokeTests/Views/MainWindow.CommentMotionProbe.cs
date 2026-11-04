using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task ProbeCommentComposerAsync()
    {
        var original = player;
        var fixture = new BufferFixturePlayer();
        timer.Stop(); player = fixture;
        try
        {
            var track = trackDetailState!.Track;
            trackDetailState = trackDetailState with { Comments = new(Enumerable.Range(0, 500).Select(i => new SoundCloudComment
            {
                Id = 10000 + i, Body = string.Concat(Enumerable.Repeat("Длинный комментарий с переносами строк. ", 1 + i % 8)),
                User = new() { Username = "Слушатель " + i }
            }).ToArray(), null) };
            RenderTrackDetailComments();
            ((CommentFixtureBrowser)api.BrowserTransport!).PlaybackTrack = track;
            SetQueue(track); await PlayAsync(track);
            var watch = Stopwatch.StartNew();
            fixture.State = new(20, track.DurationSeconds, true, false, false) { BufferedEnd = track.DurationSeconds };
            void Advance() => fixture.State = fixture.State with { Position = 20 + watch.Elapsed.TotalSeconds };
            Window.FrameRendered += Advance;
            timer.Start(); Poll();
            try
            {
                await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && !playerBackdrop.Running && !playbackLoading.Value);
                await MeasureCommentMotionAsync("fixture");
                await ProbeWheelReactivationAsync();
            }
            finally { Window.FrameRendered -= Advance; }
        }
        finally { timer.Stop(); player = original; }
    }

    private async Task MeasureCommentMotionAsync(string name)
    {
        foreach (var resumed in new[] { false, true })
        {
            if (resumed) await ReactivateProbeWindowAsync();
            var times = new List<double>();
            var previous = Stopwatch.GetTimestamp();
            void Frame()
            {
                var now = Stopwatch.GetTimestamp();
                times.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now;
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            Window.FrameRendered += Frame;
            try
            {
                for (var cycle = 0; cycle < 6; cycle++)
                {
                    previous = Stopwatch.GetTimestamp();
                    trackCommentInput.Focus();
                    await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion == 1);
                    previous = Stopwatch.GetTimestamp();
                    Window.FocusManager.ClearFocus();
                    await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion == 0);
                }
            }
            finally { Window.FrameRendered -= Frame; }
            times.Sort();
            Console.WriteLine($"COMMENT_MOTION_RESULT {name} resumed={resumed} mean={times.Average():F1}ms p95={times[(int)(times.Count * .95)]:F1}ms max={times[^1]:F1}ms frames={times.Count} allocated={GC.GetAllocatedBytesForCurrentThread() - allocated}");
        }
    }

    private async Task ReactivateProbeWindowAsync()
    {
        using var focus = new Window().Title("Pookie activation probe").Fixed(240, 120).Content(new Border());
        try
        {
            focus.Show(Window); focus.Activate();
            await WaitForTrackPageLiveAsync(() => focus.IsActive && !Window.IsActive);
            var inactive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var inactiveTime = trackPageLiveUi ? TimeSpan.FromSeconds(8) : TimeSpan.FromMilliseconds(750);
            using var interval = new System.Threading.Timer(_ => inactive.TrySetResult(), null, inactiveTime, Timeout.InfiniteTimeSpan);
            await inactive.Task.WaitAsync(inactiveTime + TimeSpan.FromSeconds(5), lifetime.Token);
            var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Frame() => returned.TrySetResult();
            Window.FrameRendered += Frame;
            var activation = Stopwatch.StartNew();
            try
            {
                Window.Activate();
                await WaitForTrackPageLiveAsync(() => Window.IsActive);
                Window.InvalidateVisual();
                await returned.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                Console.WriteLine($"WINDOW_REACTIVATION_RESULT inactive_ms={inactiveTime.TotalMilliseconds:F0} first_frame_ms={activation.Elapsed.TotalMilliseconds:F1} playing={isPlaying.Value}");
            }
            finally { Window.FrameRendered -= Frame; }
        }
        finally { focus.Close(); }
    }

    private async Task ProbeWheelReactivationAsync()
    {
        var motion = smoothScrolls[shellScroll];
        foreach (var resumed in new[] { false, true })
        {
            motion.Stop(); shellScroll.SetScrollOffsets(0, 300);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
            if (resumed)
            {
                await ReactivateProbeWindowAsync();
            }
            var point = new Point(shellScroll.Bounds.X + shellScroll.ActualWidth * .4, Math.Min(500, shellScroll.Bounds.Bottom - 40));
            if (OperatingSystem.IsWindows()) MoveLikedActionCursor(point);
            var screen = Window.ClientToScreen(point);
            var coordinates = (nint)(((int)Math.Round(screen.Y) << 16) | ((int)Math.Round(screen.X) & 0xffff));
            var times = new List<double>();
            var previous = Stopwatch.GetTimestamp();
            void Frame()
            {
                var now = Stopwatch.GetTimestamp();
                times.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now;
            }
            Window.FrameRendered += Frame;
            try
            {
                for (var step = 0; step < 100; step++)
                {
                    var direction = step % 20 < 10 ? -1 : 1;
                    if (OperatingSystem.IsWindows())
                    {
                        if (!PostLikedActionMessage(Window.Handle, 0x20A, unchecked((nuint)(uint)((direction * 120) << 16)), coordinates))
                            throw new InvalidOperationException("Cannot post native wheel input.");
                    }
                    else motion.ScrollWheel(direction);
                    // Two presented frames per notch; exercise retargeting and
                    // direction changes while the previous spring is moving.
                    await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
                }
            }
            finally { Window.FrameRendered -= Frame; motion.Stop(); }
            times.Sort();
            Console.WriteLine($"WHEEL_MOTION_RESULT resumed={resumed} mean={times.Average():F1}ms p95={times[(int)(times.Count * .95)]:F1}ms max={times[^1]:F1}ms frames={times.Count} over43={times.Count(t => t >= 43)}");
        }
    }
}
