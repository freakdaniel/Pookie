namespace Pookie.Media;

public enum MediaPlayback { Stopped, Loading, Playing, Paused }
public enum MediaAction { Play, Pause, Toggle, Stop, Next, Previous, Seek, SeekRelative, Volume, Shuffle, Raise }
public sealed record MediaCommand(MediaAction Action, double Value = 0, string? TrackId = null);
public sealed record MediaSnapshot(string TrackId, string Title, string Artist, string? ArtworkUrl,
    string? TrackUrl, double Position, double Duration, MediaPlayback Playback,
    bool CanSeek, bool CanNext, bool CanPrevious, double Volume, bool Shuffle);

public interface IMediaSession : IDisposable
{
    event Action<MediaCommand>? Command;
    void Update(MediaSnapshot snapshot);
    void Clear();
}

public static class MediaSession
{
    // UI dispatch is supplied by the host, so platform callbacks never access UI directly.
    public static async Task<IMediaSession> CreateAsync(Action<Action> dispatch)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows()) return new Windows.WindowsMediaSession();
#endif
        if (OperatingSystem.IsLinux()) return await Linux.MprisMediaSession.CreateAsync().ConfigureAwait(false);
        if (OperatingSystem.IsMacOS()) return new MacOS.MacMediaSession(dispatch);
        throw new PlatformNotSupportedException("No media-session backend for this target.");
    }
}
