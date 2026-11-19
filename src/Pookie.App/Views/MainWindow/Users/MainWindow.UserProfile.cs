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
    // History entries share profile data. Each entry still owns its tab and scroll position.
    private sealed class CachedProfile(UserProfileState state)
    {
        internal UserProfileState State = state;
        internal DateTimeOffset MetadataChecked;
        internal long MetadataGeneration = -1;
        internal readonly Dictionary<UserSection, string?> FailedCursors = [];
        internal readonly Dictionary<UserSection, ProfileSectionRead> Reads = [];
    }
    private readonly record struct ProfileSectionRead(int Pages, long Revision, DateTimeOffset Checked);
    private CachedProfile? cachedProfile;
    private UserProfileState? userProfileState
    {
        get => cachedProfile?.State;
        set
        {
            if (value == null) cachedProfile = null;
            else if (cachedProfile?.State.User.Id == value.User.Id) cachedProfile.State = value;
            else cachedProfile = new(value);
        }
    }
    private CachedProfile? FindCachedProfile(long userId) => cachedProfile?.State.User.Id == userId ? cachedProfile :
        navigationHistory.Select(entry => entry.Snapshot?.UserProfile).FirstOrDefault(profile => profile?.State.User.Id == userId);

    private UserSection[] VisibleProfileSections => userSection.Value switch
    {
        UserSection.Overview => UserOverviewSources,
        UserSection.About => [UserSection.Tracks],
        _ => [userSection.Value]
    };

    private Task RefreshVisibleProfileAsync()
    {
        if (disposed || page.Value != Page.UserProfile || cachedProfile is not { } profile || api.Session == null || loading == null)
            return Task.CompletedTask;
        var generation = navigationGeneration;
        var token = loading.Token;
        var stale = VisibleProfileSections.Where(section => !profile.State.Loading.Contains(section) &&
            DateTimeOffset.UtcNow - profile.Reads.GetValueOrDefault(section).Checked >= PageFreshness).ToArray();
        var tasks = stale.Select(section => LoadUserSectionAsync(section, generation, token)).ToList();
        if (profile.MetadataGeneration != generation && DateTimeOffset.UtcNow - profile.MetadataChecked >= PageFreshness)
            tasks.Add(LoadUserMetadataAsync(profile.State.User.Id, generation, token));
        return Task.WhenAll(tasks);
    }
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
        var previous = FindCachedProfile(user.Id);
        return NavigateRouteAsync(new(Page.UserProfile, Item: UserItem(user), UserSection: section), async generation =>
        {
            userSection.Value = section; failedUserCursor = null;
            cachedProfile = previous ?? new(new(user, [], [], [], []));
            var sections = VisibleProfileSections;
            var missing = sections.Where(source => source == UserSection.Comments ? cachedProfile.State.Comments == null : !cachedProfile.State.Sections.ContainsKey(source)).ToArray();
            // Reserve every pending section before the first frame, not one request at a time.
            userProfileState = cachedProfile.State with { Loading = new(missing) };
            RenderUserProfile();
            var token = loading!.Token;
            if (demo && api.Session == null) { userProfileState = userProfileState with { Loading = [], MetadataLoading = false, MetadataError = "Профили доступны после входа в SoundCloud" }; RenderUserProfile(); return; }
            var tasks = new List<Task>();
            if (userProfileState.MetadataLoading || userProfileState.MetadataError.Length > 0 || !userProfileState.LinksLoaded) tasks.Add(LoadUserMetadataAsync(user.Id, generation, token));
            foreach (var source in missing) tasks.Add(LoadUserSectionAsync(source, generation, token, scheduled: true));
            tasks.Add(RefreshVisibleProfileAsync());
            Run(LoadFollowingAsync);
            await Task.WhenAll(tasks);
        });
    }

    private async Task LoadUserMetadataAsync(long id, long generation, CancellationToken token)
    {
        var profile = cachedProfile!;
        profile.MetadataGeneration = generation;
        try
        {
            var followersRevision = cachedProfile!.Reads.GetValueOrDefault(UserSection.Followers).Revision;
            var followingRevision = cachedProfile.Reads.GetValueOrDefault(UserSection.Following).Revision;
            var user = await api.GetUserAsync(id, token);
            if (!IsUserProfileCurrent(id, generation)) return;
            if (followersRevision != cachedProfile!.Reads.GetValueOrDefault(UserSection.Followers).Revision)
                user = user with { FollowersCount = cachedProfile.State.User.FollowersCount };
            if (followingRevision != cachedProfile.Reads.GetValueOrDefault(UserSection.Following).Revision)
                user = user with { FollowingsCount = cachedProfile.State.User.FollowingsCount };
            userProfileState = userProfileState! with { User = user, MetadataLoading = false, MetadataError = "" };
            RenderUserProfile();
            try
            {
                var links = await api.GetUserLinksAsync(id, token);
                if (!IsUserProfileCurrent(id, generation)) return;
                if (!profile.State.LinksLoaded || !profile.State.Links.SequenceEqual(links))
                { userProfileState = profile.State with { Links = links, LinksLoaded = true }; RenderUserProfile(); }
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
        finally
        {
            if (IsUserProfileCurrent(id, generation))
            {
                profile.MetadataChecked = DateTimeOffset.UtcNow;
                profile.MetadataGeneration = -1;
            }
        }
    }

    private async Task LoadUserSectionAsync(UserSection section, long generation, CancellationToken token, string? cursor = null, bool scheduled = false)
    {
        if (userProfileState is not { } state || !scheduled && state.Loading.Contains(section)) return;
        var id = state.User.Id;
        var pending = new HashSet<UserSection>(state.Loading) { section };
        var errors = new Dictionary<UserSection, string>(state.Errors); errors.Remove(section);
        userProfileState = state with { Loading = pending, Errors = errors }; RenderUserProfile();
        var profile = cachedProfile!;
        var read = profile.Reads.GetValueOrDefault(section);
        try
        {
            // A refresh replaces the loaded prefix atomically, including removals.
            // Keep the old rows and cursor on screen until every page is available.
            var pagesToRead = cursor == null ? Math.Max(1, read.Pages) : 1;
            var next = cursor;
            var cursors = new HashSet<string>();
            var items = new List<LibraryItem>();
            var comments = new List<UserComment>();
            if (cursor != null)
            {
                if (section == UserSection.Comments) comments.AddRange(state.Comments?.Comments ?? []);
                else items.AddRange(state.Sections.GetValueOrDefault(section)?.Items ?? []);
                cursors.Add(cursor);
            }
            var pagesRead = cursor == null ? 0 : read.Pages;
            for (int i = 0; i < pagesToRead; i++)
            {
                if (section == UserSection.Comments)
                {
                    var result = await api.GetUserCommentsAsync(id, next, token);
                    comments.AddRange(result.Comments);
                    next = next != null && result.Comments.Length == 0 ? null : result.NextHref;
                }
                else
                {
                    var result = await api.GetUserSectionAsync(id, UserSectionApi(section), next, token);
                    items.AddRange(result.Items);
                    next = next != null && result.Items.Length == 0 ? null : result.NextHref;
                }
                if (!IsUserProfileCurrent(id, generation)) return;
                // A confirmed write during this read takes precedence over an older response.
                if (profile.Reads.GetValueOrDefault(section).Revision != read.Revision) return;
                pagesRead++;
                if (next == null || !cursors.Add(next)) { next = null; break; }
            }
            if (section == UserSection.Comments)
                userProfileState = profile.State with { Comments = new(comments.DistinctBy(entry => entry.Comment.Id).ToArray(), next) };
            else
            {
                var pages = new Dictionary<UserSection, LibraryPage>(profile.State.Sections);
                var previous = pages.GetValueOrDefault(section);
                var updated = items.DistinctBy(item => item.Key).ToArray();
                pages[section] = new(previous != null && previous.Items.SequenceEqual(updated) ? previous.Items : updated, next);
                if (section == UserSection.Likes && me?.Id == id)
                    ObserveOwnLikes(previous, pages[section]);
                userProfileState = profile.State with { Sections = pages };
            }
            profile.FailedCursors.Remove(section);
            profile.Reads[section] = read with { Pages = pagesRead, Checked = cursor == null ? DateTimeOffset.UtcNow : read.Checked };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        {
            if (!IsUserProfileCurrent(id, generation)) return;
            if (profile.Reads.GetValueOrDefault(section).Revision != read.Revision) return;
            profile.Reads[section] = read with { Checked = DateTimeOffset.UtcNow };
            profile.FailedCursors[section] = cursor;
            errors = new(userProfileState!.Errors) { [section] = FriendlyError(error).TrimEnd('.') };
            userProfileState = userProfileState with { Errors = errors }; failedUserCursor = cursor;
        }
        finally
        {
            if (IsUserProfileCurrent(id, generation))
            {
                pending = new(userProfileState!.Loading); pending.Remove(section);
                userProfileState = userProfileState with { Loading = pending }; RenderUserProfile();
                if (profile.Reads.GetValueOrDefault(section).Revision != read.Revision && VisibleProfileSections.Contains(section))
                    Run(RefreshVisibleProfileAsync);
            }
        }
    }

    private Task OpenUserSectionAsync(UserSection section) => userProfileState is { } state && userSection.Value != section
        ? OpenUserProfileAsync(state.User, section) : Task.CompletedTask;
    private void CheckUserProfileEnd()
    {
        if (page.Value == Page.UserProfile && userSection.Value == UserSection.Overview) RefreshUserBiographyOverflow();
        if (page.Value != Page.UserProfile || userSection.Value is UserSection.About ||
            userProfileState is not { } state || navigationHistory[navigationIndex].Pending) return;
        if (userSection.Value == UserSection.Overview && !userPublicationHost.IsReady) return;
        var section = userSection.Value;
        var cursor = section == UserSection.Comments ? state.Comments?.NextHref : state.Sections.GetValueOrDefault(section)?.NextHref;
        if (state.Loading.Contains(section) || cursor == null || cursor == failedUserCursor || UserProfileScroll is not { ViewportHeight: > 0 } scroll ||
            SmoothScroll.Maximum(scroll) - scroll.VerticalOffset > Math.Max(160, scroll.ViewportHeight * .5)) return;
        Run(() => LoadUserSectionAsync(section, navigationGeneration, loading!.Token, cursor));
    }
    private void RetryUserProfile()
    {
        if (userProfileState is not { } state) return;
        failedUserCursor = null;
        var generation = navigationGeneration; var token = loading!.Token;
        if (state.MetadataError.Length > 0) Run(() => LoadUserMetadataAsync(state.User.Id, generation, token));
        var sections = userSection.Value == UserSection.Overview ? UserOverviewSources : new[] { userSection.Value };
        foreach (var section in sections.Where(state.Errors.ContainsKey))
            Run(() => LoadUserSectionAsync(section, generation, token, cachedProfile!.FailedCursors.GetValueOrDefault(section)));
    }
    private LibraryPage? UserMusicPage => userProfileState?.Sections.GetValueOrDefault(userSection.Value);
    private async Task PlayUserTrackAsync(SoundCloudTrack track)
    {
        if (page.Value != Page.UserProfile || userProfileState is not { } state) return;
        var source = userSection.Value; var key = $"user:{state.User.Id}:{UserSectionApi(source)}";
        if (IsCurrentTrack(track)) { await ToggleAsync(); return; }
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
