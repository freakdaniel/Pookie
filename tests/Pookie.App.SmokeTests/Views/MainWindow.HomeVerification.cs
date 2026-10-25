using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.App.Home;
using Pookie.App.Playback;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyHomeUiAsync()
    {
        var originalPlayer = player;
        try
        {
            Window.WindowSize = WindowSize.Resizable(1400, 980, minWidth: 1000, minHeight: 680);
            var transport = new HomeFixtureTransport();
            api.Session = new("homefixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Home fixture" }; signedIn.Value = true;
            await imageDiskCache.WriteAsync(HomeFixtureTransport.Artwork, ArtworkFixtureBitmap(500), lifetime.Token);
            await ShowWorkspaceAsync();
            var load = NavigateHomeAsync();
            await WaitForLikedLayoutAsync(() => HomeItems("recent").Length == 6 && homePending.Contains("recommendations"));
            if (!homeSections.Single(section => section.Key == "recommendations").Loading.Skeleton.IsVisible)
                throw new InvalidOperationException("Slow recommendation request hid the loaded recent section or lost its skeleton.");
            transport.RelatedReady.SetResult(); await load;
            await WaitForLikedLayoutAsync(() => homeSections.All(section => section.Root.IsVisible) && libraryColumns == 6 &&
                collectionCards.Keys.Any(root => homeSections[0].Cards.IsAncestorOf(root) && root.ActualWidth > 100));
            if (HomeItems("recommendations").Length != 12 || HomeItems("releases").Length != 6 || HomeItems("mixes").Length != 3 || homePending.Count != 0)
                throw new InvalidOperationException("Personal Home did not bind the expected independent sections.");
            if (homeSections.Any(section => section.Key == "recent"))
                throw new InvalidOperationException("Removed Continue section is still on Home.");
            await WaitForLikedLayoutAsync(() => homeSections.All(section => !section.Loading.Skeleton.IsVisible));
            if (homeLikesCard.CornerRadius != 16 || homeLikesCard.Height != 88 || homeHistoryCard.CornerRadius != 16 ||
                homeLikesSubtitle.Text != "6 треков" || homeHistorySubtitle.Text.Length == 0)
                throw new InvalidOperationException("Home shortcuts lost their card shape or personal captions.");
            await WaitForLoginFrameAsync(); CaptureUiPreview("home-wide");
            homeScroll.SetScrollOffsets(0, 400); await WaitForLoginFrameAsync(); CaptureUiPreview("home-recommendations");
            if (homeScroll.VerticalOffset < 350) throw new InvalidOperationException("Home sections are not vertically scrollable.");
            homeScroll.SetScrollOffsets(0, 0);
            Window.WindowSize = WindowSize.Resizable(1000, 980, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => libraryColumns == 4 && Math.Abs(homeSections[0].Cards.Height - 3 * (likedArtworkSize + 90)) < 1);
            await WaitForLoginFrameAsync(); CaptureUiPreview("home-narrow");
            Window.WindowSize = WindowSize.Resizable(1400, 980, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => libraryColumns == 6);
            player = null; // Exercise actual mouse routing without requesting audio.
            await WaitForLikedLayoutAsync(() => wavePlay.ActualWidth > 40);
            if (Math.Abs(homeHero.ActualHeight - 320) > 1 / Window.DpiScale || homeHero.ActualWidth < 1200)
                throw new InvalidOperationException($"Wave hero no longer fills the available width: hero={homeHero.Bounds}, client={Window.ClientSize}, content={contentSurface.Bounds}.");
            ClickHomeElement(wavePlay);
            await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == WaveQueueKey && !homeHeroPlaying);
            if (page.Value != Page.Home || playbackQueue.Snapshot.Source.Count < 3)
                throw new InvalidOperationException("Wave play navigated or failed to create a ranked queue.");
            if (waveCandidateFetch != null) await waveCandidateFetch;
            current = playbackQueue.Current!.Track; isPlaying.Value = true; RefreshLikedPlayback();
            if (wavePlayLabel.Text != "Пауза" || !waveBackdrop.Running) throw new InvalidOperationException("Wave ignored player state.");
            var selected = playbackQueue.Current;
            var manual = playbackQueue.Enqueue(transport.Recent[5]);
            ClickHomeElement(waveSettingsButton);
            await WaitForLikedLayoutAsync(() => waveSettingsOpen.Value && homeWaveLayout.Expansion > .999999 && waveModeButtons[WaveMode.Favorites].ActualWidth > 60);
            VerifyWaveDrawerBounds();
            var paletteBefore = waveBackdrop.Colors;
            var paletteFrames = new HashSet<PlayerGradient>();
            void SampleWaveColor() => paletteFrames.Add(waveBackdrop.Colors);
            Window.FrameRendered += SampleWaveColor;
            try
            {
                ClickHomeElement(waveModeButtons[WaveMode.Favorites]);
                if (waveBackdrop.Colors != paletteBefore || !waveBackdrop.ChangingColor)
                    throw new InvalidOperationException("Wave color changed immediately instead of transitioning.");
                if (playbackQueue.Current != selected || !playbackQueue.Snapshot.ManualUpcoming.Contains(manual) ||
                    playbackQueue.Snapshot.ContextUpcoming.Any(entry => entry.Track.Id is < 101 or > 106))
                    throw new InvalidOperationException("Favorite mode changed the current/manual queue or included unknown music.");
                RouteWaveformPointer(new(homeHero.Bounds.X + 12, homeHero.Bounds.Y + 12));
                await WaitForLikedLayoutAsync(() => !waveBackdrop.ChangingColor);
                if (paletteFrames.Count < 4 || waveBackdrop.Colors == paletteBefore)
                    throw new InvalidOperationException($"Wave color transition did not render intermediate colors: frames={paletteFrames.Count}; unchanged={waveBackdrop.Colors == paletteBefore}; window={Window.WindowState}.");
                await WaitForLoginFrameAsync(); CaptureUiPreview("wave-settings");
                // Retarget an unfinished transition while paused; colors must stay continuous.
                isPlaying.Value = false; RefreshHomeHeroPlayback();
                var favoritePalette = waveBackdrop.Colors;
                SetWaveMode(WaveMode.Balance);
                await WaitForLikedLayoutAsync(() => waveBackdrop.ChangingColor && waveBackdrop.Colors != favoritePalette);
                var interruptedPalette = waveBackdrop.Colors;
                ClickHomeElement(waveModeButtons[WaveMode.Discoveries]);
                if (waveBackdrop.Colors != interruptedPalette || !waveBackdrop.ChangingColor || waveBackdrop.Running)
                    throw new InvalidOperationException("Retargeting a paused wave snapped its color or restarted motion.");
                if (playbackQueue.Snapshot.ContextUpcoming.Any(entry => entry.Track.Id is >= 101 and <= 106))
                    throw new InvalidOperationException("Discovery mode included familiar tracks.");
                await WaitForLikedLayoutAsync(() => !waveBackdrop.ChangingColor);
                // Native windows may cap the requested height to the monitor work area.
                var tallHeight = Math.Min(980, Window.ClientSize.Height);
                for (var index = 0; index < 9; index++)
                {
                    var width = index == 8 ? 1600 : index % 2 == 0 ? 1000 : 1400;
                    var height = index == 8 ? tallHeight : index % 2 == 0 ? 680 : tallHeight;
                    Window.WindowSize = WindowSize.Resizable(width, height, minWidth: 1000, minHeight: 680);
                    await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width - width) < 1 && Math.Abs(Window.ClientSize.Height - height) < 1 &&
                        Math.Abs(homeWaveLayout.ActualHeight - waveSettingsPanel.ActualHeight - HomeWaveLayout.HeaderInset - HomeWaveLayout.DrawerInset * 2) < 1);
                    VerifyWaveDrawerBounds();
                    if (index == 8)
                    {
                        await WaitForLoginFrameAsync(); CaptureUiPreview("wave-settings-wide");
                        if (waveSettingsPanel.Bounds.Right <= contentFrame.Bounds.Right)
                            throw new InvalidOperationException($"Wave drawer followed the capped content frame instead of the outer surface: panel={waveSettingsPanel.Bounds}, frame={contentFrame.Bounds}, frameWidth={contentFrame.Width}, surface={contentSurface.Bounds}.");
                    }
                    if (index == 0)
                    {
                        await WaitForLoginFrameAsync(); CaptureUiPreview("wave-settings-short");
                        // Force overflow even when the current settings fit the minimum window.
                        var settingsHeight = waveSettingsScroll.Height;
                        waveSettingsScroll.Height = 160;
                        await WaitForLikedLayoutAsync(() => waveSettingsScroll.ViewportHeight <= 160 && SmoothScroll.Maximum(waveSettingsScroll) > 0);
                        var mainOffset = homeScroll.VerticalOffset;
                        waveSettingsScroll.SetScrollOffsets(0, 1000);
                        await WaitForLikedLayoutAsync(() => waveSettingsScroll.VerticalOffset > 0);
                        if (homeScroll.VerticalOffset != mainOffset)
                            throw new InvalidOperationException("Scrolling settings moved the Home page.");
                        waveSettingsScroll.SetScrollOffsets(0, 0);
                        waveSettingsScroll.Height = settingsHeight;
                    }
                }
                ClickHomeElement(waveSettingsClose);
                await WaitForLikedLayoutAsync(() => homeWaveLayout.Expansion < .000001);
                if (waveSettingsPanel.IsVisible || waveSettingsPanel.IsHitTestVisible || waveSettingsOpen.Value)
                    throw new InvalidOperationException("Closing the inline drawer left an invisible interactive layer.");
                ClickHomeElement(waveSettingsButton);
                await WaitForLikedLayoutAsync(() => homeWaveLayout.Expansion > .999999);
                var escape = new KeyEventArgs(Key.Escape, 0);
                typeof(Window).GetMethod("RaisePreviewKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(Window, [escape]);
                await WaitForLikedLayoutAsync(() => homeWaveLayout.Expansion < .000001);
                if (!escape.Handled || waveSettingsOpen.Value)
                    throw new InvalidOperationException("Escape did not close the inline wave drawer.");
                isPlaying.Value = true; RefreshHomeHeroPlayback();
            }
            finally { Window.FrameRendered -= SampleWaveColor; }
            var rejected = current;
            await RejectWaveTrackAsync();
            if (waveTaste.Rank([rejected], [], 10, DateTimeOffset.UtcNow).Length != 0 || playbackQueue.Current?.EntryId != manual.EntryId)
                throw new InvalidOperationException("Dislike did not exclude the song or skipped a manually queued track.");
            // Exhaust the initial discoveries; the provider must find another source and refill.
            var firstIds = playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).ToHashSet();
            while (playbackQueue.Snapshot.Upcoming.Any())
            {
                var entry = playbackQueue.Next()!;
                waveTaste.Record(entry.Track, 90, false, DateTimeOffset.UtcNow);
            }
            current = playbackQueue.Current!.Track;
            await queueLoader.EnsureAheadAsync(force: true);
            if (!playbackQueue.Snapshot.Upcoming.Any(entry => !firstIds.Contains(entry.Track.Id)))
                throw new InvalidOperationException("Wave stopped after the initial candidate pool.");
            SaveWaveProfile();
            if (waveStore.Load(42).Mode != WaveMode.Discoveries) throw new InvalidOperationException("Wave settings were not persisted.");
            SetWaveMode(WaveMode.Balance);
            current = null; isPlaying.Value = false; playbackQueue.Clear(); RefreshLikedPlayback();
            var root = collectionCards.Keys.First(root => homeSections[0].Cards.IsAncestorOf(root) && root.ActualWidth > 100);
            var card = CollectionCard(root)!; var id = card.Item!.Track!.Id;
            CaptureUiPreview("home-card-before-hover");
            RouteWaveformPointer(new(card.Frame.Bounds.X + 12, card.Frame.Bounds.Y + 12));
            await WaitForLikedLayoutAsync(() => card.Playback.Opacity > .999);
            ClickHomeElement(card.Playback.PlaybackButton!);
            await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == "home:recommendations");
            if (page.Value != Page.Home || playbackQueue.Current?.Track.Id != id || playbackQueue.Snapshot.Source.Count != 12)
                throw new InvalidOperationException("Home play button navigated or copied the wrong queue.");
            var queueIds = playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).ToArray();
            var reads = transport.HomeReads;
            ClickHomeElement(card.Title);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState?.Track.Id == id && !trackDetailState.MetadataLoading);
            if (current != null || !queueIds.SequenceEqual(playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id)))
                throw new InvalidOperationException("Home card navigation started playback or replaced its queue.");
            localRecent.Insert(0, LibraryItem.FromTrack(transport.Recent[5]));
            await NavigateHomeAsync();
            if (HomeItems("recent")[0].Track?.Id != 106) throw new InvalidOperationException("Cached Home did not include the latest local playback.");
            if (transport.HomeReads != reads) throw new InvalidOperationException("Cached Home repeated section requests on return.");
            transport.FailMixes = true;
            await NavigateHomeAsync(force: true);
            if (!homeErrors.Contains("mixes") || HomeItems("recent").Length != 6 || HomeItems("recommendations").Length != 12 ||
                homeSections.Any(section => !section.Root.IsVisible))
                throw new InvalidOperationException("One failed section cleared other Home content or has no retry indication.");
            transport.FailMixes = false;
            await NavigateHomeAsync(force: true);
            if (homeErrors.Count != 0 || HomeItems("mixes").Length != 3) throw new InvalidOperationException("Home refresh did not recover a failed section.");

            transport.FailMixes = true; await NavigateHomeAsync(force: true);
            ClickHomeElement(wavePlay);
            await WaitForLikedLayoutAsync(() => playbackQueue.Context?.Key == WaveQueueKey);
            if (playbackQueue.Snapshot.Source.Count < 3 || page.Value != Page.Home)
                throw new InvalidOperationException("Missing personalized mixes stopped the wave.");
            transport.FailMixes = false;
            if (waveCandidateFetch != null) await waveCandidateFetch;

            // Compact rows now live in the feed's recent sidebar; the former
            // standalone list is no longer attached to the feed page.
            localRecent.Clear();localRecent.AddRange(transport.Recent.Select(track=>LibraryItem.FromTrack(track)));
            await OpenFeedAsync(force:true);
            await WaitForLikedLayoutAsync(() => feedSidebarRows.Any(row => row.Track?.Id == 101 && row.Root.ActualWidth > 100));
            var row = feedSidebarRows.First(row => row.Track?.Id == 101);
            if (row.Title.ActualWidth > row.Root.ActualWidth / 2) throw new InvalidOperationException("Short compact title stretches across the row.");
            RouteWaveformPointer(new(row.Title.Bounds.Right + 60, row.Title.Bounds.Y + 8));
            if (row.Title.Opacity != 1) throw new InvalidOperationException("Empty row space triggers the title link hover.");
            var restingTitle = row.Title.Foreground;
            RouteWaveformPointer(new(row.Title.Bounds.X + 10, row.Title.Bounds.Y + 8));
            if (row.Title.Opacity != 1 || row.Title.Foreground != Color.FromArgb((byte)Math.Round(restingTitle.A*.72),restingTitle.R,restingTitle.G,restingTitle.B))
                throw new InvalidOperationException("Actual title lost its foreground hover or allocated an opacity layer.");
            playbackQueue.Clear(); RouteWaveformPointer(new(row.Cover.Bounds.X + 10, row.Cover.Bounds.Y + 10));
            await WaitForLikedLayoutAsync(() => row.Overlay.Strength > .999);
            ClickHomeElement(row.PlaybackButton);
            await WaitForLikedLayoutAsync(() => playbackQueue.Current?.Track.Id == 101);
            if (page.Value != Page.Feed) throw new InvalidOperationException("Compact play button opened the track page.");
            ClickHomeElement(row.Title);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState?.Track.Id == 101);
            libraryLikes = new(transport.Recent, null); ShowLibraryTracks(); likesAsList.Value = false; RefreshLikedViews();
            await WaitForLikedLayoutAsync(() => likedTiles.Values.Any(tile => tile.Track?.Id == 101 && likedGrid.IsAncestorOf(tile.Root) && tile.Root.ActualWidth > 100));
            var tile = likedTiles.Values.First(tile => tile.Track?.Id == 101 && likedGrid.IsAncestorOf(tile.Root));
            playbackQueue.Clear(); RouteWaveformPointer(new(tile.Cover.Bounds.X + 10, tile.Cover.Bounds.Y + 10));
            await WaitForLikedLayoutAsync(() => tile.OverlayOpacity > .999);
            ClickHomeElement(tile.PlaybackButton);
            await WaitForLikedLayoutAsync(() => playbackQueue.Current?.Track.Id == 101);
            if (page.Value != Page.LibraryTracks) throw new InvalidOperationException("Liked tile play button opened the track page.");
            ClickHomeElement(tile.Title);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState?.Track.Id == 101);
            transport.RelatedReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var late = NavigateHomeAsync(force: true);
            await WaitForLikedLayoutAsync(() => homePending.Contains("recommendations"));
            api.Session = new("otherfixture", "fixture-token", "Test"); me = new() { Id = 43 }; ClearLibraryData();
            transport.RelatedReady.SetResult(); await late;
            if (homeContent != null || homeSession != null) throw new InvalidOperationException("Cleared account retained personal Home data.");
            if (waveTaste.Snapshot().Tracks.Length > 0 || waveCandidates.Count > 0 || waveStore.Load(43).Snapshot().Tracks.Length > 0)
                throw new InvalidOperationException("Another account inherited wave preferences or stale candidates.");
            await VerifyWaveJournalAsync();
            Console.WriteLine("HOME_UI_OK: personal shortcut cards, inline responsive drawer with independent scrolling, continuous and interrupted wave color transitions while playing/paused, animated wave, favorite/discovery settings, preserved current/manual queue, dislike, continuous refill, account persistence, fallback without mixes, bounded compact title links, independent sections and loading, responsive grids, scrolling, cache, partial failure/retry, stale account replies and real card/play clicks for Home, compact rows and liked tiles");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("HOME_UI_FAILED: " + error); }
        finally { player = originalPlayer; Window.Close(); }
    }

    private void ClickHomeElement(FrameworkElement element) =>
        RouteWaveformClick(new(element.Bounds.X + element.ActualWidth / 2, element.Bounds.Y + element.ActualHeight / 2));

    private void VerifyWaveDrawerBounds()
    {
        var viewport = homeWaveLayout.Bounds; var panel = waveSettingsPanel.Bounds;
        if (!contentSurface.IsAncestorOf(homeWaveLayout) || viewport != contentSurface.Bounds ||
            Math.Abs(panel.Y - contentSurface.Bounds.Y - HomeWaveLayout.HeaderInset - HomeWaveLayout.DrawerInset) > 1 ||
            Math.Abs(contentSurface.Bounds.Right - panel.Right - HomeWaveLayout.DrawerInset) > 1 ||
            !homeWaveLayout.IsAncestorOf(waveSettingsPanel) || panel.X < viewport.X || panel.Y < viewport.Y ||
            panel.Right > viewport.Right || panel.Bottom > viewport.Bottom || Math.Abs(panel.Width - 360) > 1 ||
            waveSettingsClose.Bounds.Right > panel.Right || waveSettingsClose.Bounds.Y < panel.Y || waveSettingsScroll.ActualHeight <= 0)
            throw new InvalidOperationException($"Wave drawer escaped its content viewport: viewport={viewport}, panel={panel}.");
    }

    private sealed class HomeFixtureTransport : ISoundCloudBrowserTransport
    {
        internal const string Artwork = "https://i1.sndcdn.com/home-fixture-t500x500.jpg";
        internal bool FailMixes;
        internal int HomeReads;
        internal TaskCompletionSource RelatedReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SoundCloudTrack[] Recent { get; } = Enumerable.Range(101, 6).Select(id => Track(id, id - 100,
            new[] { "Тихая гавань", "Там, где свет", "Волны", "После полуночи", "Новый день", "До весны" }[id - 101])).ToArray();
        private static SoundCloudTrack Track(long id, long artist, string title) => new()
        { Id = id, Title = title, Duration = 180000, ArtworkUrl = Artwork, CreatedAt = DateTimeOffset.UtcNow.AddDays(-2).ToString("O"),
          User = new() { Id = artist, Username = "Исполнитель " + artist }, PermalinkUrl = "https://soundcloud.com/fixture/track-" + id };
        private static JsonDocument Tracks(IEnumerable<SoundCloudTrack> tracks) => JsonDocument.Parse("{\"collection\":[" +
            string.Join(',', tracks.Select(track => JsonSerializer.Serialize(track, SoundCloudJson.Default.SoundCloudTrack))) + "]}");
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var path = uri.AbsolutePath;
            if (path is "/me/play-history/contexts" or "/me/play-history/tracks" or "/users/42/likes" or "/me/track_likes/ids" or "/stream" or "/mixed-selections") HomeReads++;
            if (path is "/me/play-history/contexts" or "/me/play-history/tracks" or "/users/42/likes") return Tracks(Recent);
            if (path == "/me/track_likes/ids") return JsonDocument.Parse("[101,102,103,104,105,106]");
            if (path == "/users/42/followings/ids") return JsonDocument.Parse("[7,8,9]");
            if (path == "/stream") return Tracks(Enumerable.Range(301, 6).Select(id => Track(id, 7 + id % 3, "Новый релиз " + (id - 300))));
            if (path == "/mixed-selections")
            {
                if (FailMixes) throw new HttpRequestException("Fixture: mixes unavailable");
                return JsonDocument.Parse("{\"collection\":[{\"items\":{\"collection\":[" + string.Join(',', Enumerable.Range(1, 3).Select(id =>
                    "{\"kind\":\"system-playlist\",\"id\":\"soundcloud:system-playlists:your-moods:42:" + id + "\",\"title\":\"Your Mix " + id + "\",\"artwork_url\":\"" + Artwork + "\",\"track_count\":30}")) + "]}}]}");
            }
            if (path.EndsWith("/related"))
            {
                await RelatedReady.Task.WaitAsync(cancellationToken);
                var seed = long.Parse(path.Split('/')[2]);
                var start = seed < 200 ? 201 : 401 + (int)(seed % 3) * 100;
                return Tracks(Enumerable.Range(start, 18).Select(id => Track(id, 20 + (id - start) / 2, "Открытие " + (id - 200))));
            }
            if (path.StartsWith("/system-playlists/"))
            {
                var urn = Uri.UnescapeDataString(path["/system-playlists/".Length..]);
                var mix = new SoundCloudPlaylist { Urn = urn, Title = "Your Mix " + urn.Split(':')[^1], Tracks = Recent,
                    TrackCount = Recent.Length, ArtworkUrl = Artwork };
                return JsonDocument.Parse(JsonSerializer.Serialize(mix, SoundCloudJson.Default.SoundCloudPlaylist));
            }
            if (path.StartsWith("/tracks/"))
            {
                var id = long.Parse(path.Split('/')[2]);
                var track = Recent.FirstOrDefault(track => track.Id == id) ?? Track(id, 20 + (id - 201) / 2, "Открытие " + (id - 200));
                return JsonDocument.Parse(JsonSerializer.Serialize(track, SoundCloudJson.Default.SoundCloudTrack));
            }
            throw new InvalidOperationException("Unexpected Home fixture request: " + path);
        }
        public Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default) => Task.FromResult(JsonDocument.Parse(
            request.Kind == "sidebar" ? "{\"data\":{\"playlists\":[],\"albums\":[]}}" :
            "{\"data\":{\"trackComments\":{\"comments\":[],\"pageInfo\":{\"hasNextPage\":false}}}}"));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
