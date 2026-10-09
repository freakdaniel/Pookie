using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly HashSet<long> followingIds = [];
    private readonly HashSet<(WebSession Session, long Artist)> pendingFollows = [];
    private readonly List<(Button Button, Image Icon, Func<SoundCloudUser?> User)> artistFollowButtons = [];
    private WebSession? followingSession;
    private Task? followingTask;
    private bool followingReady;

    private void ResetFollowing()
    {
        followingIds.Clear(); followingSession = null; followingTask = null; followingReady = false;
        RefreshFollowingButtons();
    }

    private Button ArtistFollowButton(Func<SoundCloudUser?> user)
    {
        var icon = Icons.View("user-plus", 20, TrackButtons.OnSurface);
        var button = TrackButtons.Icon(icon, () => { if (user() is { } artist) Run(() => ToggleArtistFollowAsync(artist)); });
        artistFollowButtons.Add((button, icon, user));
        return button;
    }

    private async Task LoadFollowingAsync()
    {
        if (demo || me is not { } user || api.Session is not { } session) return;
        if (followingSession != session)
        { followingSession = session; followingIds.Clear(); followingReady = false; followingTask = null; }
        if (followingReady) return;
        if (followingTask is not { IsCompleted: false }) followingTask = ReadFollowingAsync(session, user.Id);
        await followingTask;
    }

    private async Task ReadFollowingAsync(WebSession session, long userId)
    {
        var ids = await api.GetFollowingIdsAsync(userId, lifetime.Token);
        if (disposed || api.Session != session || followingSession != session) return;
        followingIds.Clear(); followingIds.UnionWith(ids); followingReady = true;
        RefreshFollowingButtons();
    }

    private void RefreshFollowingButtons()
    {
        foreach (var (button, icon, getUser) in artistFollowButtons)
        {
            var user = getUser(); var following = user != null && followingSession == api.Session && followingIds.Contains(user.Id);
            var pending = user != null && api.Session is { } session && pendingFollows.Contains((session, user.Id));
            button.IsVisible = user is { Id: > 0 } && user.Id != me?.Id;
            TrackButtons.SetReposted(button, following);
            TrackButtons.SetGlyph(icon, following ? "user-check" : "user-plus", following ? TrackButtons.OnReposted : TrackButtons.OnSurface);
            TrackButtons.SetAvailability(button, !demo && user is { Id: > 0 } && me != null && api.Session != null && !pending, pending);
            button.ToolTip(following ? "Отписаться" : "Подписаться");
        }
    }

    private async Task ToggleArtistFollowAsync(SoundCloudUser artist)
    {
        if (demo || me is not { } user || artist.Id <= 0 || artist.Id == user.Id || api.Session is not { } session ||
            !pendingFollows.Add((session, artist.Id))) return;
        RefreshFollowingButtons();
        try
        {
            await LoadFollowingAsync();
            if (api.Session != session || disposed) return;
            var following = !followingIds.Contains(artist.Id);
            await api.SetFollowingAsync(user.Id, artist.Id, following, lifetime.Token);
            if (api.Session != session || disposed) return;
            if (following) followingIds.Add(artist.Id); else followingIds.Remove(artist.Id);
            // The next library visit must read the actual following collection.
            libraryPages.Remove("following"); libraryLoads.Remove("following"); libraryLoadedAt.Remove("following");
            overviewReady.Remove("following");
            status.Value = following ? $"Теперь ты подписан на {artist.Username}." : $"Подписка на {artist.Username} отменена.";
        }
        finally { pendingFollows.Remove((session, artist.Id)); if (!disposed) RefreshFollowingButtons(); }
    }
}
