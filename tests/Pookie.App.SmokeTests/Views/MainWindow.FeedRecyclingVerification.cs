using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyFeedRecyclingAsync(FeedFixture fixture)
    {
        var host = new StackPanel().Vertical().Spacing(12);
        var avatar = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string avatarUrl = "https://i1.sndcdn.com/delayed-feed-avatar.jpg";
        artworkRequests[avatarUrl] = avatar.Task;
        var first = new FeedEntry("recycle-a", LibraryItem.FromTrack(fixture.Tracks[0]),
            fixture.Tracks[0].User! with { AvatarUrl = avatarUrl }, "track-repost", fixture.Tracks[0].CreatedAt);
        BindFeedPublication(host, first);
        var view = feedViews[host]; var header = host.Children[0]; var row = view.Track!;
        var second = first with { Key = "recycle-b", Actor = fixture.Tracks[1].User, Item = LibraryItem.FromTrack(fixture.Tracks[1]), Type = "track" };
        ClearFeedPublication(host);
        if (view.Bound || row.Track != null || row.Cover.Source != null || row.TrackArt.Source != null)
            throw new InvalidOperationException("Unbound feed host retained the outgoing track/artwork.");
        BindFeedPublication(host, second);
        avatar.SetResult(ImageSource.FromBytes(ArtworkFixtureBitmap(28)));
        await WaitForLoginFrameAsync();
        if (!ReferenceEquals(view, feedViews[host]) || !ReferenceEquals(row, view.Track) || !ReferenceEquals(header, host.Children[0]) ||
            row.Track?.Id != second.Item.Track!.Id || view.Repost.IsVisible || view.Name.Text != second.Actor!.Username ||
            !ReferenceEquals(view.Avatar.Source, Icons.Source("user-circle", MutedIcon)))
            throw new InvalidOperationException("Recycled feed host rebuilt controls or accepted the old actor/avatar.");

        var delayed = new TaskCompletionSource<SoundCloudPlaylist>(TaskCreationOptions.RunContinuationsAsynchronously);
        var playlist = new SoundCloudPlaylist { Id = 90701, Title = "Delayed playlist", User = fixture.Tracks[0].User, TrackCount = 3, Tracks = [] };
        fixture.Details[playlist.Id] = delayed.Task;
        var publication = first with { Key = "recycle-playlist-a", Item = LibraryData.FromPlaylist(playlist), Type = "playlist" };
        BindFeedPublication(host, publication);
        await WaitForLikedLayoutAsync(() => fixture.DetailReads == 1);
        var playlistRoot = view.PlaylistRoot; var preview = view.Preview.ToArray(); var buttons = view.Actions.Single();
        var other = playlist with { Id = 90702, Title = "Current playlist", Tracks = fixture.Tracks.Skip(5).Take(1).ToArray(), TrackCount = 1 };
        publication = publication with { Key = "recycle-playlist-b", Item = LibraryData.FromPlaylist(other) };
        collectionDetails[publication.Item.Key] = new(publication.Item, new(other.Tracks, null));
        BindFeedPublication(host, publication);
        delayed.SetResult(playlist with { Tracks = fixture.Tracks.Take(3).ToArray() });
        await WaitForLoginFrameAsync();
        if (!ReferenceEquals(playlistRoot, view.PlaylistRoot) || !preview.SequenceEqual(view.Preview) ||
            !ReferenceEquals(buttons, view.Actions.Single()) || buttons.Item.Playlist?.Id != other.Id ||
            view.Preview.Count(r => r.Root.IsVisible) != 1 || view.Preview[0].Track?.Id != other.Tracks[0].Id ||
            view.PlaylistTitle.Text != other.Title || row.Track != null)
            throw new InvalidOperationException("Playlist recycling lost controls, retained extra rows or accepted a stale preview.");

        // Exercise the actual retained action delegate, not just its displayed item.
        var raiseClick = typeof(Button).GetMethod("RaiseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var manualBefore = playbackQueue.Snapshot.ManualUpcoming.Count;
        raiseClick.Invoke(buttons.Queue, null);
        await WaitForLikedLayoutAsync(() => playbackQueue.Snapshot.ManualUpcoming.Count == manualBefore + 1);
        if (playbackQueue.Snapshot.ManualUpcoming.Last().Track.Id != other.Tracks[0].Id)
            throw new InvalidOperationException("Recycled playlist action targeted its original collection.");
        ClearFeedPublication(host);
        raiseClick.Invoke(buttons.Queue, null);
        if (feedPlaylistActions.Contains(buttons) || view.Preview.Any(r => r.Track != null) || view.Covers.Any(c => c.Source != null) ||
            playbackQueue.Snapshot.ManualUpcoming.Count != manualBefore + 1)
            throw new InvalidOperationException("Unbound publication retained active actions, preview tracks or artwork.");
        BindFeedPublication(host, first with { Actor = fixture.Tracks[0].User });
        if (!ReferenceEquals(row, view.Track) || !ReferenceEquals(row.Root, host.Children[1]))
            throw new InvalidOperationException("Switching publication type discarded the pooled waveform row.");
        ClearFeedPublication(host);
        var fixedActions = new List<UserPlaylistActionView>();
        var fixedRoot = UserPlaylistActions(publication.Item, fixedActions, 36);
        raiseClick.Invoke(fixedActions.Single().Queue, null);
        await WaitForLikedLayoutAsync(() => playbackQueue.Snapshot.ManualUpcoming.Count == manualBefore + 2);
        if (playbackQueue.Snapshot.ManualUpcoming.Last().Track.Id != other.Tracks[0].Id)
            throw new InvalidOperationException("Shared playlist actions lost their fixed-item behavior outside the feed.");
        fixedRoot.Dispose();
        Console.WriteLine("FEED_RECYCLING_OK: stable headers, waveform/playlist controls, current action targets, stale avatar/preview isolation and unbound cleanup");
    }
}
