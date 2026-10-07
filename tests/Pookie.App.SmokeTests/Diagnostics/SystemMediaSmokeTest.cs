using Pookie.Media;
using Pookie.Media.Linux;
using Tmds.DBus;
#if WINDOWS
using Windows.Media.Control;
#endif

namespace Pookie.App.Diagnostics;

internal static class SystemMediaSmokeTest
{
    private static readonly MediaSnapshot track = new("42", "Pookie media fixture", "Test artist", null,
        "https://soundcloud.com/pookie/fixture", 12, 180, MediaPlayback.Playing, true, true, true, .7, false);

    public static async Task RunAsync()
    {
        await CheckMprisAsync();
#if WINDOWS
        using var session = await MediaSession.CreateAsync(action => action());
        var commands = new System.Collections.Concurrent.ConcurrentQueue<MediaCommand>();
        session.Command += commands.Enqueue;
        session.Update(track);
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        GlobalSystemMediaTransportControlsSession? published = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!timeout.IsCancellationRequested)
        {
            foreach (var candidate in manager.GetSessions())
                if ((await candidate.TryGetMediaPropertiesAsync()).Title == track.Title) { published = candidate; break; }
            if (published != null) break;
            await Task.Delay(100, timeout.Token);
        }
        if (published == null) throw new InvalidOperationException("Windows did not discover the Pookie session.");
        var metadata = await published.TryGetMediaPropertiesAsync();
        if (metadata.Title != track.Title || metadata.Artist != track.Artist ||
            published.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            throw new InvalidOperationException("SMTC metadata or playback status disagrees with the player.");
        if (!await published.TryPauseAsync()) throw new InvalidOperationException("Windows refused Pause.");
        while (!commands.Any(command => command.Action == MediaAction.Pause)) await Task.Delay(20, timeout.Token);
        session.Update(track with { Playback = MediaPlayback.Paused, Position = 30 });
        if (!await published.TryPlayAsync()) throw new InvalidOperationException("Windows refused Play.");
        while (!commands.Any(command => command.Action == MediaAction.Play)) await Task.Delay(20, timeout.Token);
        session.Clear();
        while (published.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
            await Task.Delay(20, timeout.Token);
        Console.WriteLine("WINDOWS_MEDIA_OK: OS discovery, title/artist, playback status, pause/play round trip and clear");
#endif
    }

    private static async Task CheckMprisAsync()
    {
        var options = new ServerConnectionOptions();
        using var server = new Connection(options);
        using var session = new MprisMediaSession(server);
        await server.RegisterObjectAsync(session);
        var address = await options.StartAsync("tcp:host=127.0.0.1,port=0");
        using var client = new Connection(address);
        await client.ConnectAsync();
        var player = client.CreateProxy<IMprisPlayer>("fixture", new ObjectPath("/org/mpris/MediaPlayer2"));
        var root = client.CreateProxy<IMprisRoot>("fixture", new ObjectPath("/org/mpris/MediaPlayer2"));
        var commands = new System.Collections.Concurrent.ConcurrentQueue<MediaCommand>();
        session.Command += commands.Enqueue;
        session.Update(track);
        if ((await root.GetAllAsync()).Identity != "Pookie") throw new InvalidOperationException("MPRIS identity missing.");
        var state = await player.GetAllAsync();
        if (state.PlaybackStatus != "Playing" || state.Position != 12_000_000 ||
            (string)state.Metadata["xesam:title"] != track.Title || (long)state.Metadata["mpris:length"] != 180_000_000)
            throw new InvalidOperationException("MPRIS properties or metadata are invalid.");
        var changes = new TaskCompletionSource<PropertyChanges>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = await player.WatchPropertiesAsync(value => changes.TrySetResult(value));
        session.Update(track with { Playback = MediaPlayback.Paused });
        var changed = await changes.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (changed.Get<string>("PlaybackStatus") != "Paused") throw new InvalidOperationException("MPRIS signal missing.");
        await player.PauseAsync(); await player.NextAsync();
        await player.SetPositionAsync(MprisMediaSession.TrackPath("stale"), 50_000_000);
        await player.SetPositionAsync(MprisMediaSession.TrackPath("42"), 30_000_000);
        await player.SetAsync("Volume", .5);
        if (!commands.Any(value => value.Action == MediaAction.Pause) || !commands.Any(value => value.Action == MediaAction.Next) ||
            commands.Count(value => value.Action == MediaAction.Seek) != 1 ||
            commands.Single(value => value.Action == MediaAction.Seek) is not { Value: 30, TrackId: "42" } ||
            !commands.Any(value => value is { Action: MediaAction.Volume, Value: .5 }))
            throw new InvalidOperationException("MPRIS command routing or stale-track rejection failed.");
        session.Clear();
        state = await player.GetAllAsync();
        if (state.PlaybackStatus != "Stopped" || state.Metadata.Count != 0 || state.CanPlay || state.CanSeek)
            throw new InvalidOperationException("MPRIS clear left stale media.");
        Console.WriteLine("MPRIS_MEDIA_OK: real D-Bus serialization, metadata, properties/signals, commands, stale seek rejection and clear");
    }
}
