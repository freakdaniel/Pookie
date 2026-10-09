using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyTrackSectionsUiAsync()
    {
        if (trackDetailCommentsTitle.Text != "273 комментария" || trackDetailState?.Sidebar is not { HasMorePlaylists: true })
            throw new InvalidOperationException("Comment declension or playlist continuation is missing.");
        var labels = new List<string>();
        VisualTree.Visit(trackDetailOverview, element => { if (element is TextBlock text) labels.Add(text.Text); });
        if (!labels.Contains("Фанаты") || !labels.Contains("210 воспроизведений") || !labels.Contains("Прослушиваний"))
            throw new InvalidOperationException("Sidebar labels do not match the requested wording.");
        var fan = (Button)trackDetailFans.Children[0];
        var fanContent = (Grid)fan.Content!;
        if (Math.Abs(fanContent.Children[0].Bounds.X - trackDetailFans.Bounds.X) > 1)
            throw new InvalidOperationException("Fan artwork is indented relative to its heading.");
        if (Math.Abs(fan.Bounds.X - fanContent.Children[0].Bounds.X) > 1)
            throw new InvalidOperationException("Fan background extends beyond its avatar.");
        if (Math.Abs(fan.Bounds.Right - fanContent.Children[2].Bounds.Right - 16) > 1)
            throw new InvalidOperationException("Fan playback count lost its right padding.");
        VisualTree.Visit(trackDetailComments, element =>
        {
            if (element is TextBlock { Text: "Этот момент особенно хорош 🎧" } body && body.FontWeight != FontWeight.Normal)
                throw new InvalidOperationException("Comment body inherited a medium font weight.");
        });
        var related = trackDetailRelatedRows.First(row => row.Root.IsVisible);
        if (Math.Abs(related.Cover.Bounds.X - trackDetailFans.Bounds.X) > 1 ||
            related.Duration.Bounds.X - related.Like.Bounds.Right > 10)
            throw new InvalidOperationException("Related artwork alignment or like/duration spacing is incorrect.");
        Window.FocusManager.ClearFocus();
        MoveActionPointer(new Point(1, 1));
        timer.Stop();
        var enabled = trackDetailLike.IsEnabled; var style = trackDetailLike.StyleName;
        trackDetailLike.IsEnabled = true; trackDetailLike.StyleName = TrackButtons.Glass;
        MoveActionPointer(new Point(trackDetailLike.Bounds.X + 10, trackDetailLike.Bounds.Y + 10));
        await WaitForLikedLayoutAsync(() => trackDetailLike.IsMouseOver && trackDetailLike.Background == Color.FromArgb(56, 255, 255, 255));
        MoveActionPointer(new Point(1, 1)); trackDetailLike.Focus();
        await WaitForLikedLayoutAsync(() => trackDetailLike.IsFocused && trackDetailLike.Background == TrackButtons.GlassSurface);
        TrackButtons.SetLiked(trackDetailLike, true, TrackButtons.Glass);
        await WaitForLikedLayoutAsync(() => trackDetailLike.Background == TrackButtons.SelectedSurface);
        Window.FocusManager.ClearFocus(); trackDetailLike.StyleName = style; trackDetailLike.IsEnabled = enabled;
        timer.Start();
        // Resize every displayed frame in both directions, rather than checking only
        // the settled endpoint after a delay.
        foreach (var width in new[] { 1040, 1120, 1240, 1360, 1440, 1320, 1200, 1080, 1000 })
        {
            Window.WindowSize = WindowSize.Resizable(width, 900, minWidth: 1000, minHeight: 680);
            await WaitForLoginFrameAsync();
            await WaitForLikedLayoutAsync(() => Math.Abs(contentSurface.ActualWidth - Window.ClientSize.Width) < 2 &&
                Math.Abs(contentFrame.ActualWidth - Math.Min(1440, Window.ClientSize.Width)) < 2);
            if (trackDetailFans.Bounds.Right > Window.ClientSize.Width - 30 ||
                trackDetailCover.Bounds.Right > Window.ClientSize.Width - 30 ||
                Math.Abs(trackDetailFans.Bounds.X - trackDetailCover.Bounds.X) > 1)
                throw new InvalidOperationException($"Sidebar moved outside its column during resize to {width}: {trackDetailFans.Bounds}.");
        }
        CaptureUiPreview("track-sections-overview");
        await OpenTrackSectionAsync(TrackSection.Playlists);
        await WaitForLikedLayoutAsync(() => trackDetailSectionCards.Children.Count >= 3 && trackDetailSectionCards.ActualHeight > 100);
        if (trackDetailOverview.IsVisible || !trackDetailSectionContent.IsVisible)
            throw new InvalidOperationException("Track tabs do not switch the body below the hero.");
        var generation = navigationGeneration;
        await OpenTrackSectionAsync(TrackSection.Playlists);
        if (generation != navigationGeneration) throw new InvalidOperationException("The active tab reloaded itself.");
        trackDetailScroll.SetScrollOffsets(0, SmoothScroll.Maximum(trackDetailScroll));
        await WaitForLikedLayoutAsync(() => trackDetailState is { SectionLoading: false } state && state.Sections[TrackSection.Playlists].Items.Length == 4);
        CaptureUiPreview("track-sections-playlists");
        await OpenTrackSectionAsync(TrackSection.Reposts);
        if (trackDetailState!.Sections[TrackSection.Reposts].Items.Single().User?.Username != "Репостер")
            throw new InvalidOperationException("Reposts do not display the reposting users.");
        await WaitForLikedLayoutAsync(() => ((FrameworkElement)trackDetailSectionCards.Children[0]).ActualHeight > 100);
        var profile = (Button)trackDetailSectionCards.Children[0];
        var profileName = FindTrackDetailText(profile, "Репостер");
        if (profileName.TextAlignment != TextAlignment.Center || profile.Background.A != 0)
            throw new InvalidOperationException("Reposts did not reuse the library profile card style.");
        CaptureUiPreview("track-sections-reposts");
        await MoveNavigationAsync(-1);
        if (trackDetailSection.Value != TrackSection.Playlists || trackDetailState!.Sections[TrackSection.Playlists].Items.Length != 4)
            throw new InvalidOperationException("Back lost the selected tab or its appended playlists.");
        await MoveNavigationAsync(1);
        if (trackDetailSection.Value != TrackSection.Reposts) throw new InvalidOperationException("Forward lost the track subsection.");
        await OpenTrackSectionAsync(TrackSection.Albums);
        if (!trackDetailState!.Sections[TrackSection.Albums].Items.Single().IsAlbum)
            throw new InvalidOperationException("Albums were mixed with ordinary playlists.");
        await OpenTrackSectionAsync(TrackSection.Related);
        await WaitForLikedLayoutAsync(() => trackSectionRows.Count == 1 && trackSectionRows[0].Root.ActualHeight == TrackRowLayout.Height && trackSectionRows[0].Waveform.ActualHeight == 68 && trackSectionRows[0].Cover.ActualWidth == 160);
        CaptureUiPreview("track-sections-related");
        await OpenTrackSectionAsync(TrackSection.Overview);
        await WaitForLikedLayoutAsync(() => trackDetailPlaylists.ActualHeight > 200 &&
            SmoothScroll.Maximum(trackDetailScroll) > 400);
        trackDetailScroll.SetScrollOffsets(0, SmoothScroll.Maximum(trackDetailScroll));
        await WaitForLikedLayoutAsync(() => trackDetailScroll.VerticalOffset > 200);
        await WaitForLoginFrameAsync(); CaptureUiPreview("track-sections-sidebar");
        var tiles = trackDetailPlaylists.Children;
        if (tiles.Count != 4) throw new InvalidOperationException("The overview playlist preview is incomplete.");
        for (var i = 0; i < tiles.Count; i++)
        {
            if (Math.Abs(tiles[i].Bounds.X - tiles[i % 2].Bounds.X) > 1 ||
                Math.Abs(tiles[i].Bounds.Y - tiles[i / 2 * 2].Bounds.Y) > 1 ||
                i >= 2 && tiles[i].Bounds.Y <= tiles[i - 2].Bounds.Bottom)
                throw new InvalidOperationException("The sidebar playlist tiles do not form a 2x2 grid.");
            if (tiles[i] is not Button { Background.A: 0 })
                throw new InvalidOperationException("Playlist cards introduced a background highlight.");
        }
        Console.WriteLine("TRACK_SECTIONS_UI_OK: live resize, labels, shared library rows and compact alignment, focus reset, 2x2 playlist preview, subsection data, append/dedup and back/forward");
    }

    private static TextBlock FindTrackDetailText(FrameworkElement root, string value)
    {
        TextBlock? result = null;
        VisualTree.Visit(root, element => { if (element is TextBlock text && text.Text == value) result = text; });
        return result ?? throw new InvalidOperationException("Missing track detail text: " + value);
    }
}
