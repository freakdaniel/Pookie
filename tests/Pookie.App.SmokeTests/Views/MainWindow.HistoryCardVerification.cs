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
        if (!active.Playback.IsVisible || active.Playback.ShowsPause != isPlaying.Value)
            throw new InvalidOperationException("History did not initialize the current track's active overlay");
        await WaitForLikedLayoutAsync(() => active.Playback.Opacity > .999);
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
            var circle = next.Playback.PlaybackButton ?? throw new InvalidOperationException("History playback action is missing.");
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
        // The click helper emits button events only. Move back from (2,2)
        // first, as a real pointer would, so the revealed play action receives
        // the click instead of the underlying card's open-track navigation.
        point = SettingsCenter(next.Cover);
        RouteWaveformPointer(point);
        await WaitForLikedLayoutAsync(() => next.Playback.Opacity > .999 && next.Playback.PlaybackButton is { IsHitTestVisible: true });
        RouteWaveformClick(SettingsCenter(next.Playback.PlaybackButton!));
        // Playing a history track moves it to the front and can rebind the same card views.
        RouteWaveformPointer(new Point(2, 2));
        TrackArtworkOverlay? PlaybackFor(long id) => collectionCards.Values.FirstOrDefault(card =>
            card.Item.Track?.Id == id && collectionGrid.IsAncestorOf(card.Frame)).Playback;
        await WaitForLikedLayoutAsync(() => current?.Id == fixtures[1].Id && isPlaying.Value && !playbackLoading.Value &&
            PlaybackFor(fixtures[1].Id) is { ShowsPause: true, Opacity: > .999 } && PlaybackFor(active.Item.Track!.Id) is { Opacity: < .001 });
        if (!playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).SequenceEqual(activeCollection!.Items.Where(item => item.Track != null).Select(item => item.Track!.Id)))
            throw new InvalidOperationException("Clicking history did not preserve the history playback queue");
        var playingCard = PlaybackFor(fixtures[1].Id)!;
        RouteWaveformPointer(SettingsCenter(playingCard.PlaybackButton!));
        RouteWaveformClick(SettingsCenter(playingCard.PlaybackButton!));
        await WaitForLikedLayoutAsync(() => !isPlaying.Value && !playingCard.ShowsPause);
        RouteWaveformPointer(new Point(2, 2));
        if (playingCard.Opacity < .999) throw new InvalidOperationException("Paused current history track lost its active overlay");
        CaptureUiPreview("history-paused");
        await ToggleAsync();
        await ShowLibrarySectionAsync(Page.LibraryPlaylists);
        await WaitForLikedLayoutAsync(() => collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Any(card => card.Item.Playlist != null && card.Frame.ActualWidth > 100));
        if (collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Any(card =>
            card.Item.User != null && card.Playback.IsVisible || card.Item.Playlist != null && (!card.Playback.IsVisible || card.Playback.ShowsPause)))
            throw new InvalidOperationException("Recycled history controls leaked state into artist cards or playable playlist cards");
        await ShowLibrarySectionAsync(Page.LibraryHistory);
        await WaitForLikedLayoutAsync(() => collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).Any(card => card.Item.Track?.Id == fixtures[1].Id && card.Frame.ActualWidth > 100));
        var restored = collectionCards.Values.Where(card => collectionGrid.IsAncestorOf(card.Frame)).First(card => card.Item.Track?.Id == fixtures[1].Id && card.Frame.ActualWidth > 100);
        if (!restored.Playback.IsVisible || !restored.Playback.ShowsPause)
            throw new InvalidOperationException($"Returning to history changed its active overlay: current={current?.Id}, expected={fixtures[1].Id}, playing={isPlaying.Value}, paused={paused}, showsPause={restored.Playback.ShowsPause}, opacity={restored.Playback.Opacity}");
        await WaitForLikedLayoutAsync(() => restored.Playback.Opacity > .999);
        Console.WriteLine("UI_HISTORY_CARDS_OK: shared likes playback overlay, current track on entry, animated centered hover, immediate track selection, play/pause, history queue, recycled non-track cards and stable overlay on return");
    }
}
