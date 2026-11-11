using Tmds.DBus;

namespace Pookie.Media.Linux;

[Dictionary]
public sealed class MprisRootProperties
{
    public bool CanQuit = false, CanRaise = true, HasTrackList = false;
    public string Identity = "Pookie";
    public string[] SupportedUriSchemes = [], SupportedMimeTypes = [];
}

[Dictionary]
public sealed class MprisPlayerProperties
{
    public string PlaybackStatus = "Stopped";
    public double Rate = 1, MinimumRate = 1, MaximumRate = 1, Volume = 1;
    public bool Shuffle, CanGoNext, CanGoPrevious, CanPlay, CanPause, CanSeek, CanControl = true;
    public long Position;
    public IDictionary<string, object> Metadata = new Dictionary<string, object>();
}

[DBusInterface("org.mpris.MediaPlayer2")]
public interface IMprisRoot : IDBusObject
{
    Task RaiseAsync();
    Task QuitAsync();
    Task<object> GetAsync(string property);
    Task<MprisRootProperties> GetAllAsync();
    Task SetAsync(string property, object value);
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
}

[DBusInterface("org.mpris.MediaPlayer2.Player")]
public interface IMprisPlayer : IDBusObject
{
    Task NextAsync();
    Task PreviousAsync();
    Task PauseAsync();
    Task PlayPauseAsync();
    Task StopAsync();
    Task PlayAsync();
    Task SeekAsync(long offset);
    Task SetPositionAsync(ObjectPath trackId, long position);
    Task OpenUriAsync(string uri);
    Task<object> GetAsync(string property);
    Task<MprisPlayerProperties> GetAllAsync();
    Task SetAsync(string property, object value);
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
    Task<IDisposable> WatchSeekedAsync(Action<long> handler);
}
