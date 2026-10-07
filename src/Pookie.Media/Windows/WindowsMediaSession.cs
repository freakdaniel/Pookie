#if WINDOWS
using System.Runtime.InteropServices;
using LibSMTC;
using Windows.Media;

namespace Pookie.Media.Windows;

internal sealed class WindowsMediaSession : IMediaSession
{
    private readonly LibSmtcController controller;
    private (string Id, string Title, string Artist, string? Artwork)? metadata;
    private bool disposed;
    public event Action<MediaCommand>? Command;

    public WindowsMediaSession()
    {
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("Pookie"));
        controller = new();
        controller.Close();
        controller.PlayRequested += (_, _) => Send(MediaAction.Play);
        controller.PauseRequested += (_, _) => Send(MediaAction.Pause);
        controller.StopRequested += (_, _) => Send(MediaAction.Stop);
        controller.NextRequested += (_, _) => Send(MediaAction.Next);
        controller.PreviousRequested += (_, _) => Send(MediaAction.Previous);
        controller.FastForwardRequested += (_, _) => Send(MediaAction.SeekRelative, 10);
        controller.RewindRequested += (_, _) => Send(MediaAction.SeekRelative, -10);
        controller.SeekRequested += (_, e) => Send(MediaAction.Seek, e.Position.TotalSeconds);
        controller.ShuffleChangeRequested += (_, e) => Send(MediaAction.Shuffle, e.ShuffleEnabled ? 1 : 0);
    }

    private void Send(MediaAction action, double value = 0)
    { if (!disposed) Command?.Invoke(new(action, value)); }

    public void Update(MediaSnapshot state)
    {
        if (disposed) return;
        var next = (state.TrackId, state.Title, state.Artist, state.ArtworkUrl);
        if (metadata != next)
        {
            controller.SetMusicMetadata(state.Title, state.Artist);
            if (Uri.TryCreate(state.ArtworkUrl, UriKind.Absolute, out var artwork) && artwork.Scheme == "https")
                controller.SetThumbnailFromUri(artwork);
            else controller.ClearThumbnail();
            metadata = next;
        }
        controller.SetButtons(new() { PlayEnabled = true, PauseEnabled = true, StopEnabled = true,
            NextEnabled = state.CanNext, PreviousEnabled = state.CanPrevious });
        controller.IsShuffleActive = state.Shuffle;
        controller.SetTimeline(TimeSpan.FromSeconds(state.Position), TimeSpan.FromSeconds(state.Duration));
        controller.PlaybackStatus = state.Playback switch
        {
            MediaPlayback.Playing => MediaPlaybackStatus.Playing,
            MediaPlayback.Paused => MediaPlaybackStatus.Paused,
            MediaPlayback.Loading => MediaPlaybackStatus.Changing,
            _ => MediaPlaybackStatus.Stopped
        };
    }

    public void Clear()
    {
        if (disposed) return;
        metadata = null; controller.SetMusicMetadata("", ""); controller.ClearThumbnail(); controller.Close();
    }
    public void Dispose()
    { if (disposed) return; Clear(); disposed = true; controller.Dispose(); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
#endif
