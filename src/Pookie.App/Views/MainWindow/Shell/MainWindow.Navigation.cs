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
    private readonly ObservableValue<bool> isDisliked = new(false);
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
    private readonly HashSet<(WebSession Session, long Track)> pendingLikes = [];
    private bool IsLikePending(SoundCloudTrack track) => api.Session is { } session && pendingLikes.Contains((session, track.Id));
    private bool likeBusy => pendingLikes.Any(item => item.Session == api.Session);
    private bool CanLikeTrack(SoundCloudTrack track) => me != null && !demo &&
        (api.Session == null || !pendingLikes.Contains((api.Session, track.Id)));
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
        isDisliked.Value = current != null && waveTaste.IsBlocked(current);
        isLiked.Value = current != null && !isDisliked.Value && likedIds.Contains(current.Id);
        likeAvailable.Value = current != null && CanLikeTrack(current);
        RefreshLikedRows();
        if (page.Value == Page.Settings && settingsSection.Value == SettingsSection.Wave)
        { RefreshSettingsData(); RefreshSettingsControls(); }
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
        EnsureWaveTaste(); waveTaste.SyncLikes(likedIds); ScheduleWaveSave(); RerankWaveQueue();
        UpdateLikeState();
    }

    private Task ToggleLikeAsync() => current is { } track ? ToggleTrackLikeAsync(track) : Task.CompletedTask;

    private async Task ToggleTrackLikeAsync(SoundCloudTrack track)
    {
        if (demo || me == null || api.Session == null || !pendingLikes.Add((api.Session, track.Id))) return;
        likedActionError.Value = "";
        var user = me; var session = api.Session;
        var playbackId = waveListening.Track?.Id == track.Id ? waveListening.PlaybackId : null;
        UpdateLikeState();
        try
        {
            if (!likedIdsReady) await LoadLikedIdsAsync();
            if (api.Session != session) return;
            var liked = waveTaste.IsBlocked(track) || !likedIds.Contains(track.Id);
            var token = likedLoading?.Token ?? lifetime.Token;
            if (api.BrowserTransport == null) AttachBrowser(CreateBrowserSession(session));
            status.Value = liked ? "Добавляем лайк…" : "Снимаем лайк…";
            await api.BrowserTransport!.SetLikedAsync(user.Id, track.Id, liked, token);
            if (api.Session != session || disposed) return;
            if (liked) likedIds.Add(track.Id); else likedIds.Remove(track.Id);
            WaveLikeChanged(track, liked, playbackId);
            UpdateLikeState();
            status.Value = liked ? "Трек добавлен в твои лайки SoundCloud." : "Трек удалён из лайков SoundCloud.";
            await ApplyLibraryLikeAsync(track, liked, session);
        }
        finally { pendingLikes.Remove((session, track.Id)); if (!disposed) UpdateLikeState(); }
    }

}
