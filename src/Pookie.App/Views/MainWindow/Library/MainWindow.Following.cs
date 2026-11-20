using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly HashSet<long> followingIds = [];
    private readonly HashSet<(WebSession Session, long Artist)> pendingFollows = [];
    private readonly List<(Button Button, Image Icon, Func<SoundCloudUser?> User, string Inactive)> artistFollowButtons = [];
    private WebSession? followingSession;
    private Task? followingTask;
    private bool followingReady;

    private void ResetFollowing()
    {
        followingIds.Clear(); followingSession = null; followingTask = null; followingReady = false;
        RefreshFollowingButtons();
    }

    private Button ArtistFollowButton(Func<SoundCloudUser?> user, string inactive = TrackButtons.Tonal, double size = 40, bool withLabel = false)
    {
        var icon = Icons.View("user-plus", 20, TrackButtons.OnSurface);
        Action action = () => { if (user() is { } artist) Run(() => ToggleArtistFollowAsync(artist)); };
        var button = withLabel ? TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(icon.CenterVertical(),
            new TextBlock().Text(UiText.Get("profile.follow")).FontSize(14).SemiBold().CenterVertical()), action, inactive, size) : TrackButtons.Icon(icon, action, inactive, size);
        artistFollowButtons.Add((button, icon, user, inactive));
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
        foreach (var (button, icon, getUser, inactive) in artistFollowButtons)
        {
            var user = getUser(); var following = user != null && followingSession == api.Session && followingIds.Contains(user.Id);
            var pending = user != null && api.Session is { } session && pendingFollows.Contains((session, user.Id));
            button.IsVisible = user is { Id: > 0 } && user.Id != me?.Id;
            TrackButtons.SetConfirmed(button, following, inactive);
            TrackButtons.SetGlyph(icon, following ? "user-check" : "user-plus", following ? TrackButtons.OnConfirmed : TrackButtons.OnSurface);
            TrackButtons.SetAvailability(button, !demo && user is { Id: > 0 } && me != null && api.Session != null && !pending, pending);
            button.Hint(following ? UiText.Get("profile.unfollow") : UiText.Get("profile.follow"));
            if (button.Content is StackPanel content && content.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            { label.Text = following ? UiText.Get("profile.following.active") : UiText.Get("profile.follow"); label.Foreground = following ? TrackButtons.OnConfirmed : TrackButtons.OnSurface; }
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
            UpdateProfileFollow(user, artist, following);
            if (libraryPages.TryGetValue("following", out var list))
            {
                libraryPages["following"] = ChangeMembership(list, UserItem(artist), following);
                if (page.Value == Page.LibraryFollowing) { activeCollection = libraryPages["following"]; RenderLibrarySection(); }
                RefreshOverviewSections();
            }
            InvalidateLibrarySource("following");
            ++feedFollowingRevision;
            if (feedState != null) feedState = feedState with { Checked = DateTimeOffset.MinValue };
            if (page.Value == Page.Feed) Run(RefreshVisibleDataAsync);
            status.Value = following ? UiText.Format("profile.follow.added", artist.Username): UiText.Format("profile.follow.removed", artist.Username);
        }
        finally { pendingFollows.Remove((session, artist.Id)); if (!disposed) RefreshFollowingButtons(); }
    }
}
