using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record UserPlaylistActionView(StackPanel Root, Button Like, Button Repost,
        Image Heart, Image Repeat, Button Queue, FrameworkElement Social)
    {
        internal required LibraryItem Item { get; set; }
    }
    private readonly List<UserPlaylistActionView> userPlaylistActionViews = [];

    private FrameworkElement UserPlaylistActions(LibraryItem item, List<UserPlaylistActionView>? views = null, double size = TrackRowLayout.ActionSize,
        Func<LibraryItem?>? currentItem = null)
    {
        LibraryItem? Current() => currentItem == null ? item : currentItem();
        var heart = Icons.View("heart", 18, TrackButtons.OnSurface);
        var repeat = Icons.View("repeat", 18, TrackButtons.OnSurface);
        var like = TrackButtons.Icon(heart, () => { if (Current() is { Playlist: { } playlist }) Run(() => TogglePlaylistActionAsync(playlist, false)); }, TrackButtons.Glass, size);
        var repost = TrackButtons.Icon(repeat, () => { if (Current() is { Playlist: { } playlist }) Run(() => TogglePlaylistActionAsync(playlist, true)); }, TrackButtons.Glass, size);
        var queue = TrackButtons.Icon(Icons.View("queue", 18, TrackButtons.OnSurface),
            () => { if (Current() is { } value) Run(() => EnqueueUserPlaylistAsync(value)); }, TrackButtons.Glass, size).Hint("Добавить в очередь");
        var social = TrackButtons.Connected(like, repost).IsVisible(item.Playlist?.IsPrivate != true);
        var root = new StackPanel().Horizontal().Spacing(TrackButtons.ActionSpacing).Height(size).Left()
            .Children(social, TrackButtons.Icon(Icons.View("arrow-square-out", 18), () => { if (Current() is { } value) OpenShare(value); }, TrackButtons.Glass, size).Hint("Поделиться"), queue);
        (views ?? userPlaylistActionViews).Add(new(root, like, repost, heart, repeat, queue, social) { Item = item });
        RefreshUserPlaylistActions();
        return root;
    }

    private void RefreshUserPlaylistActions()
    {
        foreach (var view in userPlaylistActionViews.Concat(feedPlaylistActions))
        {
            var playlist = view.Item.Playlist!;
            view.Social.IsVisible = !playlist.IsPrivate;
            var available = !playlist.IsPrivate && !demo && me != null && api.Session is { } session && playlist.Id > 0 && collectionActionsSession == session;
            var liked = PlaylistActionSelected(playlist.Id, false); var reposted = PlaylistActionSelected(playlist.Id, true);
            var likePending = api.Session is { } likeSession && pendingCollectionActions.ContainsKey((likeSession, playlist.Id, false));
            var repostPending = api.Session is { } repostSession && pendingCollectionActions.ContainsKey((repostSession, playlist.Id, true));
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
