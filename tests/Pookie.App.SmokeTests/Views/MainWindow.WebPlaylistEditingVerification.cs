using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyWebPlaylistEditingUiAsync()
    {
        var fixture = new WebPlaylistEditingFixture(new CollectionFixtureTransport().Playlist);
        api.Session = new("fixture", "web-fixture", "Test");
        api.Transport = fixture; me = new() { Id = 42, Username = "Owner" };
        signedIn.Value = true;
        Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
        await ShowWorkspaceAsync();
        await imageDiskCache.WriteAsync(fixture.Playlist.ArtworkUrl!, ArtworkFixtureBitmap(500), lifetime.Token);
        ClearLibraryData(); ResetFollowing(); ResetReposts();
        var item = LibraryData.FromPlaylist(fixture.Playlist);
        await OpenLibraryItemAsync(item);
        await WaitForLikedLayoutAsync(() => compactTrackRows.Any(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window));
        if (!CanEditDisplayedPlaylist() || !HasCompletePlaylistOrder()) throw new InvalidOperationException("Owned web playlist has no editing actions.");
        var row = compactTrackRows.First(row => row.CollectionRow && row.Track?.Id == 9000 && row.Root.FindVisualRoot() == Window);
        foreach (var id in new[] { "remove", "up", "down" })
            if (!row.Root.ContextMenu!.Items.OfType<MenuItem>().Any(entry => entry.Command?.Id == "pookie.playlist." + id))
                throw new InvalidOperationException("Missing playlist track menu command: " + id);
        OpenPlaylistEditor();
        await WaitForLikedLayoutAsync(() => playlistEditor.IsOpen && !playlistEditor.Running);
        if (playlistEditSave.IsVisible || playlistEditSave.IsEnabled || !playlistEditFocus.IsFocused || !playlistEditor.HasGlassBackdrop || !playlistEditor.UsesCachedTransition)
            throw new InvalidOperationException("Playlist editor lost matte overlay glass, neutral focus or enabled an unchanged save.");
        var cleanHeight = playlistEditFrame.Bounds.Height; var cleanTabs = playlistEditTabs.Bounds;
        var reveals = new HashSet<double>();
        void SampleReveal() => reveals.Add(Math.Round(playlistEditSaveReveal.Opacity, 2));
        Window.FrameRendered += SampleReveal;
        playlistEditName.Text = fixture.Playlist.Title + "!";
        await WaitForLikedLayoutAsync(() => !playlistEditSaveReveal.Animating);
        if (!playlistEditSave.IsVisible || !playlistEditSave.IsEnabled || playlistEditFooter.IsVisible ||
            playlistEditSave.ActualWidth != 44 || playlistEditSave.ActualHeight != 44 ||
            Math.Abs(playlistEditSave.Bounds.Top - playlistEditFrame.Bounds.Top - 24) > 1 ||
            Math.Abs(playlistEditFrame.Bounds.Right - playlistEditSave.Bounds.Right - 24) > 1 ||
            playlistEditTabs.Bounds != cleanTabs || Math.Abs(playlistEditFrame.Bounds.Height - cleanHeight) > 1)
            throw new InvalidOperationException("Dirty editing did not reveal the round header save without moving tabs, resizing or restoring footer buttons.");
        playlistEditName.Text = fixture.Playlist.Title;
        await WaitForLikedLayoutAsync(() => !playlistEditSaveReveal.Animating);
        Window.FrameRendered -= SampleReveal;
        if (playlistEditSave.IsVisible || playlistEditSave.IsEnabled || reveals.Count < 6)
            throw new InvalidOperationException("Reverting all changes retained the header save action or skipped the fade's intermediate frames.");
        if (playlistEditCover.Bounds.Right >= playlistEditName.Bounds.Left || playlistEditCover.ActualWidth != 128)
            throw new InvalidOperationException("Artwork is not beside the title/description fields.");
        var coverPoint = new Point(playlistEditCover.Bounds.X + 20, playlistEditCover.Bounds.Y + 20);
        var coverBefore = OperatingSystem.IsLinux() ? ReadPlaylistEditorPixel(coverPoint) : 0;
        MoveActionPointer(SettingsCenter(playlistEditCover));
        await WaitForLikedLayoutAsync(() => playlistEditArtworkOverlay.Opacity >= .99);
        if (OperatingSystem.IsLinux() && ReadPlaylistEditorPixel(coverPoint) == coverBefore)
            throw new InvalidOperationException("Native artwork pixels stayed unchanged despite the hovered overlay state.");
        CaptureUiPreview("web-playlist-editor-cover-hover");
        MoveActionPointer(new(1, 1));
        await WaitForLikedLayoutAsync(() => playlistEditArtworkOverlay.Opacity <= .01);
        await VerifyPlaylistEditorMotionAsync();
        await VerifyPlaylistEditorSizingAsync();
        SelectPlaylistEditTab(2);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        var typeButton = (Button)playlistEditType.Content!;
        RouteWaveformClick(SettingsCenter(typeButton));
        await WaitForLikedLayoutAsync(() => PopupAppearance.MenuFor(typeButton).Reveal == 1);
        RouteShareKey(Key.Escape);
        if (!playlistEditor.IsOpen || PopupAppearance.MenuFor(typeButton).IsOpen)
            throw new InvalidOperationException("Release type did not use the app popup or Escape dismissed the whole editor.");
        await WaitForLikedLayoutAsync(() => !PopupAppearance.MenuFor(typeButton).IsVisible);
        playlistEditDate.Text = "30.02.2026";
        if (!playlistEditSave.IsVisible || playlistEditSave.IsEnabled || !playlistEditValidation.IsVisible) throw new InvalidOperationException("Invalid release date has no visible validation or actionable header state.");
        playlistEditDate.Text = playlistEditOriginalDate;
        Window.WindowSize = WindowSize.Resizable(1000, 680, minWidth: 1000, minHeight: 680);
        SelectPlaylistEditTab(1);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
        if (playlistEditFooter.IsVisible || playlistEditFrame.Bounds.Top < 0 || playlistEditFrame.Bounds.Bottom > Window.ClientSize.Height)
            throw new InvalidOperationException("Unchanged playlist editing retained an empty footer or escaped the smaller viewport.");
        CaptureUiPreview("web-playlist-editor-small");
        playlistEditDate.Text = "30.02.2026";
        SelectPlaylistEditTab(2);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        var releaseScroll = (ScrollViewer)playlistEditPages.PageContent(2);
        var releaseOverflow = SmoothScroll.Maximum(releaseScroll);
        if (releaseOverflow > 1 || releaseScroll.Bounds.Bottom > playlistEditFooter.Bounds.Top ||
            Math.Abs(playlistEditSave.Bounds.Top - playlistEditFrame.Bounds.Top - 24) > 1)
            throw new InvalidOperationException($"Compact release fields do not fit the small window with validation or covered the footer: overflow={releaseOverflow}, viewport={releaseScroll.Bounds}, footer={playlistEditFooter.Bounds}, save={playlistEditSave.Bounds}, card={playlistEditFrame.Bounds}.");
        await WaitForLoginFrameAsync();
        CaptureUiPreview("web-playlist-editor-release-small");
        playlistEditDate.Text = playlistEditOriginalDate;
        Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
        await WaitForLoginFrameAsync();
        SelectPlaylistEditTab(0);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        var originalIds = fixture.Playlist.Tracks.Select(track => track.Id).ToArray();
        SelectPlaylistEditTab(1);
        await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        EditPlaylistDraftTrack(0, 1); EditPlaylistDraftTrack(2, 0);
        if (!fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(originalIds) || !playlistEditSave.IsEnabled)
            throw new InvalidOperationException("Draft track changes wrote before Save or did not enable it.");
        await WaitForLikedLayoutAsync(() => playlistEditTrackRows.Children.OfType<Grid>().All(row => row.ActualHeight == 56));
        await WaitForLoginFrameAsync();
        CaptureUiPreview("web-playlist-editor-tracks");
        ClosePlaylistEditor(); await WaitForLikedLayoutAsync(() => !playlistEditor.IsVisible);
        OpenPlaylistEditor(); await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        if (!playlistEditTracks.Select(track => track.Id).SequenceEqual(originalIds) || playlistEditTab.Value != 0 || playlistEditSave.IsEnabled)
            throw new InvalidOperationException("Cancel retained a staged reorder/removal or reopened on an outdated tab.");
        playlistEditName.Text = "Обновлённый плейлист 🎵"; playlistEditDescription.Text = "Первая строка\nВторая строка";
        playlistEditGenre.Text = "Alternative Rock"; playlistEditTags.Text = "rock \"indie rock\""; playlistEditDate.Text = "13.11.2026";
        playlistEditPermalink.Text = "updated-playlist";
        playlistEditBuy.Text = "https://example.com/buy"; playlistEditLabel.Text = "Pookie Records";
        playlistEditLicense.Select("cc-by");
        playlistEditPrivate = false; RefreshPlaylistEditor();
        await WaitForLikedLayoutAsync(() => !playlistEditPrivacy.Animating);
        CaptureUiPreview("web-playlist-editor");
        SelectPlaylistEditTab(2); await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        CaptureUiPreview("web-playlist-editor-release");
        SelectPlaylistEditTab(3); await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        CaptureUiPreview("web-playlist-editor-access");
        var accessHeight = playlistEditFrame.Bounds.Height;
        var addressTint = playlistEditPermalinkGlass.Background;
        playlistEditPermalink.Text = "черебара";
        await WaitForLoginFrameAsync();
        var intermediateTint = playlistEditPermalinkGlass.Background;
        await Task.Delay(200, lifetime.Token); await WaitForLoginFrameAsync();
        if (playlistEditSave.IsEnabled || playlistEditValidation.IsVisible || playlistEditFooter.IsVisible ||
            playlistEditPermalinkGlass.Background == addressTint || intermediateTint == playlistEditPermalinkGlass.Background ||
            playlistEditPermalink.Foreground != playlistEditPermalinkGlass.Label.Foreground ||
            playlistEditPermalink.Foreground.R <= playlistEditPermalink.Foreground.G ||
            Math.Abs(playlistEditFrame.Bounds.Height - accessHeight) > 1)
            throw new InvalidOperationException("Invalid address did not smoothly tint its glass or introduced a validation footer/layout jump.");
        CaptureUiPreview("web-playlist-editor-invalid-address");
        playlistEditPermalink.Text = "updated-playlist";
        await WaitForLikedLayoutAsync(() => playlistEditPermalinkGlass.Background == addressTint && playlistEditSave.IsEnabled);
        SelectPlaylistEditTab(0); await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
        fixture.UpdateHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = SavePlaylistEditorAsync();
        if (playlistEditSave.IsEnabled || playlistEditClose.IsEnabled) throw new InvalidOperationException("Pending edit remains actionable.");
        ClosePlaylistEditor();
        if (!playlistEditor.IsOpen) throw new InvalidOperationException("Pending edit was dismissed without completing.");
        fixture.UpdateHold.SetResult(); await save;
        if (collectionTitle.Text != "Обновлённый плейлист 🎵" || displayedCollection?.Playlist?.Sharing != "public" || playlistEditor.IsOpen ||
            !libraryPages["collections"].Items.Any(entry => entry.Title == "Обновлённый плейлист 🎵") ||
            fixture.Playlist is not { Genre: "Alternative Rock", TagList: "rock \"indie rock\"", ReleaseDate: "2026-11-13T00:00:00.000Z", Permalink: "updated-playlist",
                PurchaseUrl: "https://example.com/buy", LabelName: "Pookie Records", License: "cc-by" })
            throw new InvalidOperationException("Confirmed edit did not update the page and library.");
        await EditPlaylistTrackAsync(activeCollection!.Items[0].Track!, 0, 1);
        if (activeCollection.Items[0].Track?.Id != 9001 || fixture.Playlist.Tracks[0].Id != 9001)
            throw new InvalidOperationException("Reordering did not refresh the displayed source order.");
        await EditPlaylistTrackAsync(activeCollection.Items[0].Track!, 0, 0);
        if (activeCollection.Items.Any(entry => entry.Track?.Id == 9001) || displayedCollection?.Playlist?.TrackCount != 11)
            throw new InvalidOperationException("Removed track stayed in the playlist view.");
        OpenPlaylistEditor();
        await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        var beforeDraftSave = fixture.Playlist.Tracks.Select(track => track.Id).ToArray();
        EditPlaylistDraftTrack(0, 1); EditPlaylistDraftTrack(2, 0);
        var expectedDraft = playlistEditTracks.Select(track => track.Id).ToArray();
        if (!fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(beforeDraftSave))
            throw new InvalidOperationException("Staged tracks were written eagerly.");
        await WaitForLoginFrameAsync();
        RouteWaveformClick(SettingsCenter(playlistEditSave));
        await WaitForLikedLayoutAsync(() => !playlistEditor.IsOpen && !pendingPlaylistEdits.Contains((api.Session!, fixture.Playlist.Id)));
        if (!fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(expectedDraft) || fixture.Playlist.TrackCount != 10)
            throw new InvalidOperationException("Save did not commit the exact staged track order/count.");
        fixture.Playlist = fixture.Playlist with { TrackCount = 15 };
        await ReloadEditedPlaylistAsync(item, api.Session!);
        OpenPlaylistEditor();
        if (playlistEditSave.IsEnabled || playlistEditOrderComplete || HasCompletePlaylistOrder())
            throw new InvalidOperationException($"Hidden-track editing state: save={playlistEditSave.IsEnabled}, complete={HasCompletePlaylistOrder()}, count={displayedCollection?.Playlist?.TrackCount}, tracks={displayedCollection?.Playlist?.Tracks.Length}, open={playlistEditor.IsOpen}, canEdit={CanEditDisplayedPlaylist()}.");
        var retained = fixture.Playlist.Tracks.Select(track => track.Id).ToArray();
        playlistEditName.Text = "Плейлист со скрытыми треками";
        if (!playlistEditSave.IsEnabled) throw new InvalidOperationException("Incomplete track snapshot blocked a metadata-only save.");
        await SavePlaylistEditorAsync();
        if (fixture.Playlist.TrackCount != 15 || !fixture.Playlist.Tracks.Select(track => track.Id).SequenceEqual(retained) || collectionTitle.Text != "Плейлист со скрытыми треками")
            throw new InvalidOperationException("Metadata editing changed hidden count or visible order.");
        var refusedPartialOrder = false;
        try { await EditPlaylistTrackAsync(activeCollection.Items[0].Track!, 0, 0); }
        catch (SoundCloudException) { refusedPartialOrder = true; }
        if (!refusedPartialOrder || fixture.Playlist.TrackCount != 15)
            throw new InvalidOperationException("A partial web snapshot was overwritten during removal.");
        OpenPlaylistEditor(deleting: true);
        await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        CaptureUiPreview("web-playlist-delete-confirmation");
        if (!playlistEditor.IsOpen || !playlistEditDeleting || !playlistEditDeleteConfirm.IsEnabled || playlistEditSave.IsVisible || fixture.Deleted)
            throw new InvalidOperationException("Playlist deletion skipped its confirmation.");
        await SavePlaylistEditorAsync();
        if (!fixture.Deleted || page.Value != Page.LibraryPlaylists || libraryPages["collections"].Items.Any(entry => entry.Playlist?.Id == fixture.Playlist.Id))
            throw new InvalidOperationException("Deleted playlist remained in the library.");
        Console.WriteLine("WEB_PLAYLIST_EDIT_UI_OK: matte glass overlay, four sliding tabs, neutral focus, changed-only save, metadata/privacy confirmation, cancelable staged track edits, pending isolation, reorder/remove, hidden-track protection and deletion dialog/library refresh; fixture only");
    }

    private async Task VerifyPlaylistEditorMotionAsync(bool presented = false, nint nativeDisplay = 0)
    {
        var body = (GlassCard)VisualTree.Find(playlistEditFrame, element => element is GlassCard)!;
        foreach (var tab in new[] { 1, 2, 3, 0, 3, 2, 1, 0 })
        {
            var gaps = new List<double>(); var renders = new List<double>();
            var previous = System.Diagnostics.Stopwatch.GetTimestamp();
            var wasMoving = false;
            var edgeErrors = new List<double>(); var distinctHeights = new HashSet<double>();
            var incoming = (UIElement)playlistEditPages.PageContent(tab).Parent!;
            var caption = VisualTree.Find(playlistEditPageContents[tab], element => element is TextBlock { IsVisible: true }) as TextBlock;
            var midFadeFrames = 0;
            var backdrop = playlistEditor.BackdropVersion; var allocations = playlistEditor.OpticalAllocations;
            var reflections = body.ReflectionRasterizations; var texture = body.OpticalImage;
            var lostGlass = false;
            void Frame()
            {
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                var moving = playlistEditPages.Animating || playlistEditFrame.Resizing;
                // Polling for completion can request a frame after an idle wait.
                // Measure continuous motion through its final frame, not that idle gap.
                if (moving || wasMoving)
                {
                    gaps.Add(System.Diagnostics.Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
                    if (!Window.LastFramePerformanceStats.IsEmpty) renders.Add(Window.LastFramePerformanceStats.FrameMs);
                    distinctHeights.Add(playlistEditFrame.Bounds.Height);
                    if (nativeDisplay != 0 && ReadPlaylistEditorEdgeError(nativeDisplay) is { } error) edgeErrors.Add(error);
                    if (nativeDisplay != 0 && incoming.Opacity is > .45 and < .85 && caption is { ActualHeight: > 0 })
                    {
                        if (ReadPlaylistEditorTextPeak(nativeDisplay, caption.Bounds.Inflate(24, 0)) > 70) midFadeFrames++;
                    }
                }
                previous = now; wasMoving = moving;
                lostGlass |= playlistEditPages.ContentOverlaps || !playlistEditor.HasGlassBackdrop ||
                    texture != null && (!ReferenceEquals(texture, body.OpticalImage) || !body.IsOpticsCurrent);
            }
            if (presented) WindowPresentation.FramePresented(Window) += Frame;
            else Window.FrameRendered += Frame;
            try
            {
                SelectPlaylistEditTab(tab);
                await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
                await WaitForLoginFrameAsync();
            }
            finally
            {
                if (presented) WindowPresentation.FramePresented(Window) -= Frame;
                else Window.FrameRendered -= Frame;
            }
            double P95(List<double> values) => values.Count == 0 ? 0 : values.Order().ElementAt(Math.Min(values.Count - 1, (int)(values.Count * .95)));
            var renderTiming = renders.Count == 0 ? "n/a" : P95(renders).ToString("F1") + "ms";
            Console.WriteLine($"PLAYLIST_EDITOR_PERF live={presented} pixels={nativeDisplay != 0} tab={tab} frames={gaps.Count} heights={distinctHeights.Count} gap_p95={P95(gaps):F1}ms max={gaps.Max():F1}ms render_p95={renderTiming} edge_samples={edgeErrors.Count} edge_p95_px={P95(edgeErrors):F1} edge_max_px={edgeErrors.DefaultIfEmpty().Max():F1} mid_fade_text={midFadeFrames} reflections={body.ReflectionRasterizations - reflections} allocations={playlistEditor.OpticalAllocations - allocations} address_reflections={playlistEditPermalinkGlass.ReflectionRasterizations}");
            if (nativeDisplay != 0 && caption != null && midFadeFrames == 0)
                throw new InvalidOperationException("Incoming tab text was absent from the native window until the fade completed.");
            if (nativeDisplay != 0 && (edgeErrors.Count < gaps.Count / 2 || P95(edgeErrors) > 2))
                throw new InvalidOperationException("Native card contour did not follow the measured resize geometry.");
            if (nativeDisplay == 0 && (P95(gaps) > 20 || gaps.Max() > 50 || P95(renders) > 16.7) || lostGlass ||
                playlistEditor.BackdropVersion != backdrop || playlistEditor.OpticalAllocations != allocations || body.ReflectionRasterizations != reflections)
                throw new InvalidOperationException("Playlist tab transition exceeded its frame budget, overlapped pages or rebuilt glass.");
        }
    }

    private async Task VerifyPlaylistEditorSizingAsync()
    {
        var basicHeight = playlistEditFrame.Bounds.Height;
        if (basicHeight >= 440 || playlistEditTrackSummary.IsVisible || playlistEditTrackSummary.Text.Length != 0)
            throw new InvalidOperationException("Basic editing still uses the oversized fixed card or retains redundant track copy.");
        var samples = new HashSet<double>();
        var backdrop = playlistEditor.BackdropVersion; var allocations = playlistEditor.OpticalAllocations;
        void Frame()
        {
            if (!playlistEditFrame.Resizing) return;
            samples.Add(Math.Round(playlistEditFrame.Bounds.Height, 1));
            if (playlistEditPages.ContentOverlaps || Math.Abs(playlistEditFocus.Bounds.Y - playlistEditFrame.Bounds.Y - 24) > .5)
                throw new InvalidOperationException("Resizing duplicated page content or displaced the fixed header inset.");
        }
        Window.FrameRendered += Frame;
        try
        {
            SelectPlaylistEditTab(3);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            if (playlistEditFrame.Bounds.Height >= 320)
                throw new InvalidOperationException("Access did not fit its compact form.");
            SelectPlaylistEditTab(2);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            if (playlistEditFrame.Bounds.Height <= basicHeight + 40)
                throw new InvalidOperationException("Release did not grow to fit the additional settings.");
            SelectPlaylistEditTab(1);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            while (playlistEditTracks.Count > 1) EditPlaylistDraftTrack(playlistEditTracks.Count - 1, 0);
            await WaitForLikedLayoutAsync(() => !playlistEditFrame.Resizing);
            if (playlistEditFrame.Bounds.Height >= 280 || playlistEditTrackSummary.IsVisible || playlistEditFooter.IsVisible ||
                Math.Abs(playlistEditPages.Bounds.Bottom - playlistEditFrame.Bounds.Bottom) > 1)
                throw new InvalidOperationException("One-track draft retained the tall list viewport, redundant summary or empty footer.");
            CaptureUiPreview("web-playlist-editor-one-track");
            SelectPlaylistEditTab(2); await Task.Delay(45, lifetime.Token);
            SelectPlaylistEditTab(3); await Task.Delay(45, lifetime.Token);
            SelectPlaylistEditTab(0);
            await WaitForLikedLayoutAsync(() => !playlistEditPages.Animating && !playlistEditFrame.Resizing);
            if (Math.Abs(playlistEditFrame.Bounds.Height - basicHeight) > 1 || samples.Count < 6 ||
                playlistEditor.BackdropVersion != backdrop || playlistEditor.OpticalAllocations != allocations)
                throw new InvalidOperationException("Interrupted resize skipped its intermediate heights, lost the selected page size or rebuilt glass.");
        }
        finally { Window.FrameRendered -= Frame; }
        ClosePlaylistEditor(); await WaitForLikedLayoutAsync(() => !playlistEditor.IsVisible);
        OpenPlaylistEditor(); await WaitForLikedLayoutAsync(() => !playlistEditor.Running);
        Console.WriteLine("PLAYLIST_EDITOR_SIZE_OK: compact basic/access, measured release, animated/reversible height, one-track shrink, no redundant summary and retained glass");
    }

    private sealed class WebPlaylistEditingFixture : ISoundCloudTransport
    {
        internal SoundCloudPlaylist Playlist;
        internal bool Deleted;
        internal TaskCompletionSource? UpdateHold;
        internal WebPlaylistEditingFixture(SoundCloudPlaylist playlist) => Playlist = playlist with { User = new() { Id = 42, Username = "Owner" }, TrackPagesComplete = true };
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(
            uri.AbsolutePath.StartsWith("/playlists/") ? JsonSerializer.SerializeToDocument(Playlist, SoundCloudJson.Default.SoundCloudPlaylist) :
            JsonSerializer.SerializeToDocument(new { collection = Deleted ? Array.Empty<SoundCloudPlaylist>() : new[] { Playlist } },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<JsonDocument> EditPlaylistAsync(PlaylistEditRequest request, CancellationToken cancellationToken = default)
        {
            if (UpdateHold != null) { await UpdateHold.Task.WaitAsync(cancellationToken); UpdateHold = null; }
            if (request.Update is { } update)
            {
                var metadata = update.Metadata;
                Playlist = Playlist with { Title = update.Title, Description = update.Description, Sharing = update.Sharing,
                    Permalink = metadata?.Permalink ?? Playlist.Permalink, Genre = metadata?.Genre ?? Playlist.Genre, TagList = metadata?.TagList ?? Playlist.TagList,
                    ReleaseDate = metadata?.ReleaseDate is { } date ? date.Length == 0 ? null : date + "T00:00:00.000Z" : Playlist.ReleaseDate,
                    PlaylistType = metadata?.PlaylistType ?? Playlist.PlaylistType, PurchaseUrl = metadata?.PurchaseUrl ?? Playlist.PurchaseUrl,
                    LabelName = metadata?.LabelName ?? Playlist.LabelName, License = metadata?.License ?? Playlist.License,
                    Tracks = update.TrackIds?.Select(id => Playlist.Tracks.First(track => track.Id == id)).ToArray() ?? Playlist.Tracks,
                    TrackCount = update.TrackIds?.Length ?? Playlist.TrackCount };
            }
            if (request.Kind == "remove" && Playlist.Tracks.Length != Playlist.TrackCount) throw new SoundCloudException("SoundCloud не подтвердил полный список треков плейлиста.");
            if (request.Kind == "remove") Playlist = Playlist with { Tracks = Playlist.Tracks.Where(track => track.Id != request.TrackId).ToArray(), TrackCount = Playlist.TrackCount - 1 };
            if (request.Kind == "delete") Deleted = true;
            return JsonSerializer.SerializeToDocument(Playlist, SoundCloudJson.Default.SoundCloudPlaylist);
        }
    }
}
