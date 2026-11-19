using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum TrackRowAppearance { Library, ProfileTrack, Publication }

    private LikedTrackRow CreateTrackRow(Action<SoundCloudTrack> select, Action<SoundCloudTrack, double> seek,
        TrackRowAppearance appearance, bool pooled, Func<TextBlock, FrameworkElement>? byline = null)
    {
        var publication = appearance == TrackRowAppearance.Publication;
        LikedTrackRow row = null!;
        row = new LikedTrackRow(select, track => Run(() => OpenTrackFromRowAsync(track, row.Root)),
            track => Run(() => ToggleTrackLikeAsync(track)), track => Run(() => ToggleTrackRepostAsync(track)),
            track => Run(() => OpenPlaylistPickerAsync(track)), seek, ArtworkLayer,
            glass: appearance != TrackRowAppearance.ProfileTrack, publication: publication,
            byline: byline, share: ShareTrack, recycleBuffers: pooled);
        likedRows.Add(row.Root, row);
        if (publication) row.AddQueueAction(track => EnqueueTrack(track, false));
        AttachTrackQueueMenu(row.Root, () => row.Track);
        AttachTrackTitle(row.Title, () => row.Track);
        AttachUserLink(row.Author, () => row.Track?.User);
        return row;
    }

    // Every virtualized host uses the same retention policy. Permanent owners
    // release these buffers; unbinding only clears the outgoing track's pixels.
    private LikedTrackRow CreatePooledLibraryTrackRow(Action<SoundCloudTrack>? select = null,
        Action<SoundCloudTrack, double>? seek = null) => CreateLibraryTrackRow(select, seek, recycleBuffers: true);
}
