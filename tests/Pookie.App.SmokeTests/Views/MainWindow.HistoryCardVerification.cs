using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyHistoryTrackCardsAsync(SoundCloudTrack[] fixtures)
    {
        await WaitForLikedLayoutAsync(() => collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Count(card => card.Item.Track != null && card.Cover.ActualWidth > 100) >= 6);
        var active = collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Single(card => card.Item.Track?.Id == current?.Id && card.Cover.ActualWidth > 100);
        if (!active.Playback.IsVisible || active.Playback.Opacity < .999 || active.Playback.ShowsPause != isPlaying.Value)
            throw new InvalidOperationException("History did not initialize the current track's active overlay");
        var next = collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Single(card => card.Item.Track?.Id == fixtures[1].Id && card.Cover.ActualWidth > 100);
        if (next.Playback.Opacity > .001 || next.Playback.ShowsPause)
            throw new InvalidOperationException("History initialized an idle card as active");
        var hoverFrames = new List<double>();
        void SampleHover() => hoverFrames.Add(next.Playback.Opacity);
        Window.FrameRendered += SampleHover;
        var point = new Point(next.Cover.Bounds.X + next.Cover.Bounds.Width / 2, next.Cover.Bounds.Y + next.Cover.Bounds.Height / 2);
        try
        {
            RouteWaveformPointer(point);
            await WaitForLikedLayoutAsync(() => next.Playback.Opacity > .999);
            if (!hoverFrames.Any(opacity => opacity is > .01 and < .99))
                throw new InvalidOperationException("History hover skipped its fade-in animation");
            var circle = (Border)next.Playback.Content!;
            if (Math.Abs(circle.Bounds.X + circle.Bounds.Width / 2 - point.X) > 1 ||
                Math.Abs(circle.Bounds.Y + circle.Bounds.Height / 2 - point.Y) > 1)
                throw new InvalidOperationException("History playback button is not centered on the cover");
            CaptureUiPreview("history-hover");
            RouteWaveformPointer(new Point(2, 2));
            await WaitForLikedLayoutAsync(() => next.Playback.Opacity < .001);
            if (active.Playback.Opacity < .999)
                throw new InvalidOperationException("Leaving an idle history card hid the active track overlay");
        }
        finally { Window.FrameRendered -= SampleHover; }
        RouteWaveformClick(point);
        await WaitForLikedLayoutAsync(() => current?.Id == fixtures[1].Id && isPlaying.Value && !playbackLoading.Value &&
            next.Playback.ShowsPause && next.Playback.Opacity > .999 && active.Playback.Opacity < .001);
        if (!queueTracks.Select(track => track.Id).SequenceEqual(activeCollection!.Items.Where(item => item.Track != null).Select(item => item.Track!.Id)))
            throw new InvalidOperationException("Clicking history did not preserve the history playback queue");
        RouteWaveformClick(point);
        await WaitForLikedLayoutAsync(() => !isPlaying.Value && !next.Playback.ShowsPause);
        RouteWaveformPointer(new Point(2, 2));
        if (next.Playback.Opacity < .999) throw new InvalidOperationException("Paused current history track lost its active overlay");
        CaptureUiPreview("history-paused");
        await ToggleAsync();
        await ShowLibrarySectionAsync(Page.LibraryPlaylists);
        await WaitForLikedLayoutAsync(() => collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Any(card => card.Item.Playlist != null && card.Frame.ActualWidth > 100));
        if (collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Where(card => card.Item.Track == null).Any(card => card.Playback.IsVisible))
            throw new InvalidOperationException("Recycled history controls leaked playback buttons into playlist or artist cards");
        await ShowLibrarySectionAsync(Page.LibraryHistory);
        await WaitForLikedLayoutAsync(() => collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Any(card => card.Item.Track?.Id == fixtures[1].Id && card.Frame.ActualWidth > 100));
        var restored = collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).First(card => card.Item.Track?.Id == fixtures[1].Id && card.Frame.ActualWidth > 100);
        if (!restored.Playback.ShowsPause || restored.Playback.Opacity < .999)
            throw new InvalidOperationException("Returning to history restarted the current card's overlay fade");
        Console.WriteLine("UI_HISTORY_CARDS_OK: shared likes playback overlay, current track on entry, animated centered hover, immediate track selection, play/pause, history queue, recycled non-track cards and stable overlay on return");
    }
}
