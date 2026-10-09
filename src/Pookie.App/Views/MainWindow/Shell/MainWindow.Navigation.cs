using Pookie.App.Browser;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum Page { Home, Feed, Search, Track, Library, LibraryTracks, LibraryPlaylists, LibraryAlbums, LibraryStations, LibraryFollowing, LibraryHistory, LibraryCollection }
    private readonly ObservableValue<Page> page = new(Page.Home);
    private readonly ObservableValue<string> eyebrow = new("ГЛАВНАЯ / ОТКРЫВАЙ НОВУЮ МУЗЫКУ");
    private readonly ObservableValue<bool> profileOpen = new(false);
    private readonly ObservableValue<bool> settingsOpen = new(false);
    private readonly ObservableValue<bool> searching = new(false);
    private readonly ObservableValue<bool> hasLibraryTracks = new(false);
    private readonly ObservableValue<bool> queueOpen = new(false);
    private readonly ObservableValue<bool> playerVisible = new(false);
    private readonly ObservableValue<bool> shuffle = new(false);
    private readonly ObservableValue<bool> isPlaying = new(false);
    private readonly ObservableValue<bool> isLiked = new(false);
    private readonly ObservableValue<bool> likeAvailable = new(false);
    private readonly ObservableValue<bool> muted = new(false);
    private readonly ObservableValue<double> volume = new(70);
    private readonly ObservableValue<string> queueStatus = new("Выбери трек, чтобы собрать очередь");
    private readonly HashSet<long> likedIds = [];
    private readonly Dictionary<Image, long> rowTracks = [];
    private readonly Dictionary<long, ImageSource> coverCache = [];
    private readonly Dictionary<long, ImageSource> libraryCoverCache = [];
    private readonly SemaphoreSlim coverGate = new(4);
    private CancellationTokenSource? likedLoading;
    private Task? likedTask;
    private bool likedIdsReady, advancing;
    private readonly HashSet<(WebSession Session, long Track)> pendingLikes = [];
    private bool IsLikePending(SoundCloudTrack track) => api.Session is { } session && pendingLikes.Contains((session, track.Id));
    private bool likeBusy => pendingLikes.Any(item => item.Session == api.Session);
    private bool CanLikeTrack(SoundCloudTrack track) => me != null && !demo &&
        (api.Session == null || !pendingLikes.Contains((api.Session, track.Id)));
    private double previousVolume = 70;

    private Task NavigateAsync(Page target)
    {
        if (!CanUseWorkspace) return Task.CompletedTask;
        if (target == Page.Library) return LikesAsync();
        if (target == Page.LibraryTracks) { ShowLibraryTracks(); return Task.CompletedTask; }
        if (target > Page.LibraryTracks) return ShowLibrarySectionAsync(target);
        return NavigateRouteAsync(new(target), async _ =>
        {
            eyebrow.Value = target == Page.Home ? "ГЛАВНАЯ / ОТКРЫВАЙ НОВУЮ МУЗЫКУ" : "ЛЕНТА / ТВОИ ПОДПИСКИ";
            heading.Value = target == Page.Home ? "На твоей волне" : "Новое от тех, кого ты слушаешь";
            if (demo) { status.Value = "Демо-режим: локальные тестовые звуки."; return; }
            var token = BeginLoad();
            ReplaceTracks(new([], null));
            if (target == Page.Feed && me == null)
            {
                status.Value = "Войди через профиль справа сверху, чтобы увидеть треки и репосты своих подписок.";
                return;
            }
            var result = target == Page.Home ? await api.SearchAsync("ambient", token) : await api.GetFeedAsync(token);
            token.ThrowIfCancellationRequested();
            ReplaceTracks(result);
            status.Value = target == Page.Home ? "Начни с ambient или найди музыку под своё настроение." : "Треки и репосты из твоей ленты SoundCloud.";
        });
    }

    private void ToggleMute()
    {
        if (volume.Value > 0) { previousVolume = volume.Value; volume.Value = 0; }
        else volume.Value = previousVolume > 0 ? previousVolume : 70;
    }

    private void UpdateLikeState()
    {
        isLiked.Value = current != null && likedIds.Contains(current.Id);
        likeAvailable.Value = current != null && CanLikeTrack(current);
        RefreshLikedRows();
    }

    private Task LoadLikedIdsAsync()
    {
        if (likedTask is { IsCompleted: false }) return likedTask;
        likedLoading?.Cancel(); likedLoading?.Dispose();
        likedLoading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = likedLoading.Token;
        var session = api.Session;
        if (session == null) return Task.CompletedTask;
        likedIdsReady = false;
        return likedTask = ReadLikedIdsAsync(session, token);
    }

    private async Task ReadLikedIdsAsync(WebSession session, CancellationToken token)
    {
        if (browser == null) AttachBrowser(new NativeBrowserSession(session));
        var ids = await browser!.GetLikedIdsAsync(token);
        token.ThrowIfCancellationRequested();
        if (api.Session != session) return;
        likedIds.Clear(); likedIds.UnionWith(ids); likedIdsReady = true;
        UpdateLikeState();
    }

    private Task ToggleLikeAsync() => current is { } track ? ToggleTrackLikeAsync(track) : Task.CompletedTask;

    private async Task ToggleTrackLikeAsync(SoundCloudTrack track)
    {
        if (demo || me == null || api.Session == null || !pendingLikes.Add((api.Session, track.Id))) return;
        likedActionError.Value = "";
        var user = me; var session = api.Session;
        UpdateLikeState();
        try
        {
            if (!likedIdsReady) await LoadLikedIdsAsync();
            if (api.Session != session) return;
            var liked = !likedIds.Contains(track.Id);
            var token = likedLoading?.Token ?? lifetime.Token;
            if (api.BrowserTransport == null) AttachBrowser(new NativeBrowserSession(session));
            status.Value = liked ? "Добавляем лайк…" : "Снимаем лайк…";
            await api.BrowserTransport!.SetLikedAsync(user.Id, track.Id, liked, token);
            if (api.Session != session || disposed) return;
            if (liked) likedIds.Add(track.Id); else likedIds.Remove(track.Id);
            UpdateLikeState();
            status.Value = liked ? "Трек добавлен в твои лайки SoundCloud." : "Трек удалён из лайков SoundCloud.";
            await ApplyLibraryLikeAsync(track, liked, session);
        }
        finally { pendingLikes.Remove((session, track.Id)); if (!disposed) UpdateLikeState(); }
    }

}
