using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record UserPlaylistActionView(LibraryItem Item, StackPanel Root, Button Like, Button Repost,
        Image Heart, Image Repeat, Button Queue);
    private readonly List<UserPlaylistActionView> userPlaylistActionViews = [];

    private FrameworkElement UserPlaylistActions(LibraryItem item, List<UserPlaylistActionView>? views = null, int size = 40)
    {
        var heart = Icons.View("heart", 18, TrackButtons.OnSurface);
        var repeat = Icons.View("repeat", 20, TrackButtons.OnSurface);
        var like = TrackButtons.Icon(heart, () => Run(() => TogglePlaylistActionAsync(item.Playlist!, false)), TrackButtons.Glass, size);
        var repost = TrackButtons.Icon(repeat, () => Run(() => TogglePlaylistActionAsync(item.Playlist!, true)), TrackButtons.Glass, size);
        var copy = TrackButtons.Icon(Icons.View("copy", 20, TrackButtons.OnSurface), () =>
        { if (TrackLink(item.Playlist?.PermalinkUrl) is { } uri) CopyText(uri.AbsoluteUri); }, TrackButtons.Glass, size).ToolTip("Скопировать ссылку");
        copy.IsEnabled = TrackLink(item.Playlist?.PermalinkUrl) != null;
        var queue = TrackButtons.Icon(Icons.View("queue", 20, TrackButtons.OnSurface),
            () => Run(() => EnqueueUserPlaylistAsync(item)), TrackButtons.Glass, size).ToolTip("Добавить в очередь");
        var root = new StackPanel().Horizontal().Spacing(TrackButtons.ActionSpacing).Height(size).Left()
            .Children(TrackButtons.Connected(like, repost), copy, queue);
        (views ?? userPlaylistActionViews).Add(new(item, root, like, repost, heart, repeat, queue));
        RefreshUserPlaylistActions();
        return root;
    }

    private void RefreshUserPlaylistActions()
    {
        foreach (var view in userPlaylistActionViews.Concat(feedPlaylistActions))
        {
            var playlist = view.Item.Playlist!;
            var available = !demo && me != null && api.Session is { } session && collectionActionsReady && collectionActionsSession == session;
            var liked = likedPlaylistIds.Contains(playlist.Id); var reposted = repostedPlaylistIds.Contains(playlist.Id);
            var likePending = api.Session is { } likeSession && pendingCollectionActions.Contains((likeSession, playlist.Id, false));
            var repostPending = api.Session is { } repostSession && pendingCollectionActions.Contains((repostSession, playlist.Id, true));
            TrackButtons.SetLiked(view.Like, liked, TrackButtons.Glass);
            TrackButtons.SetReposted(view.Repost, reposted, TrackButtons.Glass);
            TrackButtons.SetGlyph(view.Heart, liked ? "heart-filled" : "heart", liked ? TrackButtons.OnSelected : TrackButtons.OnSurface);
            TrackButtons.SetGlyph(view.Repeat, "repeat", reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface);
            TrackButtons.SetAvailability(view.Like, available && !likePending, likePending);
            TrackButtons.SetAvailability(view.Repost, available && playlist.User?.Id != me?.Id && !repostPending, repostPending);
            view.Like.ToolTip(liked ? "Убрать из понравившегося" : "Добавить в понравившееся");
            view.Repost.ToolTip(reposted ? "Убрать репост" : "Сделать репост");
        }
    }

    private async Task EnqueueUserPlaylistAsync(LibraryItem item)
    {
        var session = api.Session;
        var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, lifetime.Token);
        if (disposed || api.Session != session) return;
        collectionDetails[item.Key] = detail;
        foreach (var track in detail.Tracks.Tracks) playbackQueue.Enqueue(track, false);
        RefreshQueue(); status.Value = "Треки добавлены в очередь";
    }
}
