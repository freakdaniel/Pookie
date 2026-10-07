using System.Globalization;
using Pookie.Audio;
using Pookie.App.Playback;
using Pookie.Logging;
using Pookie.Media;
using Serilog.Events;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly bool enableSystemMedia;
    private IMediaSession? systemMedia;

    private async Task InitializeSystemMediaAsync()
    {
        var dispatcher = Aprillz.MewUI.Application.Current.Dispatcher;
        try
        {
            var session = await MediaSession.CreateAsync(action =>
                { if (!disposed) dispatcher?.BeginInvoke(() => { if (!disposed) action(); }); });
            if (disposed) { session.Dispose(); return; }
            systemMedia = session;
            session.Command += command =>
            {
                if (disposed) return;
                dispatcher?.BeginInvoke(() =>
                { if (!disposed && systemMedia == session) Run(() => HandleMediaCommandAsync(command)); });
            };
            UpdateSystemMedia();
            AppLog.For("Pookie.Media").Debug("Системная медиасессия подключена");
        }
        catch (Exception error)
        { AppLog.Failure("Pookie.Media", "Системная медиасессия недоступна", error, LogEventLevel.Warning); }
    }

    private void UpdateSystemMedia(AudioState? audio = null)
    {
        if (systemMedia == null || disposed) return;
        try
        {
            if (current == null) { systemMedia.Clear(); return; }
            var duration = Math.Max(0, audio?.Duration > 0 ? audio.Duration : current.DurationSeconds);
            var position = Math.Clamp(audio?.Position ?? progress.Value, 0, duration);
            var state = !audioReady && !audioPreparing ? MediaPlayback.Stopped :
                audioPreparing || audio?.Buffering == true ? MediaPlayback.Loading :
                (audio?.Playing ?? !paused) ? MediaPlayback.Playing : MediaPlayback.Paused;
            systemMedia.Update(new(current.Id.ToString(CultureInfo.InvariantCulture), current.Title, current.Author,
                current.ArtworkUrl ?? current.User?.AvatarUrl, current.PermalinkUrl, position, duration, state,
                audioReady && !audioPreparing && duration > 0, playbackQueue.Snapshot.CanNext, playbackQueue.Snapshot.CanPrevious,
                volume.Value / 100, shuffle.Value));
        }
        catch (Exception error)
        {
            AppLog.Failure("Pookie.Media", "Не удалось обновить системную медиасессию", error, LogEventLevel.Warning);
            DisposeSystemMedia();
        }
    }

    private Task HandleMediaCommandAsync(MediaCommand command)
    {
        if (disposed) return Task.CompletedTask;
        if (command.Action == MediaAction.Raise) { Window.Activate(); return Task.CompletedTask; }
        if (!CanUseWorkspace) return Task.CompletedTask;
        switch (command.Action)
        {
            case MediaAction.Play:
                return current == null || !audioReady && !audioPreparing || paused ? ToggleAsync() : Task.CompletedTask;
            case MediaAction.Pause:
                return current != null && !paused && (audioReady || audioPreparing) ? ToggleAsync() : Task.CompletedTask;
            case MediaAction.Toggle: return ToggleAsync();
            case MediaAction.Next: return playbackQueue.Snapshot.CanNext ? SkipAsync(1) : Task.CompletedTask;
            case MediaAction.Previous: return playbackQueue.Snapshot.CanPrevious ? SkipAsync(-1) : Task.CompletedTask;
            case MediaAction.Stop:
                if (playbackQueue.Current is { } entry) playbackQueue.SetPreparation(entry.EntryId, PlaybackPreparation.Stopped);
                bufferedTrack.Reset();
                expandedBuffer.Reset();
                ++playGeneration; playLoading?.Cancel(); CancelSeek(); player?.Stop();
                audioPreparing = audioReady = paused = false;
                isPlaying.Value = playbackLoading.Value = false;
                presence?.Clear(); RefreshLikedPlayback(); UpdateSystemMedia();
                break;
            case MediaAction.Seek:
            case MediaAction.SeekRelative:
                if (!audioReady || audioPreparing || current == null || !double.IsFinite(command.Value) ||
                    command.TrackId != null && command.TrackId != current.Id.ToString(CultureInfo.InvariantCulture)) break;
                QueueSeek(command.Action == MediaAction.SeekRelative ? progress.Value + command.Value : command.Value);
                if (!seekDragging) FlushSeek();
                break;
            case MediaAction.Volume:
                if (double.IsFinite(command.Value)) volume.Value = Math.Clamp(command.Value * 100, 0, 100);
                UpdateSystemMedia();
                break;
            case MediaAction.Shuffle: shuffle.Value = command.Value != 0; UpdateSystemMedia(); break;
        }
        return Task.CompletedTask;
    }

    private void DisposeSystemMedia()
    {
        var session = systemMedia;
        systemMedia = null;
        try { session?.Dispose(); }
        catch (Exception error)
        { AppLog.Failure("Pookie.Media", "Не удалось закрыть системную медиасессию", error, LogEventLevel.Warning); }
    }
}
