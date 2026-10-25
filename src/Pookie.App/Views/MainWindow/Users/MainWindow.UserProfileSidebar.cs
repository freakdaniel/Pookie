using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private long renderedUserSidebarId, renderedUserSidebarGeneration;
    private void RenderUserSidebar(UserProfileState state)
    {
        var likes = state.Sections.GetValueOrDefault(UserSection.Likes)?.Items;
        var followers = state.Sections.GetValueOrDefault(UserSection.Followers)?.Items;
        var following = state.Sections.GetValueOrDefault(UserSection.Following)?.Items;
        var likesPending = state.Loading.Contains(UserSection.Likes) && likes == null;
        var followersPending = state.Loading.Contains(UserSection.Followers) && followers == null;
        var followingPending = state.Loading.Contains(UserSection.Following) && following == null;
        userLikesSection.IsVisible = likesPending || likes?.Any(UserItemVisible) == true;
        userFollowersSection.IsVisible = followersPending || followers is { Length: > 0 };
        userFollowingSection.IsVisible = followingPending || following is { Length: > 0 };
        SetUserLoading(userLikesLoading, likesPending, LoadingRowStyle.Compact, 192);
        SetUserLoading(userFollowersLoading, followersPending, LoadingRowStyle.Compact, 40);
        SetUserLoading(userFollowingLoading, followingPending, LoadingRowStyle.Compact, 172);
        var sameUser = renderedUserSidebarId == state.User.Id && renderedUserSidebarGeneration == navigationGeneration;
        var updateLikes = !sameUser || !ReferenceEquals(likes, renderedUserLikes);
        var updateFollowers = !sameUser || !ReferenceEquals(followers, renderedUserFollowers);
        var updateFollowing = !sameUser || !ReferenceEquals(following, renderedUserFollowing);
        renderedUserSidebarId = state.User.Id; renderedUserSidebarGeneration = navigationGeneration;
        renderedUserLikes = likes; renderedUserFollowers = followers; renderedUserFollowing = following;
        if (updateLikes)
        {
            foreach (var row in userSidebarRows) { BindCompactTrackRow(row, null); compactTrackRows.Remove(row); }
            userSidebarRows.Clear(); userSidebarLikes.Clear();
            foreach (var item in (likes ?? []).Where(UserItemVisible).Take(3))
            {
                if (item.Track is { } track)
                {
                    var row = CreateCompactTrackRow(value => Run(() => PlayUserSidebarTrackAsync(value)));
                    row.Root.Margin = new Thickness(-8, 0, -8, 0);
                    BindCompactTrackRow(row, track); userSidebarRows.Add(row); userSidebarLikes.Add(row.Root);
                }
                else if (item.Playlist != null)
                    userSidebarLikes.Add(TrackButtons.Action(new TextBlock().Text(item.Title).FontSize(13).TextTrimming(TextTrimming.CharacterEllipsis),
                        () => Run(() => OpenLibraryItemAsync(item)), TrackButtons.Text, 40).Padding(0));
            }
        }
        if (updateFollowers)
        {
            userSidebarFollowers.Clear();
            foreach (var user in (followers ?? []).Select(item => item.User).OfType<SoundCloudUser>().Take(7))
            {
                var avatar = UserSidebarAvatar(user, 38, state.User.Id);
                userSidebarFollowers.Add(TrackButtons.Icon(avatar, () => Run(() => OpenUserProfileAsync(user)), TrackButtons.Text, 38).Margin(0, 0, -2, 0).ToolTip(user.Username));
            }
        }
        if (updateFollowing)
        {
            userSidebarFollowing.Clear();
            foreach (var user in (following ?? []).Select(item => item.User).OfType<SoundCloudUser>().Take(3))
            {
                var row = TrackButtons.Action(new Grid().Columns("40,*").Rows("Auto").Spacing(12).Children(
                    UserSidebarAvatar(user, 40, state.User.Id).Column(0),
                    new StackPanel().Vertical().Spacing(4).CenterVertical().Column(1).Children(
                        new TextBlock().Text(user.Username).FontSize(13).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                        new TextBlock().Text(string.Join(" · ", new[] { user.FollowersCount is { } fans ? $"{fans:N0} подписчиков" : null,
                            user.TrackCount is { } count ? $"{count:N0} треков" : null }.Where(value => value != null))).FontSize(11).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis))),
                    () => Run(() => OpenUserProfileAsync(user)), TrackButtons.Text, 52).Padding(0);
                ((FrameworkElement)row.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
                userSidebarFollowing.Add(row);
            }
        }
    }

    private void ClearUserSidebar()
    {
        foreach (var row in userSidebarRows) { BindCompactTrackRow(row, null); compactTrackRows.Remove(row); }
        userSidebarRows.Clear(); userSidebarLikes.Clear(); userSidebarFollowers.Clear(); userSidebarFollowing.Clear();
        renderedUserSidebarId = renderedUserSidebarGeneration = 0;
        renderedUserLikes = renderedUserFollowers = renderedUserFollowing = null;
    }

    private FrameworkElement UserSidebarAvatar(SoundCloudUser user, int size, long profileId)
    {
        var image = new Image().Width(size).Height(size).StretchMode(Stretch.UniformToFill);
        image.Source = Icons.Source("user-circle", Muted);
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await FetchImageAsync(user.AvatarUrl, lifetime.Token);
            if (IsUserProfileCurrent(profileId, generation) && source != null) image.Source = source;
        });
        return new Border().Width(size).Height(size).CornerRadius(size / 2d).ClipToBounds().Child(image);
    }

    private async Task PlayUserSidebarTrackAsync(SoundCloudTrack track)
    {
        if (userProfileState is not { } state) return;
        var key = $"user:{state.User.Id}:likes";
        if (current?.Id == track.Id && playbackQueue.Context?.Key == key) { await ToggleAsync(); return; }
        var data = state.Sections.GetValueOrDefault(UserSection.Likes);
        StartQueueContext(new(key, state.User.Username, "Понравившееся из профиля", data?.NextHref, LibraryCursor: true,
            Kind: Playback.PlaybackContextKind.Artist), data?.Items.Select(item => item.Track).OfType<SoundCloudTrack>().ToArray() ?? [track], track);
        await PlayAsync(track);
    }
}
