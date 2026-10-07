using Tmds.DBus;

namespace Pookie.Media.Linux;

// One immutable snapshot is read from the D-Bus thread; commands go back to the host.
internal sealed class MprisMediaSession : IMediaSession, IMprisRoot, IMprisPlayer
{
    private readonly Connection connection;
    private MediaSnapshot? snapshot;
    private bool disposed;
    private event Action<PropertyChanges>? properties;
    private event Action<long>? seeked;
    public ObjectPath ObjectPath => new("/org/mpris/MediaPlayer2");
    public event Action<MediaCommand>? Command;
    internal MprisMediaSession(Connection connection) => this.connection = connection;

    public static async Task<MprisMediaSession> CreateAsync()
    {
        var connection = new Connection(Address.Session);
        var session = new MprisMediaSession(connection);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            await connection.RegisterObjectAsync(session).ConfigureAwait(false);
            await connection.RegisterServiceAsync("org.mpris.MediaPlayer2.Pookie.instance" + Environment.ProcessId).ConfigureAwait(false);
            return session;
        }
        catch { connection.Dispose(); throw; }
    }

    internal static ObjectPath TrackPath(string id) => new("/org/mpris/MediaPlayer2/track/" +
        Convert.ToHexStringLower(System.Text.Encoding.UTF8.GetBytes(id)));
    private MprisPlayerProperties PlayerProperties()
    {
        var state = Volatile.Read(ref snapshot);
        var result = new MprisPlayerProperties();
        if (state == null) return result;
        result.PlaybackStatus = state.Playback == MediaPlayback.Playing ? "Playing" :
            state.Playback == MediaPlayback.Stopped ? "Stopped" : "Paused";
        result.CanPlay = result.CanPause = true;
        result.CanSeek = state.CanSeek;
        result.CanGoNext = state.CanNext; result.CanGoPrevious = state.CanPrevious;
        result.Shuffle = state.Shuffle; result.Volume = state.Volume;
        result.Position = (long)(state.Position * 1_000_000);
        result.Metadata = new Dictionary<string, object>
        {
            ["mpris:trackid"] = TrackPath(state.TrackId), ["mpris:length"] = (long)(state.Duration * 1_000_000),
            ["xesam:title"] = state.Title, ["xesam:artist"] = new[] { state.Artist }
        };
        if (state.ArtworkUrl != null) result.Metadata["mpris:artUrl"] = state.ArtworkUrl;
        if (state.TrackUrl != null) result.Metadata["xesam:url"] = state.TrackUrl;
        return result;
    }
    public void Update(MediaSnapshot state)
    {
        if (disposed) return;
        var previous = Interlocked.Exchange(ref snapshot, state);
        var values = PlayerProperties();
        var changes = new List<KeyValuePair<string, object>>();
        if (previous?.TrackId != state.TrackId || previous.Title != state.Title || previous.Artist != state.Artist ||
            previous.ArtworkUrl != state.ArtworkUrl || previous.TrackUrl != state.TrackUrl || previous.Duration != state.Duration)
            changes.Add(new("Metadata", values.Metadata));
        if (previous?.Playback != state.Playback) changes.Add(new("PlaybackStatus", values.PlaybackStatus));
        if (previous?.Volume != state.Volume) changes.Add(new("Volume", values.Volume));
        if (previous?.Shuffle != state.Shuffle) changes.Add(new("Shuffle", values.Shuffle));
        if (previous?.CanSeek != state.CanSeek) changes.Add(new("CanSeek", values.CanSeek));
        if (previous?.CanNext != state.CanNext) changes.Add(new("CanGoNext", values.CanGoNext));
        if (previous?.CanPrevious != state.CanPrevious) changes.Add(new("CanGoPrevious", values.CanGoPrevious));
        if (previous == null) { changes.Add(new("CanPlay", true)); changes.Add(new("CanPause", true)); }
        if (changes.Count > 0) properties?.Invoke(new(changes.ToArray(), []));
        if (previous?.TrackId == state.TrackId && Math.Abs(state.Position - previous.Position) > 1.5)
            seeked?.Invoke(values.Position);
    }
    public void Clear()
    {
        Interlocked.Exchange(ref snapshot, null);
        properties?.Invoke(new([new("Metadata", new Dictionary<string, object>()), new("PlaybackStatus", "Stopped"),
            new("CanPlay", false), new("CanPause", false), new("CanSeek", false), new("CanGoNext", false), new("CanGoPrevious", false)], []));
    }
    public void Dispose() { if (disposed) return; disposed = true; Clear(); connection.Dispose(); }
    private Task Send(MediaAction action, double value = 0, string? id = null)
    { if (!disposed) Command?.Invoke(new(action, value, id)); return Task.CompletedTask; }
    public Task NextAsync() => Send(MediaAction.Next);
    public Task PreviousAsync() => Send(MediaAction.Previous);
    public Task PauseAsync() => Send(MediaAction.Pause);
    public Task PlayPauseAsync() => Send(MediaAction.Toggle);
    public Task StopAsync() => Send(MediaAction.Stop);
    public Task PlayAsync() => Send(MediaAction.Play);
    public Task RaiseAsync() => Send(MediaAction.Raise);
    public Task QuitAsync() => Task.CompletedTask; // CanQuit is false.
    public Task OpenUriAsync(string uri) => Task.FromException(new DBusException("org.freedesktop.DBus.Error.NotSupported", "OpenUri is unavailable."));
    public Task SeekAsync(long offset) => Send(MediaAction.SeekRelative, offset / 1_000_000d);
    public Task SetPositionAsync(ObjectPath id, long position)
    {
        var state = Volatile.Read(ref snapshot);
        return state != null && TrackPath(state.TrackId) == id && position >= 0
            ? Send(MediaAction.Seek, position / 1_000_000d, state.TrackId) : Task.CompletedTask;
    }
    public Task<object> GetAsync(string property) => Task.FromResult((typeof(MprisPlayerProperties).GetField(property)?.GetValue(PlayerProperties())
        ?? throw new DBusException("org.freedesktop.DBus.Error.UnknownProperty", property)));
    public Task<MprisPlayerProperties> GetAllAsync() => Task.FromResult(PlayerProperties());
    Task<MprisRootProperties> IMprisRoot.GetAllAsync() => Task.FromResult(new MprisRootProperties());
    Task<object> IMprisRoot.GetAsync(string property) => Task.FromResult((typeof(MprisRootProperties).GetField(property)?.GetValue(new MprisRootProperties())
        ?? throw new DBusException("org.freedesktop.DBus.Error.UnknownProperty", property)));
    public Task SetAsync(string property, object value) => property switch
    {
        "Volume" when value is double volume && double.IsFinite(volume) => Send(MediaAction.Volume, Math.Max(0, volume)),
        "Shuffle" when value is bool shuffle => Send(MediaAction.Shuffle, shuffle ? 1 : 0),
        "Rate" when value is double rate && rate == 1 => Task.CompletedTask,
        _ => Task.FromException(new DBusException("org.freedesktop.DBus.Error.PropertyReadOnly", property))
    };
    Task IMprisRoot.SetAsync(string property, object value) => Task.FromException(new DBusException("org.freedesktop.DBus.Error.PropertyReadOnly", property));
    public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler)
    { properties += handler; return Task.FromResult<IDisposable>(new Subscription(() => properties -= handler)); }
    Task<IDisposable> IMprisRoot.WatchPropertiesAsync(Action<PropertyChanges> handler) => Task.FromResult<IDisposable>(new Subscription(() => { }));
    public Task<IDisposable> WatchSeekedAsync(Action<long> handler)
    { seeked += handler; return Task.FromResult<IDisposable>(new Subscription(() => seeked -= handler)); }
    private sealed class Subscription(Action unsubscribe) : IDisposable { public void Dispose() => unsubscribe(); }
}
