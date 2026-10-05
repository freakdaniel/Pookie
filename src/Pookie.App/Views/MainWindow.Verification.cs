using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.Audio;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private DispatcherTimer? startupLayoutProbe;
    private string? startupLayoutFailure;
    private int startupFadeFrames;
    private int startupSlideFrames;
    private double startupMaxCenterError;

    // Inspect arranged geometry throughout startup, including partially visible fade-out frames.
    // Playback checks alone cannot detect a loader jumping before it disappears.
    private void StartStartupLayoutProbe()
    {
        startupLayoutProbe = new DispatcherTimer(TimeSpan.FromMilliseconds(16));
        startupLayoutProbe.Tick += SampleStartupLayout;
        startupLayoutProbe.Start();
    }

    private void SampleStartupLayout()
    {
        var viewport = startupSplash.Bounds;
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        var clientSize = Window.ClientSize;
        if (Math.Abs(viewport.X) > 1 || Math.Abs(viewport.Y) > 1 ||
            Math.Abs(viewport.Width - clientSize.Width) > 1 || Math.Abs(viewport.Height - clientSize.Height) > 1)
            startupLayoutFailure ??= "Startup overlay stopped covering the full client area";

        var curtainTop = startupCurtain.Bounds.Y - viewport.Y;
        if (startupBrandLayer.IsVisible && startupBrand.Opacity > 0)
        {
            var brand = startupBrand.Bounds;
            var centerError = Math.Max(
                Math.Abs(brand.X + brand.Width / 2 - (viewport.X + viewport.Width / 2)),
                Math.Abs(brand.Y + brand.Height / 2 - (viewport.Y + viewport.Height / 2)));
            startupMaxCenterError = Math.Max(startupMaxCenterError, centerError);
            if (centerError > 1)
                startupLayoutFailure ??= $"Visible loader moved off center by {centerError:F2} DIP";
            if (curtainTop > 1)
                startupLayoutFailure ??= "Curtain moved before the loader disappeared";
            if (startupBrand.Opacity < 1) startupFadeFrames++;
        }

        if (startupCurtainOffset is >= 0 and < 64)
        {
            if (startupBrandLayer.IsVisible)
                startupLayoutFailure ??= "Loader layer remained visible during curtain movement";
            if (startupHeader.Opacity > 0 || startupContent.Opacity > 0)
                startupLayoutFailure ??= "Page elements appeared before curtain movement completed";
            if (curtainTop is > 1 and < 63) startupSlideFrames++;
            var expectedColor = Surface.Lerp(HeaderSurface, startupCurtainOffset / 64);
            if (startupBackdrop.Background != expectedColor)
                startupLayoutFailure ??= "Top bar color and curtain position used different animation progress";
        }
    }

    private void StopStartupLayoutProbe()
    {
        SampleStartupLayout();
        startupLayoutProbe?.Stop();
        if (startupFadeFrames < 3 || startupSlideFrames < 3)
            startupLayoutFailure ??= $"Missing intermediate animation frames: fade={startupFadeFrames}, slide={startupSlideFrames}";
        var finalOffset = startupCurtain.Bounds.Y - startupSplash.Bounds.Y;
        if (Math.Abs(finalOffset - 64) > 1)
            startupLayoutFailure ??= $"Curtain finished at {finalOffset:F2} DIP instead of 64";
    }

    // Exercises the same state/actions as the UI with local audio and no SoundCloud account.
    private async Task VerifyUiAsync()
    {
        try
        {
            if (startupLayoutFailure != null) throw new InvalidOperationException(startupLayoutFailure);
            Console.WriteLine($"STARTUP_ANIMATION_OK: {startupFadeFrames} fade frames, {startupSlideFrames} slide frames; max loader center error {startupMaxCenterError:F2} DIP; synchronized curtain and top bar color");
            await VerifySearchInputAsync();
            await VerifyPlaybackLoadingAsync();
            await VerifySeekingAsync();
            if (current?.Id != 1 || !isPlaying.Value) throw new InvalidOperationException("Initial playback failed");
            await ToggleAsync();
            if (isPlaying.Value) throw new InvalidOperationException("Pause failed");
            await ToggleAsync();
            await SkipAsync(1);
            if (current?.Id != 2) throw new InvalidOperationException("Next track failed");
            await SkipAsync(-1);
            if (current?.Id != 1) throw new InvalidOperationException("Previous track failed");
            shuffle.Value = true;
            await SkipAsync(1);
            if (current?.Id == 1) throw new InvalidOperationException("Shuffle repeated current track");
            await ToggleAsync();
            queueOpen.Value = true;
            var ids = queueTracks.Select(t => t.Id).ToArray();
            await NavigateAsync(Page.Feed);
            if (!ids.SequenceEqual(queueTracks.Select(t => t.Id))) throw new InvalidOperationException("Navigation changed playback queue");
            await NavigateAsync(Page.Library);
            await NavigateAsync(Page.Home);
            volume.Value = 0;
            ToggleMute();
            if (volume.Value == 0) throw new InvalidOperationException("Unmute failed");
            await VerifyLikesLayoutAsync();
            await VerifyPlayerColorsAsync();
            await VerifyNavigationAsync();
            Console.WriteLine("UI_SMOKE_OK: native layout, local playback, pause/resume, next/previous, shuffle, mute and persistent queue across navigation");
            await Task.Delay(5000, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            Console.Error.WriteLine($"UI_SMOKE_FAILED: {error.GetType().Name}: {error.Message}");
        }
        finally { Window.Close(); }
    }

    private async Task VerifyPlaybackLoadingAsync()
    {
        var audio = player ?? throw new InvalidOperationException("No audio player available");
        var controlled = new ControlledAudioPlayer(audio);
        player = controlled;
        var playerRevealFrames = 0;
        string? playerLayoutFailure = null;
        void SamplePlayerReveal()
        {
            if (playerChrome.ActualHeight is <= 0 or >= PlayerBarHeight) return;
            playerRevealFrames++;
            if (contentSurface.Bounds.Bottom > playerChrome.Bounds.Top + 1)
                playerLayoutFailure ??= "Player reveal overlapped the content instead of reserving a lower row";
        }
        Window.FrameRendered += SamplePlayerReveal;
        ShowLibraryTracks();
        await WaitForLikedLayoutAsync(() => likedTiles.Values.Any(tile => tile.Track?.Id == tracks[0].Id));
        void CheckSelected(SoundCloudTrack track)
        {
            if (current?.Id != track.Id || title.Value != track.Title || artist.Value != track.Author ||
                !playerVisible.Value || !isPlaying.Value || !playbackLoading.Value || !loadingTrack.Running ||
                currentTime.Value != "0:00" || totalTime.Value != FormatTime(track.DurationSeconds) || progress.Value != 0 ||
                !likedTiles.Values.Any(tile => tile.Track?.Id == track.Id && tile.ShowsPause))
                throw new InvalidOperationException("Player or card waited for audio readiness before selecting the track");
        }
        try
        {
            if (playerVisible.Value) throw new InvalidOperationException("Player appeared before selecting a track");
            var first = PlayAsync(tracks[0]);
            CheckSelected(tracks[0]);
            await Task.Delay(300, lifetime.Token);
            var phase = loadingTrack.Phase;
            await Task.Delay(1350, lifetime.Token);
            CheckSelected(tracks[0]);
            if (playerLayoutFailure != null) throw new InvalidOperationException(playerLayoutFailure);
            if (playerRevealFrames < 3) throw new InvalidOperationException("Player appearance skipped intermediate layout frames");
            CheckPlayerRegion();
            await VerifyPlayerTimelineAsync();
            CaptureUiPreview("player-loading");
            if (Math.Abs(phase - loadingTrack.Phase) < 0.01 || loadingTrack.ActualWidth < 100 || progress.IsVisible)
                throw new InvalidOperationException("Loading rail did not animate across a complete loop");
            await ToggleAsync();
            if (isPlaying.Value || likedTiles.Values.Any(tile => tile.Track?.Id == tracks[0].Id && tile.ShowsPause))
                throw new InvalidOperationException("Pause during loading did not update the card");
            controlled.Release();
            await first;
            if (playbackLoading.Value || loadingTrack.Running || !progress.IsVisible || isPlaying.Value || audio.Poll().Playing)
                throw new InvalidOperationException("Audio readiness failed to stop loading or preserve pause");

            var second = PlayAsync(tracks[1]);
            CheckSelected(tracks[1]);
            var third = PlayAsync(tracks[2]);
            await second;
            CheckSelected(tracks[2]);
            if (likedTiles.Values.Any(tile => tile.Track?.Id == tracks[1].Id && tile.ShowsPause))
                throw new InvalidOperationException("Previous card kept the pause icon after switching tracks");
            controlled.Release();
            await third;
            if (!audioReady || playbackLoading.Value || loadingTrack.Running || current?.Id != tracks[2].Id)
                throw new InvalidOperationException("Superseded load changed playback state");

            var failed = PlayAsync(tracks[0]);
            controlled.Fail();
            try { await failed; throw new InvalidOperationException("Expected controlled audio failure"); }
            catch (IOException) { }
            if (playbackLoading.Value || loadingTrack.Running || isPlaying.Value || audioReady)
                throw new InvalidOperationException("Failed loading left playback or animation active");
            var retry = ToggleAsync();
            CheckSelected(tracks[0]);
            controlled.Release();
            await retry;
            Console.WriteLine("UI_LOADING_OK: immediate first player/card selection, looping gradient, pause during loading, rapid switching, failure and retry");
        }
        finally { Window.FrameRendered -= SamplePlayerReveal; controlled.Release(); player = audio; }
        await NavigateAsync(Page.Home);
    }

    // Holds readiness explicitly, while successful requests still play real local audio.
    private sealed class ControlledAudioPlayer(IAudioPlayer inner) : IAudioPlayer
    {
        private TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default)
        {
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await ready.Task.WaitAsync(cancellationToken);
            await inner.PlayAsync(source, cancellationToken);
        }
        public void Release() => ready.TrySetResult();
        public void Fail() => ready.TrySetException(new IOException("Controlled readiness failure"));
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) => inner.SeekAsync(seconds, cancellationToken);
        public void Stop() => inner.Stop();
        public void Pause(bool paused) => inner.Pause(paused);
        public void Volume(double percent) => inner.Volume(percent);
        public AudioState Poll() => inner.Poll();
        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private async Task VerifySeekingAsync()
    {
        var audio = player ?? throw new InvalidOperationException("Audio unavailable");
        var recording = new SeekRecordingPlayer(audio);
        player = recording;
        async Task WaitFor(Func<bool> condition)
        {
            for (var i = 0; i < 100; i++)
            {
                if (condition()) return;
                await Task.Delay(40, lifetime.Token);
            }
            throw new InvalidOperationException("Seek input did not settle");
        }
        try
        {
            await ToggleAsync(); // Hold playback still while verifying position previews.
            BeginSeekDrag();
            for (var i = 0; i < 60; i++)
            {
                progress.Value = 2 + i * .05;
                await Task.Delay(8, lifetime.Token);
            }
            var target = progress.Value;
            Poll();
            if (recording.Seeks.Count != 0 || progress.Value != target || currentTime.Value != FormatTime(target))
                throw new InvalidOperationException("Drag reopened audio or polling overwrote the seek preview");
            EndSeekDrag();
            if (recording.Seeks.Count != 1 || Math.Abs(recording.Seeks[0] - target) > .001)
                throw new InvalidOperationException("Drag release did not commit exactly one seek");
            for (var i = 0; i < 10; i++) { progress.Value = 6 + i * .1; await Task.Delay(20, lifetime.Token); }
            await Task.Delay(200, lifetime.Token);
            if (recording.Seeks.Count != 1) throw new InvalidOperationException("Seek changes opened overlapping streams");
            recording.Release();
            await WaitFor(() => recording.Seeks.Count == 2 && !seeking);
            if (Math.Abs(recording.Seeks[1] - 6.9) > .001) throw new InvalidOperationException("Queued seek lost its latest position");
            var beforeKeyboard = recording.Seeks.Count;
            for (var i = 0; i < 10; i++) { progress.Value = 3 + i * .1; await Task.Delay(20, lifetime.Token); }
            await WaitFor(() => recording.Seeks.Count == beforeKeyboard + 1 && !seeking);
            if (Math.Abs(recording.Seeks[^1] - 3.9) > .001) throw new InvalidOperationException("Keyboard seeks were not coalesced");
            recording.Hold();
            progress.Value = 8;
            await WaitFor(() => recording.Seeks.Count == beforeKeyboard + 2);
            await PlayAsync(tracks[1]);
            if (!recording.LastHeldSeekCancelled || seeking || pendingSeek != null)
                throw new InvalidOperationException("Track switch retained an old seek");
            var beforeSwitch = recording.Seeks.Count;
            progress.Value = 2;
            await PlayAsync(tracks[0]);
            await Task.Delay(220, lifetime.Token);
            if (recording.Seeks.Count != beforeSwitch) throw new InvalidOperationException("Debounced seek was applied to the new track");
            Console.WriteLine("UI_SEEK_OK: drag previews without reopening streams, one seek on release, keyboard coalescing, no overlapping seeks, latest position retained, track-switch cancellation");
        }
        finally { recording.Release(); player = audio; }
    }

    private sealed class SeekRecordingPlayer(IAudioPlayer inner) : IAudioPlayer
    {
        private TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<double> Seeks { get; } = [];
        public bool LastHeldSeekCancelled { get; private set; }
        public void Hold() { gate = new(TaskCreationOptions.RunContinuationsAsynchronously); LastHeldSeekCancelled = false; }
        public void Release() => gate.TrySetResult();
        public async Task SeekAsync(double seconds, CancellationToken cancellationToken = default)
        {
            Seeks.Add(seconds);
            try { await gate.Task.WaitAsync(cancellationToken); await inner.SeekAsync(seconds, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { LastHeldSeekCancelled = true; throw; }
        }
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => inner.PlayAsync(source, cancellationToken);
        public void Stop() => inner.Stop();
        public void Pause(bool paused) => inner.Pause(paused);
        public void Volume(double percent) => inner.Volume(percent);
        public AudioState Poll() => inner.Poll();
        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private async Task VerifyLikesLayoutAsync()
    {
        queueOpen.Value = false;
        var fixtures = Enumerable.Range(0, 30).Select(index => new SoundCloudTrack
        {
            Id = 1000 + index, Duration = 12000, PlaybackCount = 12345, LikesCount = 321, CommentCount = 7,
            CreatedAt = "2026-09-15T12:00:00Z", Genre = "Ambient",
            Title = index == 19 ? "needle / длинное название для проверки фильтра" : $"Трек {index + 1}",
            User = new SoundCloudUser { Username = $"Исполнитель {index % 3}" }
        }).ToArray();
        localRecent.Clear();
        ReplaceTracks(new TrackPage(fixtures, null));
        ShowLibraryTracks();
        await WaitForLikedLayoutAsync(() => likedTiles.Values.Count(tile => tile.Track != null && tile.Root.ActualWidth > 100) >= 12);
        var visible = likedTiles.Values.Where(tile => tile.Track != null).ToArray();
        var firstRow = visible.GroupBy(tile => Math.Round(tile.Root.Bounds.Y)).OrderBy(group => group.Key).First().ToArray();
        if (firstRow.Length != 6) throw new InvalidOperationException($"Expected six likes columns, got {firstRow.Length}");
        if (visible.Any(tile => tile.Root.Bounds.Right > likedGrid.Bounds.Right + 1 ||
            tile.Author.Bounds.Y < tile.Cover.Bounds.Bottom || tile.Title.Bounds.Width > tile.Cover.Bounds.Width))
            throw new InvalidOperationException("Likes card artwork, caption or viewport bounds are incorrect");

        await PlayAsync(fixtures[0]);
        await Task.Delay(220, lifetime.Token);
        var expectedSize = visible[0].Cover.ActualWidth;
        await NavigateAsync(Page.Library);
        await WaitForLikedLayoutAsync(() => libraryTiles.Values.Count(tile => tile.Track != null && tile.Cover.ActualWidth > 100) == 6);
        if (libraryTiles.Values.Any(tile => tile.Track != null && (Math.Abs(tile.Cover.ActualWidth - expectedSize) > 1 ||
            tile.Title.FontSize != visible[0].Title.FontSize || tile.Author.FontWeight != visible[0].Author.FontWeight)))
            throw new InvalidOperationException("Overview and likes use different card geometry or typography");
        CaptureUiPreview("overview");
        ShowLibraryTracks();
        await WaitForLikedLayoutAsync(() => likedGrid.ActualWidth > 100);

        for (var switchIndex = 0; switchIndex < 3; switchIndex++)
        {
            page.Value = Page.Library; RefreshLibraryCards();
            await WaitForLikedLayoutAsync(() => libraryTiles.Values.Any(tile => tile.Track?.Id == fixtures[0].Id && tile.Cover.ActualWidth > 100));
            var overviewActive = libraryTiles.Values.Single(tile => tile.Track?.Id == fixtures[0].Id);
            if (overviewActive.OverlayOpacity < .999) throw new InvalidOperationException("Overview restarted active-card fade");
            ShowLibraryTracks();
            await WaitForLikedLayoutAsync(() => likedTiles.Values.Any(tile => tile.Track?.Id == fixtures[0].Id && tile.Cover.ActualWidth > 100));
            if (likedTiles.Values.Single(tile => tile.Track?.Id == fixtures[0].Id).OverlayOpacity < .999)
                throw new InvalidOperationException("Likes restarted active-card fade");
        }
        libraryPages["collections"] = new([
            new("playlist:one", "Плейлист", "Исполнитель", null, Playlist: new() { Id = 91, Title = "Плейлист", Tracks = [] }),
            new("playlist:two", "Альбом", "Исполнитель", null, Playlist: new() { Id = 92, Title = "Альбом", IsAlbum = true, Tracks = [] })], null);
        libraryPages["stations"] = new([new("station:one", "Станция", "Artist station", null,
            Playlist: new() { Urn = "soundcloud:system-playlists:artist-stations:1:2", Title = "Станция" })], null);
        libraryPages["following"] = new([new("user:one", "Исполнитель", "123 подписчика", null,
            User: new() { Id = 77, Username = "Исполнитель" })], null);
        libraryPages["history"] = new(fixtures.Take(6).Select(LibraryItem.FromTrack).ToArray(), null);
        foreach (var target in new[] { Page.LibraryPlaylists, Page.LibraryAlbums, Page.LibraryStations, Page.LibraryFollowing, Page.LibraryHistory })
        {
            await ShowLibrarySectionAsync(target);
            await WaitForLikedLayoutAsync(() => collectionGrid.ActualWidth > 100);
            var expected = target == Page.LibraryHistory ? 6 : 1;
            if (collectionGrid.ItemsSource.Count != expected || librarySectionTitle.Value != SectionTitle(target))
                throw new InvalidOperationException("Library section loaded the wrong resource type or title");
        }
        page.Value = Page.Library; RefreshOverviewSections();
        if (overviewSections["recent"].Grid.ItemsSource.Count == 0) throw new InvalidOperationException("Recently played was not updated after playback");
        ShowLibraryTracks();
        Console.WriteLine("UI_LIBRARY_OK: all seven tabs, album/playlist separation, stations/following/history, recently played and stable active-card overlays on repeated navigation");

        likedFilter.Text = "NEEDLE";
        await WaitForLikedLayoutAsync(() => likedTiles.Values.Any(tile => tile.Track?.Id == 1019 && tile.Root.ActualWidth > 0));
        if (likedGrid.ItemsSource.Count != 1 || likedList.ItemsSource.Count != 1)
            throw new InvalidOperationException("Likes filter did not find an offscreen track in both views");
        likesAsList.Value = true;
        await WaitForLikedLayoutAsync(() => likedList.ActualWidth > 100);
        await WaitForLikedLayoutAsync(() => likedRows.Values.Any(row => row.Track?.Id == 1019 && row.Waveform.ActualWidth > 100));
        var filteredRow = likedRows.Values.Single(row => row.Track?.Id == 1019);
        if (filteredRow.Cover.ActualWidth != 160 || filteredRow.Cover.ActualHeight != 160 ||
            filteredRow.Author.Bounds.Y >= filteredRow.Title.Bounds.Y || filteredRow.Waveform.Bounds.X <= filteredRow.Cover.Bounds.Right ||
            filteredRow.Root.Bounds.Right > likedList.Bounds.Right + 1 || filteredRow.PlayButton.ActualHeight != 38)
            throw new InvalidOperationException("SoundCloud list layout clipped cover, title, waveform or playback controls");
        likedFilter.Text = "нет такого трека";
        if (!likedEmpty.Value || likedGrid.ItemsSource.Count != 0 || likedList.ItemsSource.Count != 0)
            throw new InvalidOperationException("Likes empty filter state failed");
        likedFilter.Text = "исполнитель 2";
        if (likedGrid.ItemsSource.Count != 10) throw new InvalidOperationException("Artist filtering failed");
        likedFilter.Text = "";
        await WaitForLikedLayoutAsync(() => likedRows.Values.Count(row => row.Track != null && row.Waveform.ActualWidth > 100) >= 3);
        CheckLikedRowsGeometry();
        var firstTrack = fixtures[0];
        await PlayAsync(firstTrack);
        var firstListRow = likedRows.Values.Single(row => row.Track?.Id == firstTrack.Id);
        if (!firstListRow.ShowsPause) throw new InvalidOperationException("List play button did not select the track immediately");
        await ToggleAsync();
        if (firstListRow.ShowsPause) throw new InvalidOperationException("List pause state did not follow playback");
        await SeekLikedTrackAsync(firstTrack, .25);
        await WaitForLikedLayoutAsync(() => !seeking && player!.Poll().Position >= 2.9);
        if (firstListRow.Waveform.Progress < .24) throw new InvalidOperationException("Waveform click did not seek or show playback position");
        foreach (var row in likedRows.Values.Where(row => row.Track != null))
            row.Waveform.SetSamples(Enumerable.Range(0, 1800).Select(index => (float)(.15 + .65 * Math.Abs(Math.Sin(index * .04)))).ToArray());
        CaptureUiPreview("list");
        Console.WriteLine("UI_LIKES_LIST_OK: shared overview/grid cards, 160px artwork, artist above title, waveform bounds, play/pause synchronization and waveform seeking");
        likesAsList.Value = false;
        Window.WindowSize = WindowSize.Resizable(1000, 840, minWidth: 1000, minHeight: 680);
        // The window manager may clamp or round requested sizes; check the applied layout.
        await WaitForLikedLayoutAsync(() => Window.ClientSize.Width < DefaultWindowWidth - 100 && likedTiles.Values
            .Where(tile => tile.Track != null).GroupBy(tile => Math.Round(tile.Root.Bounds.Y)).OrderBy(group => group.Key).FirstOrDefault()?.Count() == 4);
        if (likedGrid.ItemsSource.Count != 30) throw new InvalidOperationException("Resize lost liked tracks");
        likesAsList.Value = true;
        await WaitForLikedLayoutAsync(() => Math.Abs(likedList.ActualWidth - likedGrid.ActualWidth) <= 1 && likedRows.Values.Any(row =>
            row.Track != null && row.Waveform.ActualWidth > 100 && row.Root.Bounds.Right <= likedList.Bounds.Right));
        CheckLikedRowsGeometry();
        if (likedRows.Values.Any(row => row.Track != null && row.Root.Bounds.Right > likedList.Bounds.Right + 1))
            throw new InvalidOperationException("List rows overflowed the narrow window");
        CheckPlayerRegion();
        CaptureUiPreview("list-narrow");
        Console.WriteLine("UI_LIKES_OK: six/four responsive columns, caption bounds, offscreen filtering, artist filter, list/grid toggle and empty state");
    }

    private void CheckPlayerRegion()
    {
        var bounds = playerChrome.Bounds;
        var client = Window.ClientSize;
        if (Math.Abs(bounds.X) > 1 || Math.Abs(bounds.Width - client.Width) > 1 ||
            Math.Abs(bounds.Bottom - client.Height) > 1 || Math.Abs(bounds.Height - PlayerBarHeight) > 1 ||
            contentSurface.Bounds.Bottom > bounds.Top + 1)
            throw new InvalidOperationException("Player did not occupy its own full-width bottom region");
        var controls = ((Grid)playerContentFrame.Child!).Children[1] as FrameworkElement;
        if (controls == null || Math.Abs((controls.Bounds.Top - bounds.Top) - (bounds.Bottom - controls.Bounds.Bottom)) > 1)
            throw new InvalidOperationException("Player controls have unequal top and bottom spacing");
        VisualTree.Visit(playerContentFrame, element =>
        {
            if (element is FrameworkElement control && control.IsVisible && element is Button or TextBlock &&
                (control.Bounds.X < bounds.Left - 1 || control.Bounds.Right > bounds.Right + 1 ||
                 control.Bounds.Y < bounds.Top - 1 || control.Bounds.Bottom > bounds.Bottom + 1))
                throw new InvalidOperationException("Player text or controls escaped the bottom region after resizing");
        });
    }

    private async Task VerifyPlayerTimelineAsync()
    {
        var rail = (Grid)playerTimeline.Children[1];
        var expected = rail.Bounds;
        var savedPosition = currentTime.Value;
        var savedDuration = totalTime.Value;
        try
        {
            foreach (var (position, duration) in new[]
            {
                ("0:00", "2:33"), ("0:11", "2:33"), ("0:59", "9:59"), ("1:00", "10:00"),
                ("9:59", "59:59"), ("10:00", "1:00:00"), ("1:00:00", "11:11:11"), ("11:11:11", "88:58:58")
            })
            {
                currentTime.Value = position; totalTime.Value = duration;
                await WaitForLoginFrameAsync();
                if (Math.Abs(rail.Bounds.X - expected.X) > .01 || Math.Abs(rail.Bounds.Width - expected.Width) > .01 ||
                    Math.Abs(loadingTrack.Bounds.X - expected.X) > 1 || Math.Abs(loadingTrack.ActualWidth - expected.Width) > 1)
                    throw new InvalidOperationException("Playback timeline moved or changed width when time digits changed");
                foreach (var text in playerTimeline.Children.OfType<TextBlock>())
                    if (text.Bounds.Left < playerTimeline.Bounds.Left - 1 || text.Bounds.Right > playerTimeline.Bounds.Right + 1)
                        throw new InvalidOperationException("Playback time escaped its fixed container");
            }
        }
        finally { currentTime.Value = savedPosition; totalTime.Value = savedDuration; }
        await WaitForLoginFrameAsync();
        Console.WriteLine("UI_PLAYER_LAYOUT_OK: equal vertical spacing and stable timeline bounds across different digits, minute rollover and hour-long durations");
    }

    private void CheckLikedRowsGeometry()
    {
        var rows = likedRows.Values.Where(row => row.Track != null).OrderBy(row => row.Root.Bounds.Y).ToArray();
        for (var index = 1; index < rows.Length; index++)
            if (rows[index].Root.Bounds.Y < rows[index - 1].Root.Bounds.Bottom + 30)
                throw new InvalidOperationException("Liked list rows overlap or lose their vertical gap");
        if (rows.Any(row => row.Title.Bounds.Bottom > row.Waveform.Bounds.Y ||
            row.Waveform.Bounds.Bottom > row.Root.Bounds.Bottom + 1))
            throw new InvalidOperationException("Liked list content overlaps or escapes its row");
    }

    // Optional rendering of the app's own visual tree into a CPU surface for visual QA.
    private void CaptureUiPreview(string name)
    {
        if (Environment.GetEnvironmentVariable("POOKIE_UI_PREVIEW") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var scale = Window.DpiScale;
        var width = (int)Math.Ceiling(Window.ClientSize.Width * scale);
        var height = (int)Math.Ceiling(Window.ClientSize.Height * scale);
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(width, height, scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        context.FillRectangle(new Rect(0, 0, Window.ClientSize.Width, Window.ClientSize.Height), HeaderSurface);
        Window.Content?.Render(context);
        context.EndFrame();
        if (surface is not Aprillz.MewUI.Rendering.ICpuPixelSurface cpu) throw new InvalidOperationException("Preview surface is not readable");
        var pixels = cpu.GetReadOnlyPixelSpan();
        using var file = File.Create(Path.Combine(directory, name + ".ppm"));
        file.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        var row = new byte[width * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * cpu.StrideBytes + x * 4;
                row[x * 3] = pixels[offset + 2]; row[x * 3 + 1] = pixels[offset + 1]; row[x * 3 + 2] = pixels[offset];
            }
            file.Write(row);
        }
    }

    private async Task WaitForLikedLayoutAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (ready()) return;
            await Task.Delay(50, lifetime.Token);
        }
        var rows = string.Join(",", likedTiles.Values.Where(tile => tile.Track != null)
            .GroupBy(tile => Math.Round(tile.Root.Bounds.Y)).Select(group => $"{group.Key}:{group.Count()}"));
        throw new InvalidOperationException($"Likes layout did not settle: grid={likedGrid.ActualWidth}x{likedGrid.ActualHeight}, items={likedGrid.ItemsSource.Count}, tiles={likedTiles.Count}, rows={rows}, window={Window.ClientSize.Width}, tile width={likedTiles.Values.FirstOrDefault()?.Root.ActualWidth}");
    }
}
