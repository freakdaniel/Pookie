using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record UserPlaylistActionView(StackPanel Root, Button Like, Button Repost,
        Image Heart, Image Repeat, Button Queue)
    {
        internal required LibraryItem Item { get; set; }
    }
    private readonly List<UserPlaylistActionView> userPlaylistActionViews = [];

    private FrameworkElement UserPlaylistActions(LibraryItem item, List<UserPlaylistActionView>? views = null, int size = 40,
        Func<LibraryItem?>? currentItem = null)
    {
        LibraryItem? Current() => currentItem == null ? item : currentItem();
        var heart = Icons.View("heart", 18, TrackButtons.OnSurface);
        var repeat = Icons.View("repeat", 20, TrackButtons.OnSurface);
        var like = TrackButtons.Icon(heart, () => { if (Current() is { Playlist: { } playlist }) Run(() => TogglePlaylistActionAsync(playlist, false)); }, TrackButtons.Glass, size);
        var repost = TrackButtons.Icon(repeat, () => { if (Current() is { Playlist: { } playlist }) Run(() => TogglePlaylistActionAsync(playlist, true)); }, TrackButtons.Glass, size);
        var queue = TrackButtons.Icon(Icons.View("queue", 20, TrackButtons.OnSurface),
            () => { if (Current() is { } value) Run(() => EnqueueUserPlaylistAsync(value)); }, TrackButtons.Glass, size).Hint("Добавить в очередь");
        var root = new StackPanel().Horizontal().Spacing(TrackButtons.ActionSpacing).Height(size).Left()
            .Children(TrackButtons.Connected(like, repost), TrackButtons.Icon(Icons.View("arrow-square-out", 20), () => { if (Current() is { } value) OpenShare(value); }, TrackButtons.Glass, size).Hint("Поделиться"), queue);
        (views ?? userPlaylistActionViews).Add(new(root, like, repost, heart, repeat, queue) { Item = item });
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
            view.Like.Hint(liked ? "Убрать из понравившегося" : "Добавить в понравившееся");
            view.Repost.Hint(reposted ? "Убрать репост" : "Сделать репост");
        }
    }

    private Task EnqueueUserPlaylistAsync(LibraryItem item) => EnqueueLibraryCollectionAsync(item);
}
