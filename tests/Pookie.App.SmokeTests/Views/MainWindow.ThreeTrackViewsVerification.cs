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
        CaptureUiPreview("home-trending-actions");
        SetHomeChartDetail(chart);
        MoveActionPointer(new Point(1, 1));
        Window.FocusManager.ClearFocus();
        await WaitForLikedLayoutAsync(() => row.Trailing.Reveal < .001 && row.Overlay.Opacity < .001);
    }
}
