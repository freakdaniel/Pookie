using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool CanStartTrackRadio(SoundCloudTrack track) => !demo && me != null && HomeRecommendations.Playable(track);

    private async Task StartTrackRadioAsync(SoundCloudTrack track)
    {
        if (!CanStartTrackRadio(track)) return;
        var key = $"radio:{track.Id}";
        if (playbackQueue.Context?.Key == key && audioReady)
        {
            if (paused) await ToggleAsync();
            return;
        }
        var keepPlaying = current?.Id == track.Id && audioReady && !audioPreparing;
        if (playbackQueue.Repeat != RepeatMode.Off)
        {
            playbackQueue.SetRepeat(RepeatMode.Off); RefreshRepeatState();
            configurationTimer.Stop(); configurationTimer.Start();
        }
        StartQueueContext(new(key, "Радио: " + track.Title, "Похожие треки SoundCloud",
            Kind: PlaybackContextKind.Radio), [track], track, preserveManual: true);
        if (keepPlaying)
        {
            playbackQueue.SetPreparation(playbackQueue.Current!.EntryId, PlaybackPreparation.Ready, current);
            RefreshQueue();
            Run(() => queueLoader.EnsureAheadAsync());
            if (paused) await ToggleAsync();
        }
        else await PlayAsync(track, queueAdvance: true);
    }
}
