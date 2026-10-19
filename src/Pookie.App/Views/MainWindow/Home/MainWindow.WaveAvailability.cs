using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool RememberUnavailableWaveTrack(SoundCloudTrack track, Exception error)
    {
        if (error is not SoundCloudException { TrackUnavailable: true, RequiresBrowserVerification: false } failure) return false;
        EnsureWaveTaste();
        // Deletion is stable; access restrictions can change much sooner.
        waveTaste.MarkUnavailable(track, DateTimeOffset.UtcNow + (failure.StatusCode is 404 or 410 ? TimeSpan.FromDays(30) : TimeSpan.FromHours(1)));
        waveCandidates.Remove(track.Id);
        playbackQueue.RemoveUnavailableRecommendation(track.Id);
        ScheduleWaveSave(); RerankWaveQueue(); RefreshQueue();
        Pookie.Logging.AppLog.For("Pookie.Wave").Information("Недоступный трек {TrackId} исключён из рекомендаций; HTTP {StatusCode}", track.Id, failure.StatusCode);
        return true;
    }

    private async Task ContinueAfterUnavailableWaveTrackAsync(long generation, int skipped)
    {
        if (skipped >= 5) { status.Value = "Несколько рекомендованных треков недоступны. Попробуй продолжить позже."; return; }
        var version = playbackQueue.SourceVersion;
        if (!playbackQueue.Snapshot.Upcoming.Any())
            await queueLoader.EnsureAheadAsync(force: true);
        if (disposed || generation != playGeneration || version != playbackQueue.SourceVersion) return;
        var next = playbackQueue.Next();
        if (next != null) await PlayAsync(next.Track, queueAdvance: true, unavailableSkips: skipped + 1);
        else { EndPlayback(); status.Value = "Пока не нашлось доступных треков для продолжения."; }
    }
}
