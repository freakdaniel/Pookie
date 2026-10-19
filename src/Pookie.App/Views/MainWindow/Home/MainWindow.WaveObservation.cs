using Pookie.App.Playback;
using Pookie.Recommendations;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ListeningSource WaveListeningSource(PlaybackEntry entry)
    {
        if (entry.Origin == PlaybackEntryOrigin.Manual) return ListeningSource.Manual;
        if (entry.Origin == PlaybackEntryOrigin.Recommendation) return ListeningSource.Autoplay;
        return playbackQueue.Context?.Kind switch {
            PlaybackContextKind.Wave => ListeningSource.Wave,
            PlaybackContextKind.Likes => ListeningSource.Likes,
            PlaybackContextKind.Playlist => ListeningSource.Playlist,
            PlaybackContextKind.Album => ListeningSource.Album,
            PlaybackContextKind.Artist => ListeningSource.Artist,
            PlaybackContextKind.Feed => ListeningSource.Feed,
            PlaybackContextKind.Recommendations => ListeningSource.Recommendations,
            PlaybackContextKind.History => ListeningSource.History,
            PlaybackContextKind.Recent => ListeningSource.Recent,
            _ => ListeningSource.Track
        };
    }
}
