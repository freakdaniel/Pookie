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
        CaptureUiPreview("home-trending-actions");
        MoveActionPointer(new Point(1, 1));
        await WaitForLikedLayoutAsync(() => row.Trailing.Reveal < .001);
    }
}
