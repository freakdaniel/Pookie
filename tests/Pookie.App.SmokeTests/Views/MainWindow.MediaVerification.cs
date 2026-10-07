using Pookie.Media;
#if WINDOWS
using Windows.Media.Control;
#endif

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySystemMediaAsync()
    {
        try
        {
            await PlayAsync(tracks[0]);
            await WaitForLikedLayoutAsync(() => systemMedia != null && audioReady);
#if WINDOWS
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? published = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (published == null)
            {
                foreach (var candidate in manager.GetSessions())
                    if ((await candidate.TryGetMediaPropertiesAsync()).Title == current!.Title) { published = candidate; break; }
                if (published == null) await Task.Delay(100, timeout.Token);
            }
            var first = current!.Id;
            if (!await published.TryPauseAsync()) throw new InvalidOperationException("OS Pause failed.");
            await WaitForLikedLayoutAsync(() => paused && !isPlaying.Value);
            await HandleMediaCommandAsync(new(MediaAction.Pause));
            if (!paused) throw new InvalidOperationException("Repeated Pause toggled back to Play.");
            if (!await published.TryPlayAsync()) throw new InvalidOperationException("OS Play failed.");
            await WaitForLikedLayoutAsync(() => !paused && isPlaying.Value);
            await HandleMediaCommandAsync(new(MediaAction.Play));
            if (paused) throw new InvalidOperationException("Repeated Play toggled back to Pause.");
            if (!await published.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(3).Ticks))
                throw new InvalidOperationException("OS seek failed.");
            await WaitForLikedLayoutAsync(() => !seeking && player!.Poll().Position >= 2.9);
            if (!await published.TrySkipNextAsync()) throw new InvalidOperationException("OS Next failed.");
            await WaitForLikedLayoutAsync(() => current?.Id != first && audioReady);
            await VerifyPublishedTrackAsync();
            SetExpandedPlayer(true);
            ToggleExpandedPanel(PlayerPanel.Queue);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var endedTrack = current!.Id;
                var duration = player!.Poll().Duration;
                await player.SeekAsync(Math.Max(0, duration - .15), lifetime.Token);
                await WaitForLikedLayoutAsync(() => current?.Id != endedTrack && audioReady);
                await VerifyPublishedTrackAsync();
                if (title.Value != current!.Title || artist.Value != current.Author)
                    throw new InvalidOperationException("Auto-advance left stale player metadata.");
            }
            var beforeNext = current!.Id;
            if (!await published.TrySkipNextAsync()) throw new InvalidOperationException("OS Next after auto-advance failed.");
            await WaitForLikedLayoutAsync(() => current?.Id != beforeNext && audioReady);
            await VerifyPublishedTrackAsync();
            SetExpandedPlayer(false);
            if (!await published.TryStopAsync()) throw new InvalidOperationException("OS Stop failed.");
            await WaitForLikedLayoutAsync(() => !audioReady && !audioPreparing && !isPlaying.Value);
            if (!await published.TryPlayAsync()) throw new InvalidOperationException("OS resume from Stop failed.");
            await WaitForLikedLayoutAsync(() => audioReady && isPlaying.Value);

            async Task VerifyPublishedTrackAsync()
            {
                while (true)
                {
                    var metadata = await published.TryGetMediaPropertiesAsync();
                    if (metadata.Title == current!.Title && metadata.Artist == current.Author) return;
                    await Task.Delay(50, timeout.Token);
                }
            }
#else
            await HandleMediaCommandAsync(new(MediaAction.Pause));
            if (!paused) throw new InvalidOperationException("System Pause did not reach the player.");
            await HandleMediaCommandAsync(new(MediaAction.Play));
            if (paused) throw new InvalidOperationException("System Play did not reach the player.");
#endif
            await HandleMediaCommandAsync(new(MediaAction.Volume, .35));
            await HandleMediaCommandAsync(new(MediaAction.Shuffle, 1));
            if (volume.Value != 35 || !shuffle.Value) throw new InvalidOperationException("System Volume/Shuffle failed.");
            await HandleMediaCommandAsync(new(MediaAction.Stop));
            Console.WriteLine("MEDIA_UI_OK: system session controls real audio, auto-advance and OS Next keep metadata/player/fullscreen in sync, idempotent play/pause, seek, stop/resume and shutdown");
        }
        catch (Exception error)
        { Environment.ExitCode = 1; Console.Error.WriteLine("MEDIA_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }
}
