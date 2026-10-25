using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.App.Playback;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum UserSection { Overview, Tracks, Albums, Playlists, Reposts, Likes, About, Followers, Following, Comments }
    private sealed record UserProfileState(SoundCloudUser User, Dictionary<UserSection, LibraryPage> Sections,
        Dictionary<UserSection, string> Errors, HashSet<UserSection> Loading, UserWebProfile[] Links,
        bool MetadataLoading = true, string MetadataError = "", bool LinksLoaded = false)
    { public UserCommentPage? Comments { get; init; } }
    private UserProfileState? userProfileState;
    private readonly ObservableValue<UserSection> userSection = new(UserSection.Overview);
    private string? failedUserCursor;
    private ScrollViewer? UserProfileScroll => userSection.Value switch
    {
        UserSection.Overview => userOverviewScroll,
        UserSection.About => userAboutScroll,
        UserSection.Comments => userCommentsScroll,
        UserSection.Tracks => userTrackList.FindVisualChild<ScrollViewer>() as ScrollViewer,
        _ => userCardGrid.FindVisualChild<ScrollViewer>() as ScrollViewer
    };
    private static LibraryItem UserItem(SoundCloudUser user) => new("user:" + user.Id, user.Username, "Профиль", user.AvatarUrl, User: user);
    private static readonly UserSection[] UserOverviewSources = [UserSection.Overview, UserSection.Tracks, UserSection.Likes, UserSection.Followers, UserSection.Following, UserSection.Comments];
    private static string UserSectionTitle(UserSection section) => section switch
    {
        UserSection.Overview => "Обзор", UserSection.Tracks => "Треки", UserSection.Albums => "Альбомы",
        UserSection.Playlists => "Плейлисты", UserSection.Reposts => "Репосты", UserSection.Likes => "Лайки",
        UserSection.About => "О пользователе", UserSection.Comments => "Комментарии", UserSection.Followers => "Подписчики", _ => "Подписки"
    };
    private static string UserSectionApi(UserSection section) => section switch
    {
        UserSection.Overview => "activity", UserSection.Tracks => "tracks", UserSection.Albums => "albums", UserSection.Playlists => "playlists",
        UserSection.Reposts => "reposts", UserSection.Likes => "likes", UserSection.Followers => "followers",
        UserSection.Following => "following", UserSection.Comments => "comments", _ => throw new ArgumentOutOfRangeException(nameof(section))
    };
    private bool IsUserProfileCurrent(long id, long generation) => !disposed && page.Value == Page.UserProfile &&
        userProfileState?.User.Id == id && navigationGeneration == generation;

    private Task OpenUserProfileAsync(SoundCloudUser user, UserSection section = UserSection.Overview)
    {
        if (user.Id <= 0) return Task.CompletedTask;
        var previous = userProfileState;
        return NavigateRouteAsync(new(Page.UserProfile, Item: UserItem(user), UserSection: section), async generation =>
        {
            userSection.Value = section; failedUserCursor = null;
            userProfileState = previous?.User.Id == user.Id ? previous with { Loading = [] } : new(user, [], [], [], []);
            var sections = section == UserSection.Overview ? UserOverviewSources :
                section == UserSection.About ? new[] { UserSection.Tracks } : new[] { section };
            var missing = sections.Where(source => source == UserSection.Comments ? userProfileState.Comments == null : !userProfileState.Sections.ContainsKey(source)).ToArray();
            // Reserve every pending section before the first frame, not one request at a time.
            userProfileState = userProfileState with { Loading = new(missing) };
            RenderUserProfile();
            var token = loading!.Token;
            if (demo && api.Session == null) { userProfileState = userProfileState with { Loading = [], MetadataLoading = false, MetadataError = "Профили доступны после входа в SoundCloud" }; RenderUserProfile(); return; }
            var tasks = new List<Task>();
            if (userProfileState.MetadataLoading || userProfileState.MetadataError.Length > 0 || !userProfileState.LinksLoaded) tasks.Add(LoadUserMetadataAsync(user.Id, generation, token));
            foreach (var source in missing) tasks.Add(LoadUserSectionAsync(source, generation, token, scheduled: true));
            Run(LoadFollowingAsync);
            await Task.WhenAll(tasks);
        });
    }

    private async Task LoadUserMetadataAsync(long id, long generation, CancellationToken token)
    {
        try
        {
            var user = await api.GetUserAsync(id, token);
            if (!IsUserProfileCurrent(id, generation)) return;
            userProfileState = userProfileState! with { User = user, MetadataLoading = false, MetadataError = "" };
            RenderUserProfile();
            try
            {
                var links = await api.GetUserLinksAsync(id, token);
                if (!IsUserProfileCurrent(id, generation)) return;
                userProfileState = userProfileState! with { Links = links, LinksLoaded = true }; RenderUserProfile();
            }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException) { }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        {
            if (!IsUserProfileCurrent(id, generation)) return;
            userProfileState = userProfileState! with { MetadataLoading = false, MetadataError = FriendlyError(error).TrimEnd('.') };
            RenderUserProfile();
        }
    }

    private async Task LoadUserSectionAsync(UserSection section, long generation, CancellationToken token, string? cursor = null, bool scheduled = false)
    {
        if (userProfileState is not { } state || !scheduled && state.Loading.Contains(section)) return;
        var id = state.User.Id;
        var pending = new HashSet<UserSection>(state.Loading) { section };
        var errors = new Dictionary<UserSection, string>(state.Errors); errors.Remove(section);
        userProfileState = state with { Loading = pending, Errors = errors }; RenderUserProfile();
        try
        {
            if (section == UserSection.Comments)
            {
                var commentPage = await api.GetUserCommentsAsync(id, cursor, token);
                if (!IsUserProfileCurrent(id, generation)) return;
                var comments = cursor == null ? commentPage.Comments : (userProfileState!.Comments?.Comments ?? []).Concat(commentPage.Comments).DistinctBy(entry => entry.Comment.Id).ToArray();
                userProfileState = userProfileState! with { Comments = new(comments, commentPage.NextHref == cursor || cursor != null && commentPage.Comments.Length == 0 ? null : commentPage.NextHref) };
                return;
            }
            var result = await api.GetUserSectionAsync(id, UserSectionApi(section), cursor, token);
            if (!IsUserProfileCurrent(id, generation)) return;
            var pages = new Dictionary<UserSection, LibraryPage>(userProfileState!.Sections);
            var items = cursor == null ? result.Items : (pages.GetValueOrDefault(section)?.Items ?? []).Concat(result.Items).DistinctBy(item => item.Key).ToArray();
            pages[section] = new(items, result.NextHref == cursor || cursor != null && result.Items.Length == 0 ? null : result.NextHref);
            userProfileState = userProfileState with { Sections = pages };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        {
            if (!IsUserProfileCurrent(id, generation)) return;
            errors = new(userProfileState!.Errors) { [section] = FriendlyError(error).TrimEnd('.') };
            userProfileState = userProfileState with { Errors = errors }; failedUserCursor = cursor;
        }
        finally
        {
            if (IsUserProfileCurrent(id, generation))
            {
                pending = new(userProfileState!.Loading); pending.Remove(section);
                userProfileState = userProfileState with { Loading = pending }; RenderUserProfile();
            }
        }
    }

    private Task OpenUserSectionAsync(UserSection section) => userProfileState is { } state && userSection.Value != section
        ? OpenUserProfileAsync(state.User, section) : Task.CompletedTask;
    private void CheckUserProfileEnd()
    {
        LoadVisibleUserPublications();
        if (page.Value == Page.UserProfile && userSection.Value == UserSection.Overview) RefreshUserBiographyOverflow();
        if (page.Value != Page.UserProfile || userSection.Value is UserSection.About ||
            userProfileState is not { } state || navigationHistory[navigationIndex].Pending || state.Loading.Contains(userSection.Value)) return;
        var cursor = userSection.Value == UserSection.Comments ? state.Comments?.NextHref : state.Sections.GetValueOrDefault(userSection.Value)?.NextHref;
        if (cursor == null || cursor == failedUserCursor || UserProfileScroll is not { ViewportHeight: > 0 } scroll ||
            SmoothScroll.Maximum(scroll) - scroll.VerticalOffset > Math.Max(160, scroll.ViewportHeight * .5)) return;
        Run(() => LoadUserSectionAsync(userSection.Value, navigationGeneration, loading!.Token, cursor));
    }
    private void RetryUserProfile()
    {
        if (userProfileState is not { } state) return;
        failedUserCursor = null;
        var generation = navigationGeneration; var token = loading!.Token;
        if (state.MetadataError.Length > 0) Run(() => LoadUserMetadataAsync(state.User.Id, generation, token));
        var sections = userSection.Value == UserSection.Overview ? UserOverviewSources : new[] { userSection.Value };
        foreach (var section in sections.Where(state.Errors.ContainsKey))
            Run(() => LoadUserSectionAsync(section, generation, token, section == UserSection.Comments ? state.Comments?.NextHref : state.Sections.GetValueOrDefault(section)?.NextHref));
    }
    private LibraryPage? UserMusicPage => userProfileState?.Sections.GetValueOrDefault(userSection.Value);
    private async Task PlayUserTrackAsync(SoundCloudTrack track)
    {
        if (page.Value != Page.UserProfile || userProfileState is not { } state) return;
        var source = userSection.Value; var key = $"user:{state.User.Id}:{UserSectionApi(source)}";
        if (current?.Id == track.Id && playbackQueue.Context?.Key == key) { await ToggleAsync(); return; }
        var items = UserMusicPage;
        StartQueueContext(new(key, state.User.Username, "Сейчас играет из профиля", items?.NextHref,
            LibraryCursor: true, Kind: PlaybackContextKind.Artist), items?.Items.Select(item => item.Track).OfType<SoundCloudTrack>().ToArray() ?? [track], track);
        await PlayAsync(track);
    }
    private async Task SeekUserTrackAsync(SoundCloudTrack track, double fraction)
    {
        if (current?.Id != track.Id) await PlayUserTrackAsync(track);
        await SeekLikedTrackAsync(track, fraction);
    }
}
