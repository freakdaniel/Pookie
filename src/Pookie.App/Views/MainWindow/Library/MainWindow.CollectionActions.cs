using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Button collectionLike = null!, collectionRepost = null!;
    private Image collectionHeart = null!, collectionRepostIcon = null!;
    private TextBlock collectionLikeLabel = null!, collectionRepostLabel = null!;
    private readonly HashSet<long> likedPlaylistIds = [], repostedPlaylistIds = [];
    private readonly HashSet<(WebSession Session, long Playlist, bool Repost)> pendingCollectionActions = [];
    private WebSession? collectionActionsSession;
    private Task? collectionActionsTask;
    private bool collectionActionsReady;
    private string? collectionActionStatusKey;

    private void CreateCollectionActionButtons()
    {
        collectionHeart = Icons.View("heart", 20, TrackButtons.OnSurface);
        collectionRepostIcon = Icons.View("repeat", 20, TrackButtons.OnSurface);
        collectionLikeLabel = new TextBlock().Text("Нравится").FontSize(14).SemiBold().Foreground(TrackButtons.OnSurface).CenterVertical();
        collectionRepostLabel = new TextBlock().Text("Репост").FontSize(14).SemiBold().Foreground(TrackButtons.OnSurface).CenterVertical();
        collectionLike = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(collectionHeart.CenterVertical(), collectionLikeLabel),
            () => Run(() => ToggleCollectionActionAsync(false)), TrackButtons.Glass, 44);
        collectionRepost = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(collectionRepostIcon.CenterVertical(), collectionRepostLabel),
            () => Run(() => ToggleCollectionActionAsync(true)), TrackButtons.Glass, 44);
    }

    private void ResetCollectionActions()
    {
        collectionActionsSession = null; collectionActionsTask = null; collectionActionsReady = false;
        likedPlaylistIds.Clear(); repostedPlaylistIds.Clear(); collectionActionStatusKey = null;
        RefreshCollectionActions();
    }

    private void RefreshCollectionActions()
    {
        RefreshUserPlaylistActions();
        if (collectionLike == null) return;
        var playlist = displayedCollection?.Playlist;
        var available = !demo && me != null && api.Session != null && playlist?.Id > 0 && collectionActionsReady && collectionActionsSession == api.Session;
        var liked = playlist != null && likedPlaylistIds.Contains(playlist.Id);
        var reposted = playlist != null && repostedPlaylistIds.Contains(playlist.Id);
        var likePending = playlist != null && api.Session is { } session && pendingCollectionActions.Contains((session, playlist.Id, false));
        var repostPending = playlist != null && api.Session is { } repostSession && pendingCollectionActions.Contains((repostSession, playlist.Id, true));
        TrackButtons.SetLiked(collectionLike, liked, TrackButtons.Glass);
        TrackButtons.SetReposted(collectionRepost, reposted, TrackButtons.Glass);
        TrackButtons.SetGlyph(collectionHeart, liked ? "heart-filled" : "heart", liked ? TrackButtons.OnSelected : TrackButtons.OnSurface);
        TrackButtons.SetGlyph(collectionRepostIcon, "repeat", reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface);
        TrackButtons.SetTextColor(collectionLikeLabel, liked ? TrackButtons.OnSelected : TrackButtons.OnSurface);
        TrackButtons.SetTextColor(collectionRepostLabel, reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface);
        TrackButtons.SetAvailability(collectionLike, available && !likePending, likePending);
        TrackButtons.SetAvailability(collectionRepost, available && playlist?.User?.Id != me?.Id && !repostPending, repostPending);
    }

    private Task LoadCollectionActionsAsync() => EnsureCollectionActionsAsync(displayedCollection?.Playlist?.Id is > 0);
    private Task LoadUserPlaylistActionsAsync() => EnsureCollectionActionsAsync(userPublications.Any(entry => entry.Item.Playlist?.Id is > 0));
    private Task EnsureCollectionActionsAsync(bool hasPlaylist)
    {
        if (demo || me == null || api.Session is not { } session || !hasPlaylist) return Task.CompletedTask;
        if (collectionActionsSession != session)
        {
            likedPlaylistIds.Clear(); repostedPlaylistIds.Clear(); collectionActionsReady = false;
            collectionActionsSession = session; collectionActionsTask = null;
        }
        if (collectionActionsReady) return Task.CompletedTask;
        if (collectionActionsTask is { IsCompleted: true }) collectionActionsTask = null;
        return collectionActionsTask ??= LoadCollectionActionsCoreAsync(session);
    }

    private async Task LoadCollectionActionsCoreAsync(WebSession session)
    {
        try
        {
            var likes = await api.GetPlaylistActionIdsAsync(false, lifetime.Token);
            if (disposed || api.Session != session || collectionActionsSession != session) return;
            var reposts = await api.GetPlaylistActionIdsAsync(true, lifetime.Token);
            if (disposed || api.Session != session || collectionActionsSession != session) return;
            likedPlaylistIds.UnionWith(likes); repostedPlaylistIds.UnionWith(reposts); collectionActionsReady = true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        {
            // Keep unknown states unavailable; never offer a toggle based on a failed read.
            if (!disposed && api.Session == session && playlistCollectionVisible.Value && collectionActionStatusKey != displayedCollection?.Key)
            { collectionActionStatusKey = displayedCollection?.Key; status.Value = FriendlyError(error); }
        }
        finally { if (!disposed && collectionActionsSession == session) RefreshCollectionActions(); }
    }

    private Task ToggleCollectionActionAsync(bool repost) => displayedCollection?.Playlist is { } playlist
        ? TogglePlaylistActionAsync(playlist, repost) : Task.CompletedTask;

    private async Task TogglePlaylistActionAsync(SoundCloudPlaylist playlist, bool repost)
    {
        if (playlist.Id <= 0 || api.Session is not { } session || me is not { } user ||
            demo || !collectionActionsReady || collectionActionsSession != session || repost && playlist.User?.Id == user.Id || !pendingCollectionActions.Add((session, playlist.Id, repost))) return;
        RefreshCollectionActions();
        var ids = repost ? repostedPlaylistIds : likedPlaylistIds;
        var selected = !ids.Contains(playlist.Id);
        try
        {
            if (repost) await api.SetPlaylistRepostedAsync(playlist.Id, selected, lifetime.Token);
            else await api.SetPlaylistLikedAsync(user.Id, playlist.Id, selected, lifetime.Token);
            if (disposed || api.Session != session || collectionActionsSession != session) return;
            if (selected) ids.Add(playlist.Id); else ids.Remove(playlist.Id);
            libraryPages.Remove("collections"); overviewReady.Remove("collections");
            status.Value = repost ? selected ? "Плейлист появился в твоём профиле." : "Репост убран." : selected ? "Плейлист сохранён." : "Лайк убран.";
        }
        finally { pendingCollectionActions.Remove((session, playlist.Id, repost)); if (!disposed) RefreshCollectionActions(); }
    }
}
