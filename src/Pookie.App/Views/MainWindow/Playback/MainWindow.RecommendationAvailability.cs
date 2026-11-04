using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<long, DateTimeOffset> unavailableRecommendations = [];

    private bool AllowsRecommendation(SoundCloudTrack track) =>
        unavailableRecommendations.GetValueOrDefault(track.Id) <= DateTimeOffset.UtcNow;

    private bool RememberUnavailableRecommendation(SoundCloudTrack track, Exception error)
    {
        if (error is not SoundCloudException { TrackUnavailable: true, RequiresBrowserVerification: false } failure) return false;
        // Deletion is stable; access restrictions can change much sooner.
        unavailableRecommendations[track.Id] = DateTimeOffset.UtcNow + (failure.StatusCode is 404 or 410 ? TimeSpan.FromDays(30) : TimeSpan.FromHours(1));
        playbackQueue.RemoveUnavailableRecommendation(track.Id);
        RefreshQueue();
        Pookie.Logging.AppLog.For("Pookie.Radio").Information("Недоступный трек {TrackId} исключён из рекомендаций; HTTP {StatusCode}", track.Id, failure.StatusCode);
        return true;
    }

    private async Task ContinueAfterUnavailableRecommendationAsync(long generation, int skipped)
    {
        if (skipped >= 5) { status.Value = "Несколько рекомендованных треков недоступны. Попробуй продолжить позже."; return; }
        var version = playbackQueue.SourceVersion;
        for (var attempt = 0; attempt < 3 && !playbackQueue.Snapshot.Upcoming.Any() && playbackQueue.Snapshot.CanLoadRecommendations; attempt++)
        {
            await queueLoader.EnsureAheadAsync(force: true);
            if (disposed || generation != playGeneration || version != playbackQueue.SourceVersion) return;
        }
        if (disposed || generation != playGeneration || version != playbackQueue.SourceVersion) return;
        var next = playbackQueue.Next();
        if (next != null) await PlayAsync(next.Track, queueAdvance: true, unavailableSkips: skipped + 1);
        else { EndPlayback(); status.Value = "Пока не нашлось доступных треков для продолжения."; }
    }
}
