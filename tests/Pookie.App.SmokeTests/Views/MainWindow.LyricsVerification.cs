using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.Lyrics;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyLyricsUiAsync()
    {
        var original = player; var originalService = lyricsService;
        var audio = new BufferFixturePlayer(); var provider = new LyricsFixtureProvider();
        using var service = new LyricsService(provider, Path.Combine(dataPaths.Cache, "FixtureLyrics"));
        timer.Stop(); player = audio; lyricsService = service;
        try
        {
            var track = new SoundCloudTrack { Id = 901, Title = "Тестовая песня", Duration = 3000000, User = new() { Username = "Pookie preview" } };
            var published = IdentifyLyrics(track with { PublisherMetadata = new() { Artist = "Исполнитель", AlbumTitle = "Альбом" } });
            if (published.Artist != "Исполнитель" || published.Album != "Альбом" ||
                IdentifyLyrics(track with { PublisherMetadata = new() { Artist = " " } }).Artist != track.Author ||
                IdentifyLyrics(track with { FullDuration = track.Duration + 3000 }).TimingAvailable)
                throw new InvalidOperationException("Lyrics identity lost publisher metadata or treated a preview as a full recording.");
            ReplaceTracks(new([track], null)); SetQueue(track); await PlayAsync(track);
            if (provider.Calls != 0) throw new InvalidOperationException("Closed lyrics panel fetched the network.");
            var pixels = new byte[256 * 256 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            { pixels[i] = 35; pixels[i + 1] = 125; pixels[i + 2] = 38; pixels[i + 3] = 255; }
            SetPlayerArtwork(ImageSource.FromBgraPixels(256, 256, pixels), playGeneration);
            SetExpandedPlayer(true); ToggleExpandedPanel(PlayerPanel.Lyrics);
            VerifyDirectLyricsReveal();
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning && lyricsSkeleton.IsVisible);
            CaptureExpandedPanelPreview("lyrics-loading"); provider.Reply.SetResult(provider.Record);
            await WaitForLikedLayoutAsync(() => lyricsResult.Status == LyricsStatus.Synced && lyricsList.ActualHeight > 100 && lyricsRequest == null);
            await VerifyLyricsLoadingFadeAsync("lyrics-transition");
            audio.State = new(12, 3000, false, false, false); Poll(); await Task.Delay(900, lifetime.Token);
            var active = lyricsRows.Single(pair => pair.Value.Row?.Index == 4);
            if (lyricsActiveLine != 4 || Math.Abs(active.Key.Bounds.Y + active.Key.ActualHeight / 2 - lyricsScroll.Bounds.Y - lyricsScroll.ActualHeight * .4) > 4 || active.Value.Text.Opacity < .99)
                throw new InvalidOperationException("Active lyric was not centered/highlighted.");
            if (lyricsRows.Count > 64) throw new InvalidOperationException("Lyrics materialized all 1000 rows instead of virtualizing them.");
            CaptureExpandedPanelPreview("lyrics-synced");
            var restingGap = lyricsRows.Values.Single(view => view.Row?.Index == 3);
            if (LyricsWaveDots.Diameter <= 10 || restingGap.Dots.WaveAmount != 0 ||
                Enumerable.Range(0, 3).Any(index => restingGap.Dots.VerticalOffset(index) != 0) ||
                Math.Abs(restingGap.Dots.Opacity - restingGap.Text.Opacity) > .01)
                throw new InvalidOperationException("Inactive dots were not thicker, aligned and shaded like lyric lines.");
            await Task.Delay(300, lifetime.Token); Poll();
            if (lyricsActiveLine != 4) throw new InvalidOperationException("Paused lyrics advanced with wall time.");

            var next = lyricsRows.First(pair => pair.Value.Row?.Index == 5);
            var beforeHover = next.Value.Text.Opacity;
            RouteExpandedPointer(new Point(next.Key.Bounds.X + next.Key.ActualWidth / 2, next.Key.Bounds.Y + next.Key.ActualHeight / 2));
            await Task.Delay(80, lifetime.Token);
            if (next.Value.Text.Opacity <= beforeHover || next.Value.Text.Opacity >= 1)
                throw new InvalidOperationException("Lyric hover had no intermediate animation.");
            await Task.Delay(220, lifetime.Token);
            if (next.Value.Text.Opacity < .99 || next.Key.Background != Color.Transparent)
                throw new InvalidOperationException("Lyric hover did not highlight the text transparently.");
            RouteWaveformClick(new Point(next.Key.Bounds.X + next.Key.ActualWidth / 2, next.Key.Bounds.Y + next.Key.ActualHeight / 2));
            await WaitForLikedLayoutAsync(() => audio.SeekCount > 0);
            if (Math.Abs(audio.State.Position - 15) > .001) throw new InvalidOperationException("Clicking a lyric did not seek to its timestamp.");
            RouteExpandedPointer(new Point(1, 1));

            // The real seven-second timer is reset by another wheel event.
            var wheelPoint = new Point(lyricsScroll.Bounds.X + 40, lyricsScroll.Bounds.Y + 80);
            RouteSearchTestWheel(wheelPoint); await Task.Delay(650, lifetime.Token);
            var offset = lyricsScroll.VerticalOffset;
            audio.State = audio.State with { Position = 90 }; Poll(); await Task.Delay(1800, lifetime.Token);
            if (lyricsFollow || Math.Abs(offset - lyricsScroll.VerticalOffset) > 1)
                throw new InvalidOperationException("Lyric changes took over a manually scrolled viewport.");
            RouteSearchTestWheel(wheelPoint); await Task.Delay(5500, lifetime.Token);
            if (lyricsFollow) throw new InvalidOperationException("Auto-follow timer did not restart after scrolling.");
            await WaitForLikedLayoutAsync(() => lyricsFollow); await Task.Delay(900, lifetime.Token);
            var followed = lyricsRows.Single(pair => pair.Value.Row?.Index == 30).Key;
            if (Math.Abs(followed.Bounds.Y + followed.ActualHeight / 2 - lyricsScroll.Bounds.Y - lyricsScroll.ActualHeight * .4) > 4)
                throw new InvalidOperationException("Timed return did not smoothly center the current line.");

            audio.State = audio.State with { Position = 9, Playing = true }; Poll();
            await WaitForLikedLayoutAsync(() => lyricsRows.Values.Any(view => view.Row?.Index == 3 && view.Dots.IsVisible));
            await Task.Delay(80, lifetime.Token);
            var startingGap = lyricsRows.Values.Single(view => view.Row?.Index == 3);
            if (startingGap.Dots.WaveAmount <= 0 || startingGap.Dots.WaveAmount >= 1 || startingGap.Dots.Opacity >= 1)
                throw new InvalidOperationException("Wave dots snapped into movement or active color.");
            await Task.Delay(350, lifetime.Token);
            var gap = lyricsRows.Values.Single(view => view.Row?.Index == 3);
            if (!gap.Dots.IsVisible || gap.Text.IsVisible) throw new InvalidOperationException("Timed gap did not use the white wave dots.");
            var phase = gap.Dots.Phase; await Task.Delay(150, lifetime.Token);
            if (phase == gap.Dots.Phase) throw new InvalidOperationException("Wave dots did not move while playing.");
            audio.State = audio.State with { Playing = false }; Poll();
            phase = gap.Dots.Phase; await Task.Delay(250, lifetime.Token);
            if (phase != gap.Dots.Phase) throw new InvalidOperationException("Wave dots kept moving on pause.");
            await Task.Delay(150, lifetime.Token);
            if (gap.Dots.WaveAmount != 0) throw new InvalidOperationException("Paused dots did not settle into a row.");
            audio.State = audio.State with { Playing = true }; Poll(); await Task.Delay(400, lifetime.Token);
            CaptureExpandedPanelPreview("lyrics-gap");
            audio.State = audio.State with { Position = 12 }; Poll(); await Task.Delay(80, lifetime.Token);
            if (gap.Dots.WaveAmount <= 0 || gap.Dots.WaveAmount >= 1 || gap.Dots.Opacity <= .48 || gap.Dots.Opacity >= 1)
                throw new InvalidOperationException("Completed gap did not smoothly settle or dim.");
            await Task.Delay(350, lifetime.Token);
            if (gap.Dots.WaveAmount != 0 || Math.Abs(gap.Dots.Opacity - .48) > .01)
                throw new InvalidOperationException("Completed gap did not return to an aligned inactive row.");
            CaptureExpandedPanelPreview("lyrics-gap-settled");

            PauseLyricsFollowing(); lyricsList.ScrollIntoView(lyricsItems.DataCount - 2);
            await WaitForLikedLayoutAsync(() => lyricsRows.Values.Any(view => view.Row?.Index == -3 && view.Text.ActualHeight > 0));
            var credit = lyricsRows.Values.Single(view => view.Row?.Index == -3);
            if (credit.Text.Text != "Текст предоставлен LRCLIB" || credit.Text.FontSize != 13)
                throw new InvalidOperationException("Source credit was not at the end of the lyric list.");
            CaptureExpandedPanelPreview("lyrics-credit");
            ResumeLyricsFollowing();
            SetExpandedPlayer(false); await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            SetExpandedPlayer(true); VerifyDirectLyricsReveal(); await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            if (provider.Calls != 1) throw new InvalidOperationException("Reopening lyrics repeated a resolved lookup.");
            ToggleExpandedPanel(PlayerPanel.Lyrics);
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning);
            ToggleExpandedPanel(PlayerPanel.Queue);
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning);
            ToggleExpandedPanel(PlayerPanel.Queue);
            // Reopen during the closing motion: retained queue content must not slide out.
            ToggleExpandedPanel(PlayerPanel.Lyrics); VerifyDirectLyricsReveal();
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning);
            ToggleExpandedPanel(PlayerPanel.Queue);
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning && expandedPanel.ContentOpacity == 1);
            ToggleExpandedPanel(PlayerPanel.Lyrics);
            if (expandedPanel.Transition.Kind != ContentTransitionKind.Slide || expandedPanel.ContentOpacity >= 1)
                throw new InvalidOperationException("Switching two visible panels lost the content transition.");
            await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning && expandedPanel.ContentOpacity == 1);

            var plain = provider.Record with { SyncedLyrics = null, PlainLyrics = "Первая строка без времени\nВторая строка без времени" };
            ApplyLyrics(new(LyricsStatus.Plain, plain.ToDocument()));
            await Task.Delay(350, lifetime.Token); await WaitForLoginFrameAsync(); CaptureExpandedPanelPreview("lyrics-plain");
            if (lyricsRows.Keys.Any(button => button.IsHitTestVisible)) throw new InvalidOperationException("Plain text offered timestamp seeking.");
            foreach (var status in new[] { LyricsStatus.NotFound, LyricsStatus.Instrumental, LyricsStatus.Unavailable })
            {
                ApplyLyrics(new(LyricsStatus.Loading)); await WaitForLoginFrameAsync();
                ApplyLyrics(new(status)); await VerifyLyricsLoadingFadeAsync("lyrics-empty-transition");
                if (!lyricsMessage.IsVisible || lyricsMessage.Text != "Для текущего трека\nотсутствует текст" || lyricsMessage.FontWeight != FontWeight.Bold ||
                    Math.Abs(lyricsMessage.FontSize - lyricsFontSize) > .1)
                    throw new InvalidOperationException("Missing text was not a two-line active-style caption.");
            }
            CaptureExpandedPanelPreview("lyrics-empty");
            ApplyLyrics(new(LyricsStatus.Synced, provider.Record.ToDocument()));
            Window.WindowState = WindowState.Normal;
            Window.WindowSize = WindowSize.Resizable(1000, 680, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => lyricsScroll.ActualWidth < 500 && lyricsFontSize <= 32 && !expandedLayout.IsMeasureDirty);
            await Task.Delay(650, lifetime.Token); CaptureExpandedPanelPreview("lyrics-narrow");
            Window.WindowState = WindowState.FullScreen;
            await WaitForLikedLayoutAsync(() => lyricsScroll.ActualWidth > 500);

            var spedIdentity = new LyricsIdentity(track.Id, track.Title + " (ꜱᴘᴇᴇᴅ + reverb by DJ Speed)", track.Author, 2500);
            var adjusted = LyricsMatcher.SpedUpDocument(spedIdentity, provider.Record);
            ApplyLyrics(new(LyricsStatus.Synced, adjusted));
            audio.State = audio.State with { Position = 50 }; Poll();
            await WaitForLikedLayoutAsync(() => lyricsActiveLine == 20 && lyricsRows.Values.Any(view => view.Row?.Index == 21));
            await Task.Delay(650, lifetime.Token);
            var spedRow = lyricsRows.First(pair => pair.Value.Row?.Index == 21).Key;
            RouteWaveformClick(new Point(spedRow.Bounds.X + spedRow.ActualWidth / 2, spedRow.Bounds.Y + spedRow.ActualHeight / 2));
            await WaitForLikedLayoutAsync(() => Math.Abs(audio.State.Position - 52.5) < .001);
            CaptureExpandedPanelPreview("lyrics-spedup");
            ApplyLyrics(new(LyricsStatus.Synced, provider.Record.ToDocument()));

            var calls = provider.Calls;
            var track2 = track with { Id = 902, Title = "Новый трек" };
            provider.Record = provider.Record with { TrackName = "Новый трек" };
            provider.Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await PlayAsync(track2); await WaitForLikedLayoutAsync(() => provider.Calls == calls + 1);
            var oldReply = provider.Reply;
            provider.Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Record = provider.Record with { TrackName = "Следующий трек" };
            await PlayAsync(track2 with { Id = 903, Title = "Следующий трек" });
            oldReply.SetResult(provider.Record with { TrackName = "Новый трек" });
            await WaitForLikedLayoutAsync(() => provider.Calls == calls + 2); provider.Reply.SetResult(provider.Record);
            await WaitForLikedLayoutAsync(() => lyricsResult.Document?.Title == "Следующий трек");
            if (lyricsIdentity?.TrackId != 903) throw new InvalidOperationException("Late lyric reply replaced the selected track.");
            Console.WriteLine("LYRICS_UI_OK: direct panel reveal without queue slides, skeleton crossfade to text/missing, larger shaded wave dots with smooth aligned start/finish, 1000 virtualized lines, original/scaled click seeking and highlighting, seven-second return, end credit, responsive layout and stale replies");
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("LYRICS_UI_FAILED: " + error); }
        finally { player = original; lyricsService = originalService; Window.Close(); }
    }

    private void VerifyDirectLyricsReveal()
    {
        if (expandedPanel.Content != expandedLyricsContent || expandedPanel.ContentOpacity != 1)
            throw new InvalidOperationException("Opening hidden lyrics started a content slide.");
        ((IVisualTreeHost)expandedPanel).VisitChildren(element =>
        {
            if (element == expandedQueueContent) throw new InvalidOperationException("Hidden queue appeared as outgoing lyrics content.");
            return true;
        });
    }

    private async Task VerifyLyricsLoadingFadeAsync(string preview)
    {
        await Task.Delay(80, lifetime.Token);
        if (!lyricsReveal.IsRunning || !lyricsSkeleton.IsVisible || lyricsContent.Opacity <= 0 || lyricsContent.Opacity >= 1 ||
            lyricsSkeleton.Opacity <= 0 || lyricsSkeleton.Opacity >= 1 || Math.Abs(lyricsContent.Opacity + lyricsSkeleton.Opacity - 1) > .001)
            throw new InvalidOperationException("Skeleton did not crossfade through intermediate opacity to the result.");
        CaptureExpandedPanelPreview(preview);
        await WaitForLikedLayoutAsync(() => !lyricsReveal.IsRunning);
        if (lyricsSkeleton.IsVisible || lyricsContent.Opacity != 1)
            throw new InvalidOperationException("Lyrics result did not finish fading in or retained the skeleton.");
    }

    private sealed class LyricsFixtureProvider : ILyricsProvider
    {
        public int Calls;
        public TaskCompletionSource<LyricsCandidate?> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LyricsCandidate Record = new()
        {
            Id = 9001, TrackName = "Тестовая песня", ArtistName = "Pookie preview", Duration = 3000,
            SyncedLyrics = string.Join('\n', Enumerable.Range(0, 1000).Select(index =>
                $"[{index * 3 / 60:00}:{index * 3 % 60:00}.00]" + (index == 3 ? "" : index % 7 == 2 ?
                    "Длинная тестовая строка, которая переносится по ширине области" : $"Тестовая строка {index + 1}")))
        };
        public Task<LyricsCandidate?> GetAsync(LyricsQuery query, CancellationToken token) { Calls++; return Reply.Task; }
        public Task<LyricsCandidate[]> SearchAsync(string title, string artist, CancellationToken token) { Calls++; return Task.FromResult(new[] { Record }); }
    }
}
