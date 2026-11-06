using Pookie.App.Browser;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum Page { Home, Feed, Search, Track, Chats, Library, LibraryTracks, LibraryPlaylists, LibraryAlbums, LibraryStations, LibraryFollowing, LibraryHistory, LibraryCollection, Settings, UserProfile }
    private readonly ObservableValue<Page> page = new(Page.Home);
    private readonly ObservableValue<string> eyebrow = new("ГЛАВНАЯ / ОТКРЫВАЙ НОВУЮ МУЗЫКУ");
    private readonly ObservableValue<bool> profileOpen = new(false);
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
    private readonly Storage.ResourceCache<long, ImageSource> coverCache = new(256, ArtworkCacheBudget, ArtworkBytes);
    private readonly Storage.ResourceCache<long, ImageSource> libraryCoverCache = new(256, ArtworkCacheBudget, ArtworkBytes);
    private readonly SemaphoreSlim coverGate = new(4);
    private CancellationTokenSource? likedLoading;
    private Task? likedTask;
    private bool likedIdsReady, advancing;
    private readonly Dictionary<(WebSession Session, long Track), bool> pendingLikes = [];
    private bool IsTrackLiked(SoundCloudTrack track) => api.Session is { } session && pendingLikes.TryGetValue((session, track.Id), out var liked)
        ? liked : likedIds.Contains(track.Id) || !likedIdsReady && page.Value is Page.Library or Page.LibraryTracks && tracks.Any(item => item.Id == track.Id);
    private bool IsLikePending(SoundCloudTrack track) => api.Session is { } session && pendingLikes.ContainsKey((session, track.Id));
    private bool likeBusy => pendingLikes.Keys.Any(item => item.Session == api.Session);
    private bool CanLikeTrack(SoundCloudTrack track) => me != null && !demo &&
        (api.Session == null || !pendingLikes.ContainsKey((api.Session, track.Id)));
    private double previousVolume = 70;

    private Task NavigateAsync(Page target)
    {
        if (!CanUseWorkspace) return Task.CompletedTask;
        if (target == Page.Home) return NavigateHomeAsync();
        if (target == Page.Feed) return OpenFeedAsync();
        if (target == Page.Chats) return OpenChatsAsync();
        if (target == Page.Settings) return ShowSettingsAsync(settingsSection.Value);
        if (target == Page.Library) return LikesAsync();
        if (target == Page.LibraryTracks) { ShowLibraryTracks(); return Task.CompletedTask; }
        if (IsCollectionPage(target)) return ShowLibrarySectionAsync(target);
        return Task.CompletedTask;
    }

    private void ToggleMute()
    {
        if (volume.Value > 0) { previousVolume = volume.Value; volume.Value = 0; }
        else volume.Value = previousVolume > 0 ? previousVolume : 70;
    }

    private void UpdateLikeState()
    {
        isLiked.Value = current != null && IsTrackLiked(current);
        likeAvailable.Value = current != null && CanLikeTrack(current);
        RefreshLikedRows();
    }

    private Task LoadLikedIdsAsync()
    {
        if (likedTask is { IsCompleted: false }) return likedTask;
        if (likedIdsReady) return Task.CompletedTask;
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
        if (api.BrowserTransport == null) AttachBrowser(CreateBrowserSession(session));
        var ids = await api.GetLikedIdsAsync(token);
        token.ThrowIfCancellationRequested();
        if (api.Session != session) return;
        likedIds.Clear(); likedIds.UnionWith(ids); likedIdsReady = true;
        UpdateLikeState();
    }

    private Task ToggleLikeAsync() => current is { } track ? ToggleTrackLikeAsync(track) : Task.CompletedTask;

    private async Task ToggleTrackLikeAsync(SoundCloudTrack track)
    {
        if (demo || me == null || api.Session == null) return;
        var user = me; var session = api.Session;
        // Capture the user's visible intent before any session/ID check can await.
        // Keep it separate from confirmed IDs so background reads cannot erase it.
        var liked = !IsTrackLiked(track);
        if (!pendingLikes.TryAdd((session, track.Id), liked)) return;
        likedActionError.Value = "";
        UpdateLikeState();
        try
        {
            if (!likedIdsReady) await LoadLikedIdsAsync();
            if (api.Session != session) return;
            var token = likedLoading?.Token ?? lifetime.Token;
            if (api.BrowserTransport == null) AttachBrowser(CreateBrowserSession(session));
            status.Value = liked ? "Добавляем лайк…" : "Снимаем лайк…";
            await api.BrowserTransport!.SetLikedAsync(user.Id, track.Id, liked, token);
            if (api.Session != session || disposed) return;
            if (liked) likedIds.Add(track.Id); else likedIds.Remove(track.Id);
            RefreshHomeShortcuts();
            UpdateLikeState();
            status.Value = liked ? "Трек добавлен в твои лайки SoundCloud." : "Трек удалён из лайков SoundCloud.";
            await ApplyLibraryLikeAsync(track, liked, session);
        }
        finally
        {
            pendingLikes.Remove((session, track.Id));
            // Existing heart/surface transitions animate rollback to confirmed state.
            if (!disposed) UpdateLikeState();
        }
    }

}
