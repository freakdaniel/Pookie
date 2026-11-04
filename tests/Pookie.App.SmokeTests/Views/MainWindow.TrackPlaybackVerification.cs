using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyTrackPlaybackVisibilityAsync()
    {
        var original = player;
        var fixture = new BufferFixturePlayer();
        timer.Stop(); player = fixture;
        try
        {
            await VerifyTrackAvatarBudgetAsync();
            var track = trackDetailState!.Track;
            trackDetailState = trackDetailState with { Comments = new(Enumerable.Range(0, 500).Select(i => new SoundCloudComment
            {
                Id = 10000 + i, User = new() { Username = "Слушатель " + i },
                Body = string.Concat(Enumerable.Repeat("Длинный комментарий с переносами строк. ", 1 + i % 8))
            }).ToArray(), null), CommentsLoading = false };
            RenderTrackDetailComments();
            SetQueue(track); await PlayAsync(track);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && TrackTimelineInViewport());
            fixture.State = new(15, track.DurationSeconds, true, false, false) { BufferedEnd = track.DurationSeconds };
            Poll();
            try { await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && TrackTimelineInViewport() && trackDetailPosition.Text == "0:15"); }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException($"Playback hero did not settle: ready={shellPageScroll.IsReady}, visible={TrackTimelineInViewport()}, text={trackDetailPosition.Text}, model={likedPlaybackPosition}, footer={currentTime.Value}, current={current?.Id}, track={track.Id}, audio={audioReady}, seek={seeking}/{seekDragging}/{pendingSeek}, wave={trackDetailWaveform.Bounds}, scroll={trackDetailScroll.Bounds}, shell={shellScroll.Bounds}, offset={shellScroll.VerticalOffset}.", error);
            }
            var text = trackDetailPosition.Text;
            var waveform = trackDetailWaveform.Progress;
            shellScroll.SetScrollOffsets(0, SmoothScroll.Maximum(shellScroll) / 2);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && !TrackTimelineInViewport() && !trackTimelineVisible);
            for (var seconds = 30; seconds <= 75; seconds += 15)
            {
                fixture.State = fixture.State with { Position = seconds };
                Poll(); await WaitForLoginFrameAsync();
                if (likedPlaybackPosition != seconds || currentTime.Value != FormatTime(seconds) ||
                    trackDetailPosition.Text != text || trackDetailWaveform.Progress != waveform)
                    throw new InvalidOperationException("Hidden hero changed its UI or stopped updating the playback model/footer.");
            }
            shellScroll.SetScrollOffsets(0, 0);
            await WaitForLikedLayoutAsync(() => TrackTimelineInViewport() && trackDetailPosition.Text == "1:15" &&
                Math.Abs(trackDetailWaveform.Progress - 75 / track.DurationSeconds) < .0001);

            SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning && !workspace.IsVisible && !trackTimelineVisible);
            fixture.State = fixture.State with { Position = 90, Playing = false };
            Poll(); await WaitForLoginFrameAsync();
            if (trackDetailPosition.Text != "1:15") throw new InvalidOperationException("The hidden page updated behind the expanded player.");
            SetExpandedPlayer(false);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning && workspace.IsVisible && trackDetailPosition.Text == "1:30");

            shellScroll.SetScrollOffsets(0, SmoothScroll.Maximum(shellScroll) / 2);
            await WaitForLikedLayoutAsync(() => !TrackTimelineInViewport());
            var other = track with { Id = 990001, Title = "Другой играющий трек" };
            SetQueue(other); await PlayAsync(other);
            fixture.State = fixture.State with { Position = 30, Playing = true };
            Poll(); shellScroll.SetScrollOffsets(0, 0);
            await WaitForLikedLayoutAsync(() => TrackTimelineInViewport() && trackDetailPosition.Text == "0:00" && trackDetailWaveform.Progress == 0);
            if (renderedComments.Length != 500) throw new InvalidOperationException("Playback visibility discarded comments.");
            Window.FocusManager.ClearFocus();
            var readyArtwork = await GetLibraryArtworkAsync(other);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && !progressAnimation.IsRunning && !playerBackdrop.Running &&
                !trackDetailWaveform.IsAnimating && ReferenceEquals(artwork.Source, readyArtwork));
            var idleFrames = 0;
            void IdleFrame() => idleFrames++;
            Window.FrameRendered += IdleFrame;
            try
            {
                // A timer bounds the measurement window after readiness was observed.
                var interval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var sampleTimer = new System.Threading.Timer(_ => interval.TrySetResult(), null,
                    TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
                await interval.Task.WaitAsync(lifetime.Token);
                if (idleFrames > 3) throw new InvalidOperationException($"Settled page kept requesting frames while idle: {idleFrames} in 400ms.");
            }
            finally { Window.FrameRendered -= IdleFrame; }
            Console.WriteLine("TRACK_PLAYBACK_VISIBILITY_OK: 500 comments, hidden UI stays stable while model/footer advance; scroll/expanded-player reentry, pause and track-switch catchup");
            Console.WriteLine($"TRACK_LAYOUT_IDLE_OK: settled page rendered {idleFrames} frames in 400ms with the poll timer stopped");
        }
        catch (Exception error)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var extent = typeof(ShellPageScroll).GetField("extent", flags)!.GetValue(shellPageScroll);
            var calculated = typeof(ShellPageScroll).GetMethod("ContentExtent", flags)!.Invoke(shellPageScroll, []);
            throw new InvalidOperationException($"Playback visibility snapshot: ready={shellPageScroll.IsReady}, dirty={shellPageScroll.IsMeasureDirty}/{shellPageScroll.IsArrangeDirty}, extent={extent}/{calculated}, visible={TrackTimelineInViewport()}, cached={trackTimelineVisible}, workspace={workspace.IsVisible}, text={trackDetailPosition.Text}, model={likedPlaybackPosition}, audio={audioReady}, wave={trackDetailWaveform.Bounds}, scroll={trackDetailScroll.Bounds}, shell={shellScroll.Bounds}, offset={shellScroll.VerticalOffset}.", error);
        }
        finally { timer.Stop(); player = original; }
    }

    private async Task VerifyTrackAvatarBudgetAsync()
    {
        const string url = "https://i1.sndcdn.com/track-avatar-budget-large.jpg";
        await imageDiskCache.WriteAsync(url, ArtworkFixtureBitmap(100), lifetime.Token);
        await imageDiskCache.WriteAsync(ToLargeArtworkUrl(url)!.AbsoluteUri, ArtworkFixtureBitmap(500), lifetime.Token);
        var cover = await SharedArtworkAsync(url);
        var avatar = await TrackAvatarAsync(url);
        var side = Math.Clamp((int)Math.Ceiling(44 * Window.DpiScale / 32) * 32, 64, 256);
        var expected = side;
        if (cover is not { PixelWidth: 500, PixelHeight: 500 } || avatar == null ||
            avatar.PixelWidth != expected || avatar.PixelHeight != expected || ReferenceEquals(cover, avatar) ||
            !ReferenceEquals(avatar, await TrackAvatarAsync(url)))
            throw new InvalidOperationException("Comment avatars shared oversized cover pixels, exceeded their DPI budget or lost request deduplication.");
        var highDpi = await FetchTrackAvatarAsync(url, 160);
        if (highDpi is not { PixelWidth: 160, PixelHeight: 160 })
            throw new InvalidOperationException("High-DPI avatars retained an undersized API thumbnail instead of decoding the required size.");
        Console.WriteLine($"TRACK_AVATAR_BUDGET_OK: cover=500x500, avatar={expected}x{expected}, retained RGBA bytes per avatar {ArtworkBytes(cover)}->{ArtworkBytes(avatar)}; separate caches and shared repeated avatar");
    }
}
