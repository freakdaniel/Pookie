using Pookie.Localization;
using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private long renderedUserPublicationGeneration;
    private readonly List<Image> userPublicationCovers = [];
    private readonly List<(LibraryItem Item, Image Icon)> userPlaylistPlayIcons = [];
    private LikedTrackRow CreateUserTrackRow(bool publication = false, bool pooled = false,
        Func<TextBlock, FrameworkElement>? byline = null)
        => CreateTrackRow(track => Run(() => PlayUserTrackAsync(track)), (track, fraction) => Run(() => SeekUserTrackAsync(track, fraction)),
            publication ? TrackRowAppearance.Publication : TrackRowAppearance.ProfileTrack, pooled, byline);

    private static int UserPlaylistTrackCount(SoundCloudPlaylist playlist) => Math.Max(playlist.TrackCount, playlist.Tracks?.Length ?? 0);
    private static bool UserItemVisible(LibraryItem item) => item.Playlist == null || UserPlaylistTrackCount(item.Playlist) > 0;

    private async Task PlayUserPlaylistTrackAsync(LibraryItem item, SoundCloudTrack track)
    {
        var request = ++collectionPlaybackRequest;
        if (IsCurrentTrack(track)) { await ToggleAsync(); return; }
        var session = api.Session; var sourceVersion = playbackQueue.SourceVersion; var generation = playGeneration;
        var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, lifetime.Token);
        if (disposed || request != collectionPlaybackRequest || api.Session != session || playbackQueue.SourceVersion != sourceVersion || playGeneration != generation) return;
        collectionDetails[item.Key] = detail;
        StartQueueContext(CollectionPlaybackContext(detail), detail.Tracks.Tracks, track);
        await PlayAsync(track);
    }

    private static string UserPublicationDate(string? value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return "";
        var days = Math.Max(0, (DateTimeOffset.UtcNow - date).TotalDays);
        return days < 1 ? UiText.Get("time.today") : days < 30 ? UiText.Format("time.ago.days", (int)days): date.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}
