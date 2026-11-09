using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyTrackPageLiveUiAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var savedVault = SessionVault.Open(new AppDataPaths());
            var account = savedVault.Load() ?? throw new InvalidOperationException("No saved account for the live track page check.");
            // Use the saved account with a private browser profile so this
            // probe can run while the user's normal Pookie session is open.
            var connected = new NativeBrowserSession(account, profilePath: Path.Combine(dataPaths.Browser, "LiveProbe"));
            AttachBrowser(connected);
            me = await RestoreSoundCloudProfileAsync(connected, timeout.Token);
            api.Session = connected.Account; signedIn.Value = true;
            preferences = preferences with { SendPlaybackHistory = false };
            await ShowWorkspaceAsync();
            volume.Value = 0; // Exercise real audio decoding without sound or an OS media session.
            Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
            if (Environment.GetEnvironmentVariable("POOKIE_TRENDS_PROBE") == "1")
            {
                await VerifyLiveTrendingActionPixelsAsync(timeout.Token);
                return;
            }
            if (Environment.GetEnvironmentVariable("POOKIE_LIVE_SESSION") == "1")
            {
                await ProbeLiveSessionAsync(timeout.Token);
                return;
            }
            var seekProbe = Environment.GetEnvironmentVariable("POOKIE_SEEK_LIVE") == "1";
            var scrollResizeProbe = Environment.GetEnvironmentVariable("POOKIE_SCROLL_RESIZE") == "1";
            var scrollLoadingProbe = scrollResizeProbe && Environment.GetEnvironmentVariable("POOKIE_SCROLL_LOADING") == "1";
            var seekTrack = scrollResizeProbe ? Environment.GetEnvironmentVariable("POOKIE_SCROLL_TRACK") :
                seekProbe ? Environment.GetEnvironmentVariable("POOKIE_SEEK_TRACK") : null;
            var track = long.TryParse(seekTrack, out var trackId)
                ? await api.GetTrackAsync(trackId, timeout.Token)
                : await api.ResolveAsync(seekTrack ?? "https://soundcloud.com/kar-tonw/dipins-etazhi-speed-up", timeout.Token);
            libraryLikes = new([track], null); ShowLibraryTracks(); likesMode.Value = TrackViewMode.Waveform;
            await WaitForTrackPageLiveAsync(() => likedRows.Values.Any(row => row.Track?.Id == track.Id && row.Title.ActualWidth > 100));
            var title = likedRows.Values.First(row => row.Track?.Id == track.Id && row.Title.ActualWidth > 100).Title;
            ClickLiveTrackElement(title);
            await WaitForTrackPageLiveAsync(() => page.Value == Page.Track && trackDetailState is { MetadataLoading: false } &&
                trackDetailPlayIcon.ActualWidth > 0);
            if (seekTrack == null)
                await WaitForTrackPageLiveAsync(() => trackDetailState is { CommentsLoading: false, RelatedLoading: false, SidebarLoading: false } &&
                    !navigationHistory[navigationIndex].Pending && trackDetailWaveform.HasSamples && trackDetailCover.Source is ImageSource);
            if (trackDetailState!.MetadataError.Length > 0 || current != null ||
                seekTrack == null && (trackDetailState.CommentsError.Length > 0 || trackDetailState.RelatedError.Length > 0 ||
                    trackDetailState.Comments.Comments.Length == 0 || trackDetailState.Related.Tracks.Length == 0))
                throw new InvalidOperationException("Live track page did not load all sections, or title click started playback.");
            if (seekTrack == null)
                await WaitForTrackPageLiveAsync(() => trackDetailRelatedRows.Any(row => row.Track != null && row.Root.IsVisible &&
                    row.Root.ActualWidth > 100 && row.Root.ActualHeight > 30));
            await WaitForLoginFrameAsync();
            CaptureUiPreview("track-page-live");
            var firstPageCount = trackDetailState.Comments.Comments.Length;
            if (seekTrack == null)
            {
                await MoreTrackCommentsAsync();
                if (trackDetailState.Comments.Comments.Length <= firstPageCount)
                    throw new InvalidOperationException("Live comments pagination did not add rows.");
            }
            Button? playButton = null;
            VisualTree.Visit(trackDetailScroll, element => { if (element is Button button && button.Content == trackDetailPlayIcon) playButton = button; });
            if (playButton == null) throw new InvalidOperationException("Track page play button was not found.");
            if (scrollLoadingProbe)
            {
                await ProbeTrackHeroScrollingAsync(timeout.Token, () => ClickLiveTrackElement(playButton));
                await WaitForTrackPageLiveAsync(() => current?.Id == track.Id && audioReady && progress.Value > .1);
                Console.WriteLine("TRACK_WHEEL_LOADING_OK: playback started after the resize and wheel input");
                return;
            }
            ClickLiveTrackElement(playButton);
            await WaitForTrackPageLiveAsync(() => current?.Id == track.Id && audioReady && progress.Value > .1);
            Console.WriteLine("TRACK_PAGE_LIVE_PLAY_OK: native play click, real audio decoding and advancing position");
            if (scrollResizeProbe)
            {
                await ProbeTrackHeroScrollingAsync(timeout.Token);
                return;
            }
            if (seekProbe)
            {
                await ProbeTrackPageSeekingAsync(timeout.Token);
                return;
            }
            if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_LIVE") == "1")
            {
                for (var batch = 0; batch < 20 && trackDetailState.Comments.Comments.Length < 500 && trackDetailState.Comments.NextHref != null; batch++)
                {
                    var count = trackDetailState.Comments.Comments.Length;
                    await MoreTrackCommentsAsync();
                    if (trackDetailState.Comments.Comments.Length <= count) break;
                }
                await WaitForTrackPageLiveAsync(() => shellPageScroll.IsReady && audioReady);
                Console.WriteLine($"LIVE_SCROLL_SETUP comments={trackDetailState.Comments.Comments.Length}; real audio, comments and avatars; muted");
                void Avatars(string phase)
                {
                    var sources = new HashSet<ImageSource>();
                    LogicalTree.Visit(trackDetailComments, element =>
                    { if (element is Image { Source: ImageSource image } && image.PixelWidth >= 48) sources.Add(image); });
                    Console.WriteLine($"LIVE_AVATAR_PIXELS {phase} unique={sources.Count}; rgba_bytes={sources.Sum(ArtworkBytes)}; max_side={sources.Select(image=>Math.Max(image.PixelWidth,image.PixelHeight)).DefaultIfEmpty().Max()}");
                }
                Avatars("before");
                await MeasureCommentMotionAsync("live");
                await ProbeWheelReactivationAsync();
                if (Environment.GetEnvironmentVariable("POOKIE_SCROLL_PRESENTATION") == "1")
                    await ProbeNativeTouchpadScrollingAsync("live-wheel-" + trackDetailState.Comments.Comments.Length, wheelNotches: true);
                await ProbeContinuousScrollAsync("live-track-" + trackDetailState.Comments.Comments.Length);
                Avatars("after");
                Console.WriteLine("LIVE_SCROLL_OK");
                return;
            }
            var waveformPoint = new Point(trackDetailWaveform.Bounds.X + trackDetailWaveform.Bounds.Width * .25,
                trackDetailWaveform.Bounds.Y + trackDetailWaveform.Bounds.Height / 2);
            SendLikedActionMouse(0x201, 1, waveformPoint); SendLikedActionMouse(0x202, 0, waveformPoint);
            await WaitForTrackPageLiveAsync(() => progress.Value >= track.DurationSeconds * .25 - .5 && progress.Value < track.DurationSeconds * .25 + 3);
            ClickLiveTrackElement(playButton);
            await WaitForTrackPageLiveAsync(() => !isPlaying.Value);
            var timed = trackDetailState.Comments.Comments.First(comment => comment.Timestamp > 1000 && comment.Timestamp < track.Duration);
            var index = Array.FindIndex(trackDetailState.Comments.Comments, comment => comment.Id == timed.Id);
            Button? timeButton = null;
            VisualTree.Visit(trackDetailComments.Children[index], element =>
            {
                if (element is Button { Content: TextBlock label } button && label.Text == FormatTime(timed.Timestamp!.Value / 1000))
                    timeButton ??= button;
            });
            if (timeButton == null) throw new InvalidOperationException("Live timed comment button was not rendered.");
            trackDetailScroll.SetScrollOffsets(0, Math.Max(0, timeButton.Bounds.Y - Window.ClientSize.Height * .6));
            await WaitForLoginFrameAsync();
            ClickLiveTrackElement(timeButton);
            await WaitForTrackPageLiveAsync(() => Math.Abs(progress.Value - timed.Timestamp!.Value / 1000) < 1);
            Console.WriteLine("TRACK_PAGE_LIVE_SEEK_OK: native waveform and comment timestamp clicks, pause button");
            await MoveNavigationAsync(-1);
            if (page.Value != Page.LibraryTracks || likesMode.Value != TrackViewMode.Waveform || current?.Id != track.Id)
                throw new InvalidOperationException("Live back navigation lost the list or playback.");
            await MoveNavigationAsync(1);
            if (page.Value != Page.Track || trackDetailState?.Track.Id != track.Id || trackDetailState.Comments.Comments.Length <= firstPageCount)
                throw new InvalidOperationException("Live forward navigation lost the page or comments.");
            Console.WriteLine("TRACK_PAGE_LIVE_UI_OK: preserved account, real track data/artwork/waveform/comments/related, native title/play/pause/seek and back/forward; no likes or clipboard writes");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("TRACK_PAGE_LIVE_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private void ClickLiveTrackElement(FrameworkElement element)
    {
        var point = new Point(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        SendLikedActionMouse(0x200, 0, point); SendLikedActionMouse(0x201, 1, point); SendLikedActionMouse(0x202, 0, point);
    }

    private async Task ProbeTrackPageSeekingAsync(CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("POOKIE_SEEK_IMMEDIATE") == "1")
        {
            var first = trackDetailWaveform.Bounds;
            var clickPoint = new Point(first.X + first.Width * .75, first.Y + first.Height * .4);
            var at = Stopwatch.GetTimestamp();
            SendLikedActionMouse(0x201, 1, clickPoint); SendLikedActionMouse(0x202, 0, clickPoint);
            Console.WriteLine($"TRACK_SEEK_FIRST_WAVE: audio_resume={await WaitForSeekAsync(progress.Maximum * .75, at):F0}ms");
            return;
        }
        await ResizeTrackProbeWindowAsync(token);
        await WaitForTrackPageLiveAsync(() => shellPageScroll.IsReady && progress.Bounds.Width > 50);
        var rail = progress.Bounds;
        var wave = trackDetailWaveform.Bounds;
        long lastFrame = 0;
        var frames = new List<double>();
        var handling = new List<double>();
        void Frame()
        {
            var now = Stopwatch.GetTimestamp();
            if (lastFrame != 0) frames.Add(Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds);
            lastFrame = now;
        }
        var start = Stopwatch.GetTimestamp();
        var dispatcher = Application.Current.Dispatcher!;
        Point RailPoint(double fraction) => new(rail.X + rail.Width * fraction, rail.Center.Y);
        void Drag(Point point)
        {
            var began = Stopwatch.GetTimestamp();
            RouteActionHeldPointer(point);
            handling.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
        }
        double requested;
        Window.FrameRendered += Frame;
        try
        {
            SendLikedActionMouse(0x200, 0, RailPoint(.15));
            SendLikedActionMouse(0x201, 1, RailPoint(.15));
            if (!seekDragging || !progress.IsMouseCaptured)
                throw new InvalidOperationException("The native slider did not capture the drag.");
            // Schedule on wall time, so stalled frames cannot slow down the input source.
            await Task.Run(async () =>
            {
                using var moves = new PeriodicTimer(TimeSpan.FromMilliseconds(12));
                for (var step = 1; step <= 80; step++)
                {
                    await moves.WaitForNextTickAsync(token).ConfigureAwait(false);
                    var point = RailPoint(.15 + step * .6 / 80);
                    dispatcher.BeginInvoke(() => Drag(point));
                }
            }, token);
            Drag(RailPoint(.75));
            requested = progress.Value;
            if (Math.Abs(requested / progress.Maximum - .75) > .025 || !seekDragging)
                throw new InvalidOperationException($"The slider did not reach its preview: {requested:F1}/{progress.Maximum:F1}; dragging={seekDragging}.");
        }
        finally { Window.FrameRendered -= Frame; }
        var released = Stopwatch.GetTimestamp();
        SendLikedActionMouse(0x202, 0, RailPoint(.75));
        var dragResume = await WaitForSeekAsync(requested, released);
        var sorted = frames.Order().ToArray();
        if (sorted.Length < 10) throw new InvalidOperationException("Too few rendered frames during seek.");
        var input = handling.Order().ToArray();
        Console.WriteLine($"TRACK_SEEK_DRAG: frames={sorted.Length} p95={sorted[(int)(sorted.Length * .95)]:F1}ms max={sorted[^1]:F1}ms input_count={input.Length} input_p95={input[(int)(input.Length * .95)]:F2}ms input_max={input[^1]:F2}ms drag_ms={Stopwatch.GetElapsedTime(start, released).TotalMilliseconds:F0} audio_resume={dragResume:F0}ms");
        var click = new Point(wave.X + wave.Width * .25, wave.Y + wave.Height * .4);
        var clicked = Stopwatch.GetTimestamp();
        SendLikedActionMouse(0x201, 1, click); SendLikedActionMouse(0x202, 0, click);
        Console.WriteLine($"TRACK_SEEK_WAVE: audio_resume={await WaitForSeekAsync(progress.Maximum * .25, clicked):F0}ms");
        Console.WriteLine($"TRACK_SEEK_RESIZED_OK: track={current?.Id}; viewport={Window.ClientSize}; PlayReady={player is Playback.WindowsPlayReadyAudioPlayer { HasProtectedSession: true }}");

        async Task<double> WaitForSeekAsync(double position, long inputTime)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var poll = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
            double? firstPlaying = null;
            do
            {
                var state = player!.Poll();
                if (seekDragging || seeking || pendingSeek != null || Math.Abs(state.Position - position) >= 2 ||
                    !state.Playing || state.Buffering) continue;
                firstPlaying ??= state.Position;
                if (state.Position > firstPlaying) return Stopwatch.GetElapsedTime(inputTime).TotalMilliseconds;
            } while (await poll.WaitForNextTickAsync(deadline.Token));
            throw new InvalidOperationException("Playback did not advance after seeking.");
        }
    }

    private async Task ResizeTrackProbeWindowAsync(CancellationToken token)
    {
        var scale = Window.DpiScale;
        Window.Activate();
        foreach (var width in new[] { 1450, 1050, 1330, 1140 })
        {
            if (!ProbeSetWindowPos(Window.Handle, 0, 0, 0, (int)(width * scale), (int)(820 * scale), 0x16))
                throw new InvalidOperationException("Could not resize the track window.");
            await WaitForLoginFrameAsync().WaitAsync(token);
        }
    }

    private async Task ProbeTrackHeroScrollingAsync(CancellationToken token, Action? startLoading = null)
    {
        await ResizeTrackProbeWindowAsync(token);
        await WaitForTrackPageLiveAsync(() => shellPageScroll.IsReady && SmoothScroll.Maximum(shellScroll) > 100);
        var max = SmoothScroll.Maximum(shellScroll);
        var motion = smoothScrolls[shellScroll];
        var handle = Window.Handle;
        var local = new Point(shellScroll.Bounds.Center.X, 300);
        var screen = Window.ClientToScreen(local);
        var coordinates = (nint)(((int)Math.Round(screen.Y) << 16) | ((int)Math.Round(screen.X) & 0xffff));
        SendLikedActionMouse(0x200, 0, local);
        Console.WriteLine($"TRACK_WHEEL_SETUP track={current?.Id} comments={trackDetailState!.Comments.Comments.Length} max={max:F0} viewport={Window.ClientSize}");

        var notch = Environment.GetEnvironmentVariable("POOKIE_SCROLL_NOTCH") == "1" ? 120 : 60;
        await Sweep(startLoading == null ? "hero-after-resize" : "hero-loading-after-resize", 0, notch, resize: true);
        if (startLoading == null && max >= 1400) await Sweep("below-hero", 700, 30);

        async Task Sweep(string name, double origin, int delta, bool resize = false)
        {
            motion.Stop(); shellScroll.SetScrollOffsets(0, origin);
            await WaitForLoginFrameAsync().WaitAsync(token);
            var intervals = new List<double>();
            var movement = new List<double>();
            var slowFrames = new List<(long Frame, double Gap, DateTime At, bool Preparing, bool LoadingComments)>();
            var last = 0L;
            var firstInput = 0L;
            var firstHandled = 0L;
            var lastReversal = 0L;
            var inputDirection = 0;
            var actualDirection = 0;
            var reversals = 0;
            var resizeStarted = 0L;
            var resizeWall = default(DateTime);
            var resizeReturned = 0L;
            var resizePresented = 0L;
            var resizeCoalescing = false;
            var loadingAtInput = false;
            var resizeFrameMs = 0d;
            var directResizeFrame = false;
            var resizeBlurReady = false;
            var oldSize = Window.ClientSize;
            var firstMovedAt = 0L;
            var firstMovedFrame = 0L;
            var firstMovedDrawnAt = 0L;
            double firstMove = 0, reversed = 0, furthest = origin, previous = origin;
            void Drawn()
            {
                if (firstMovedDrawnAt == 0 && resizeStarted != 0 && Window.ClientSize != oldSize &&
                    Volatile.Read(ref firstInput) != 0 && Math.Abs(shellScroll.VerticalOffset - origin) > .01)
                    firstMovedDrawnAt = Stopwatch.GetTimestamp();
            }
            void HandleWheel()
            {
                if (firstHandled != 0 || Volatile.Read(ref firstInput) == 0) return;
                firstHandled = Stopwatch.GetTimestamp();
                loadingAtInput = audioPreparing;
            }
            void Frame()
            {
                var now = Stopwatch.GetTimestamp();
                var offset = shellScroll.VerticalOffset;
                if (resizeStarted != 0 && resizePresented == 0 && Window.ClientSize != oldSize)
                {
                    resizePresented = now;
                    resizeFrameMs = Window.LastFramePerformanceStats.FrameMs;
                    directResizeFrame = WindowPresentation.DrawDirectly(Window);
                    resizeBlurReady = startupHeader is ShellHeader header && HeaderBackdrop(header).Ready;
                }
                if (last != 0)
                {
                    var gap = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
                    intervals.Add(gap);
                    if (Math.Abs(previous - offset) > .01) movement.Add(gap);
                    if (gap >= 35)
                        slowFrames.Add((Window.LastFramePerformanceStats.FrameIndex, gap, DateTime.UtcNow,
                            audioPreparing, trackDetailState?.CommentsLoading == true));
                }
                if (Math.Abs(offset - previous) > .01 && Volatile.Read(ref firstInput) is > 0 and var sent)
                {
                    if (firstMovedAt == 0 && (!resize || Window.ClientSize != oldSize))
                    {
                        firstMovedAt = now;
                        firstMovedFrame = Window.LastFramePerformanceStats.FrameIndex;
                        firstMove = Stopwatch.GetElapsedTime(sent, now).TotalMilliseconds;
                    }
                    var direction = Math.Sign(offset - previous);
                    if (direction != actualDirection && direction == Volatile.Read(ref inputDirection))
                    {
                        if (actualDirection != 0) reversals++;
                        actualDirection = direction;
                    }
                    if (reversed == 0 && reversals > 0 && Volatile.Read(ref lastReversal) is > 0 and var turn)
                        reversed = Stopwatch.GetElapsedTime(turn, now).TotalMilliseconds;
                }
                furthest = Math.Max(furthest, offset); last = now; previous = offset;
            }
            var firstFrame = Window.LastFramePerformanceStats.FrameIndex;
            motion.UserScrolling += HandleWheel;
            Window.FrameRendered += Drawn;
            WindowPresentation.FramePresented(Window) += Frame;
            try
            {
                if (resize)
                {
                    startLoading?.Invoke();
                    var dpi = Window.DpiScale;
                    resizeWall = DateTime.UtcNow;
                    resizeStarted = Stopwatch.GetTimestamp();
                    if (!ProbeSetWindowPos(handle, 0, 0, 0, (int)(1275 * dpi), (int)(850 * dpi), 0x16))
                        throw new InvalidOperationException("Could not resize before posting wheel input.");
                    resizeReturned = Stopwatch.GetTimestamp();
                    resizeCoalescing = trackResizeCoalescer.IsEnabled;
                }
                await Task.Run(async () =>
                {
                    const int packetsPerDirection = 12;
                    using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
                    for (var i = 0; i < 8 * packetsPerDirection; i++)
                    {
                        if (i != 0) await ticks.WaitForNextTickAsync(token).ConfigureAwait(false);
                        else Volatile.Write(ref firstInput, Stopwatch.GetTimestamp());
                        var direction = i / packetsPerDirection % 2 == 0 ? 1 : -1;
                        if (i != 0 && i % packetsPerDirection == 0) Volatile.Write(ref lastReversal, Stopwatch.GetTimestamp());
                        Volatile.Write(ref inputDirection, direction);
                        if (!PostLikedActionMessage(handle, 0x20a, unchecked((nuint)(uint)(-direction * delta << 16)), coordinates))
                            throw new InvalidOperationException("Could not post wheel input.");
                    }
                }, token);
                using var settle = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
                while (motion.IsRunning) await settle.WaitForNextTickAsync(token);
            }
            finally
            {
                motion.UserScrolling -= HandleWheel;
                Window.FrameRendered -= Drawn;
                WindowPresentation.FramePresented(Window) -= Frame;
                motion.Stop();
            }
            if (movement.Count < 10 || firstHandled == 0 || firstMove <= 0 || reversals < 4 || furthest - origin < 20 || furthest - shellScroll.VerticalOffset < 20)
                throw new InvalidOperationException($"The native wheel did not move in both directions ({name}, offset={shellScroll.VerticalOffset:F1}, maximum={furthest:F1}).");
            if (startLoading != null && !loadingAtInput)
                throw new InvalidOperationException("Playback was no longer preparing when the first wheel packet was handled.");
            if (resize && resizePresented == 0) throw new InvalidOperationException("The resized viewport was not rendered.");
            if (resize && !resizeBlurReady) throw new InvalidOperationException("The pinned header lost its blur on the first resized frame.");
            intervals.Sort(); movement.Sort();
            var afterResize = resizeReturned == 0 ? 0 : Stopwatch.GetElapsedTime(resizeReturned, firstInput).TotalMilliseconds;
            if (resize)
            {
                Console.WriteLine($"TRACK_RESIZE_MARK {resizeWall:O}");
                Console.WriteLine($"TRACK_RESIZE_FIRST {name}: first_frame={resizeFrameMs:F1}ms resize_to_frame={Stopwatch.GetElapsedTime(resizeStarted, resizePresented).TotalMilliseconds:F1}ms resize_to_movement={Stopwatch.GetElapsedTime(resizeStarted, firstMovedAt).TotalMilliseconds:F1}ms movement_frame={firstMovedFrame} set_window_pos={Stopwatch.GetElapsedTime(resizeStarted, resizeReturned).TotalMilliseconds:F1}ms coalescing={resizeCoalescing} direct={directResizeFrame} loading_at_input={loadingAtInput}");
                if (firstMovedDrawnAt != 0)
                    Console.WriteLine($"TRACK_RESIZE_PHASES {name}: wheel_to_draw={Stopwatch.GetElapsedTime(firstHandled, firstMovedDrawnAt).TotalMilliseconds:F1}ms draw_to_present={Stopwatch.GetElapsedTime(firstMovedDrawnAt, firstMovedAt).TotalMilliseconds:F1}ms");
            }
            foreach (var slow in slowFrames.OrderByDescending(frame => frame.Gap).Take(6))
                Console.WriteLine($"TRACK_WHEEL_SLOW {name}: frame={slow.Frame} gap={slow.Gap:F1}ms at={slow.At:O} audio_preparing={slow.Preparing} comments_loading={slow.LoadingComments}");
            Console.WriteLine($"TRACK_WHEEL_RESIZE {name}: movement_frames={movement.Count} p95={movement[(int)(movement.Count * .95)]:F1}ms max={intervals[^1]:F1}ms over33={intervals.Count(gap => gap >= 33)} first={firstMove:F1}ms input_queue={Stopwatch.GetElapsedTime(firstInput, firstHandled).TotalMilliseconds:F1}ms reverse={reversed:F1}ms reversals={reversals} resize_to_input={afterResize:F1}ms furthest={furthest:F0} final={shellScroll.VerticalOffset:F0} frames={firstFrame}-{Window.LastFramePerformanceStats.FrameIndex}");
        }
    }

    private async Task WaitForTrackPageLiveAsync(Func<bool> ready)
    {
        var started = Stopwatch.GetTimestamp();
        while (!ready())
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(30))
                throw new InvalidOperationException($"Live track UI did not settle: page={page.Value}; audio_ready={audioReady}; preparing={audioPreparing}; status={status.Value}");
            await WaitForLoginFrameAsync();
        }
    }
}
