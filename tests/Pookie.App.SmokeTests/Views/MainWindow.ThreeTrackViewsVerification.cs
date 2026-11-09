using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyThreeLikedViewsAsync()
    {
        var original = likesMode.Value; var cursor = nextHref; var version = playbackQueue.SourceVersion;
        var controls = new[] { likedGrid, likedList, likedCompactList };
        foreach (var mode in Enum.GetValues<TrackViewMode>())
        {
            likesMode.Value = mode;
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && LikedTrackView.ActualHeight > 100 && !LikedTrackView.IsArrangeDirty);
            if (controls.Count(control => control.IsVisible) != 1 || pageItems[LikedTrackView].DataCount != tracks.Count ||
                !ReferenceEquals(PageContentScroll(), LikedTrackView.FindVisualChild<ScrollViewer>()) || nextHref != cursor || playbackQueue.SourceVersion != version)
                throw new InvalidOperationException("Library view switching lost its source, viewport, pagination cursor or queue.");
            CaptureUiPreview("likes-view-" + mode.ToString().ToLowerInvariant());
        }
        likesMode.Value = original;
        Console.WriteLine("THREE_LIKES_VIEWS_OK: square cards, waveform cards and compact list share loaded likes, pagination, active scroll routing and queue");
    }

    private async Task VerifyTrendingActionsAsync(CompactTrackRow row)
    {
        Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
        MoveActionPointer(new(row.Cover.Bounds.X + 4, row.Cover.Bounds.Y + 4));
        await WaitForLikedLayoutAsync(() => row.Trailing.Reveal > .999);
        if (row.Root.ActualWidth >= CompactTrackTail.FullActionsThreshold &&
            (row.ExtraActions is not { IsVisible: true } || row.ActionButtons.Length != 4 || row.Repost is not { ActualWidth: > 20 }))
            throw new InvalidOperationException("Trending row collapsed its actions despite having room for the complete strip.");
        MoveActionPointer(row.Cover.Bounds.Center);
        await WaitForLikedLayoutAsync(() => row.Overlay.Opacity > .999 && row.PlaybackButton.IsHitTestVisible);
        if (!row.PlaybackButton.Focus()) throw new InvalidOperationException("The visible trends play button cannot take focus.");
        var chart = homeChartDetail!;
        var refreshed = chart.Tracks.Tracks.ToArray();
        refreshed[0] = refreshed[0] with { PlaybackCount = (refreshed[0].PlaybackCount ?? 0) + 1 };
        SetHomeChartDetail(chart with { Tracks = chart.Tracks with { Tracks = refreshed } });
        await WaitForLoginFrameAsync();
        if (!ReferenceEquals(homeChartRows[0], row) || !row.PlaybackButton.IsFocused || !row.Root.IsMouseOver ||
            row.Overlay.Opacity < .99 || row.Track?.PlaybackCount != refreshed[0].PlaybackCount)
            throw new InvalidOperationException("Refreshing a trends track lost its play button or keyboard focus.");
        var cover = (ImageSource)row.Cover.Source!;
        var refreshedCover = ImageSource.FromBgraPixels(1, 1, [48, 60, 180, 255]);
        SetCardArtwork(row.Cover, refreshedCover, refreshed[0].ArtworkUrl, finished: true);
        await WaitForLoginFrameAsync();
        if (row.Overlay.Opacity < .99 || !row.PlaybackButton.IsFocused)
            throw new InvalidOperationException("Replacing the trends cover hid the focused play button.");
        SetCardArtwork(row.Cover, cover, refreshed[0].ArtworkUrl, finished: true);
        VerifyCompactCoverDimming(row);
        if (OperatingSystem.IsWindows()) await VerifyTrendingActionPixelsAsync(row);
        CaptureUiPreview("home-trending-actions");
        SetHomeChartDetail(chart);
        MoveActionPointer(new Point(1, 1));
        Window.FocusManager.ClearFocus();
        await WaitForLikedLayoutAsync(() => row.Trailing.Reveal < .001 && row.Overlay.Opacity < .001);
    }

    private async Task VerifyTrendingActionPixelsAsync(CompactTrackRow row)
    {
        MoveActionPointer(new Point(1, 1));
        Window.FocusManager.ClearFocus();
        await WaitForLikedLayoutAsync(() => !row.Trailing.Animating && row.Trailing.Reveal == 0);
        var movingFrames = 0;
        var blankFrames = 0;
        void SampleMotion()
        {
            if (row.Trailing.Reveal is <= .12 or >= .88) return;
            movingFrames++;
            if (!ReadTrendingGlyph(row.Heart)) blankFrames++;
        }

        WindowPresentation.FramePresented(Window) += SampleMotion;
        try
        {
            MoveActionPointer(row.Cover.Bounds.Center);
            await WaitForLikedLayoutAsync(() => !row.Trailing.Animating && row.Trailing.Reveal == 1);
        }
        finally { WindowPresentation.FramePresented(Window) -= SampleMotion; }
        if (movingFrames == 0 || blankFrames != 0)
            throw new InvalidOperationException($"Trending heart was missing during action motion in {blankFrames} of {movingFrames} presented frames.");

        var offset = shellScroll.VerticalOffset;
        try
        {
            // Keep the pointer over the row and exercise partial paints at
            // different physical pixel alignments of the same moving strip.
            foreach (var step in new[] { 0, 6, 19, 12, 0 })
            {
                shellScroll.SetScrollOffsets(0, offset + step);
                await WaitForLoginFrameAsync();
                if (!row.Root.IsMouseOver || row.Actions.Strength < .99)
                    throw new InvalidOperationException("Scrolling inside a hovered trends row interrupted its actions.");
                if (!ReadTrendingGlyph(row.Heart) || row.Repost is { IsVisible: true } && row.ExtraActions!.IsVisible && !ReadTrendingGlyph(row.RepostIcon!))
                    throw new InvalidOperationException($"A trends action disappeared from the presented frame at scroll step {step}.");
            }
        }
        finally { shellScroll.SetScrollOffsets(0, offset); }
        Console.WriteLine("TRENDING_ACTION_PIXELS_OK: native frames keep glyphs through hover animation and partial scroll paints");
    }

    private async Task VerifyLiveTrendingActionPixelsAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || !ProbeSetWindowPos(Window.Handle, -1, 0, 0, 0, 0, 0x53))
            throw new InvalidOperationException("Could not expose the native trends window.");
        Window.WindowSize = WindowSize.Resizable(1400, 980, minWidth: 1000, minHeight: 680);
        await NavigateHomeAsync().WaitAsync(token);
        await WaitForTrackPageLiveAsync(() => homeChartRows.Count == 10 && !homeChartLeftLoading.Loading);
        await ShowTrendsInViewportAsync();
        MoveActionPointer(new Point(1, 1));
        await WaitForLoginFrameAsync();
        var blank = homeChartRows.Select((row, index) => (row, index))
            .Where(entry => !ReadTrendingGlyph(entry.row.Heart)).Select(entry => entry.index).ToArray();
        if (blank.Length != 0)
            throw new InvalidOperationException($"Live trends did not paint idle hearts on rows {string.Join(", ", blank)}.");
        foreach (var row in new[] { homeChartRows[0], homeChartRows[5] })
            await VerifyTrendingActionPixelsAsync(row);
        Console.WriteLine("LIVE_TRENDS_ACTION_PIXELS_OK");
    }

    private bool ReadTrendingGlyph(Image glyph)
    {
        if (ProbeDwmFlush() < 0) throw new InvalidOperationException("Could not sample presented trends actions.");
        var dc = ProbeGetDC(0);
        try
        {
            var center = glyph.TranslatePoint(new Point(glyph.ActualWidth / 2, glyph.ActualHeight / 2), Window);
            var screen = Window.ClientToScreen(center);
            for (var y = -6; y <= 6; y++)
            for (var x = -7; x <= 7; x++)
            {
                var pixel = ProbeGetPixel(dc, (int)Math.Round(screen.X) + x, (int)Math.Round(screen.Y) + y);
                if (pixel != uint.MaxValue && Math.Max(pixel & 255, Math.Max((pixel >> 8) & 255, (pixel >> 16) & 255)) > 80)
                    return true;
            }
            return false;
        }
        finally { _ = ProbeReleaseDC(0, dc); }
    }
}
