using Pookie.App.Playback;
using Pookie.Logging;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool playbackHistorySubmitted;
    private string? playbackHistoryContext;
    private WebSession? playbackHistorySession;

    private static string? PlaybackHistoryContextUrn(LibraryItem? item)
    {
        var urn = item?.Playlist is { } playlist ? string.IsNullOrEmpty(playlist.Urn) ? $"soundcloud:playlists:{playlist.Id}" : playlist.Urn :
            item?.User is { Id: > 0 } user ? $"soundcloud:users:{user.Id}" : null;
        return BrowserRequestCommand.IsHistoryContextUrn(urn) ? urn : null;
    }

    private void BeginPlaybackHistory(PlaybackEntry entry)
    {
        playbackHistorySubmitted = demo;
        playbackHistorySession = api.Session;
        playbackHistoryContext = entry.Origin == PlaybackEntryOrigin.Context ? playbackQueue.Context?.ResourceUrn : null;
    }

    private void PollPlaybackHistory()
    {
        // Match the website's minimum play time using actual listened seconds.
        // WaveListening already excludes pause, buffering and seek position jumps.
        if (playbackHistorySubmitted || waveListening.Seconds < 1 || waveListening.Track is not { } track ||
            api.Session is not { } session || session != playbackHistorySession) return;
        playbackHistorySubmitted = true;
        var context = playbackHistoryContext;
        Run(() => RecordPlaybackHistoryAsync(track, context, session));
    }

    private async Task RecordPlaybackHistoryAsync(SoundCloudTrack track, string? context, WebSession session)
    {
        try
        {
            await api.RecordPlaybackAsync(track.Id, context, lifetime.Token);
            if (disposed || api.Session != session) return;
            libraryLoadedAt.Remove("recent"); libraryLoadedAt.Remove("history");
            homeLoadedAt = default;
            AppLog.For("Pookie.History").Debug("Событие прослушивания трека {TrackId} отправлено в SoundCloud", track.Id);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.For("Pookie.History").Warning("Не удалось записать прослушивание трека {TrackId}: {ErrorType}; HTTP {StatusCode}",
                track.Id, error.GetType().Name, (error as SoundCloudException)?.StatusCode);
            if (!disposed && api.Session == session)
                status.Value = "Не удалось обновить историю SoundCloud. Прослушивание сохранено в Pookie.";
        }
    }
}
