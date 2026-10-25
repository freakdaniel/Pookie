using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void ShowUserProfileError(string message)
    { userNotice.Text = message.TrimEnd('.'); userNotice.IsVisible = true; }
    private void RefreshUserProfilePlayback()
    {
        RefreshFeedPlayback();
        foreach (var (item, icon) in userPlaylistPlayIcons)
            icon.Source = Icons.Source(playbackQueue.Context?.Key == item.Key && isPlaying.Value ? "pause-solid" : "play-solid", TrackButtons.OnPrimary);
    }
    private void RenderUserProfile()
    {
        if (page.Value != Page.UserProfile || userProfileState is not { } state) return;
        var user = state.User; var section = userSection.Value;
        userName.Text = user.Username; userVerified.IsVisible = user.IsVerified;
        userArtistPro.IsVisible = user.HasArtistPro;
        userCountryFlagImage.Source = Icons.CountryFlag(user.CountryCode);
        userCountryFlag.IsVisible = userCountryFlagImage.Source != null;
        if (userCountryFlag.IsVisible) userCountryFlag.ToolTip(UserLocation(user));
        userFullName.Text = user.FullName ?? ""; userFullName.IsVisible = !string.IsNullOrWhiteSpace(user.FullName) && user.FullName != user.Username;
        userFollowerCount.Text = UserMetricCount(user.FollowersCount);
        userFollowingCount.Text = UserMetricCount(user.FollowingsCount);
        userTrackCount.Text = UserMetricCount(user.TrackCount);
        userCopy.IsEnabled = userOpen.IsEnabled = TrackLink(user.PermalinkUrl) != null;
        var hasTracks = user.TrackCount is > 0 || user.TrackCount == null && state.Sections.GetValueOrDefault(UserSection.Tracks)?.Items.Any(item => item.Track != null) == true;
        if (userTracksCounter.IsVisible != hasTracks)
        {
            userTracksCounter.IsVisible = hasTracks;
            userCounters.Columns(hasTracks ? "*,*,*" : "*,*");
        }
        userOverviewScroll.IsVisible = section == UserSection.Overview;
        userAboutScroll.IsVisible = section == UserSection.About;
        userCommentsScroll.IsVisible = section == UserSection.Comments;
        userTrackList.IsVisible = section == UserSection.Tracks;
        userCardGrid.IsVisible = section is not (UserSection.Overview or UserSection.About or UserSection.Tracks or UserSection.Comments);
        userSectionHeading.IsVisible = section is UserSection.Followers or UserSection.Following or UserSection.Comments;
        userSectionHeading.Text = UserSectionTitle(section);
        userDescription.Text = string.IsNullOrWhiteSpace(user.Description) ? "Пользователь пока не добавил описание" : user.Description;
        userSidebarBiography.Text = user.Description ?? "";
        userSidebarAboutSection.IsVisible = !string.IsNullOrWhiteSpace(user.Description);
        userBioHeading.IsVisible = userSidebarAboutSection.IsVisible;
        RefreshUserBiographyOverflow();
        userAboutDetails.Text = string.Join("\n", new[] { user.FullName, UserLocation(user) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        userAboutDetails.IsVisible = userAboutDetails.Text.Length > 0;
        var source = section; var data = state.Sections.GetValueOrDefault(source);
        var visibleItems = (data?.Items ?? []).Where(UserItemVisible).ToArray();
        var itemCount = section == UserSection.Comments ? state.Comments?.Comments.Length ?? 0 : visibleItems.Length;
        var errors = section == UserSection.Overview ? state.Errors.Where(pair => UserOverviewSources.Contains(pair.Key)).Select(pair => UserSectionTitle(pair.Key) + ": " + pair.Value) :
            state.Errors.Where(pair => pair.Key == source).Select(pair => pair.Value);
        userNotice.Text = string.Join("\n", new[] { state.MetadataError }.Concat(errors).Where(value => value.Length > 0));
        userNotice.IsVisible = userNotice.Text.Length > 0;
        userRetry.IsVisible = userNotice.IsVisible;
        var pending = section == UserSection.Overview ? state.Loading.Contains(UserSection.Overview) : state.Loading.Contains(source);
        userSectionLoading.Root.IsVisible = section is not (UserSection.Overview or UserSection.About);
        userSectionLoading.Skeleton.TextOnly = section == UserSection.Comments;
        SetUserLoading(userSectionLoading, pending && itemCount == 0 && userSectionLoading.Root.IsVisible,
            section == UserSection.Tracks ? LoadingRowStyle.Waveform : section == UserSection.Comments ? LoadingRowStyle.Compact : LoadingRowStyle.CardRow, 440);
        userEmpty.IsVisible = !pending && section is not (UserSection.Overview or UserSection.About) && itemCount == 0 && !userNotice.IsVisible;
        userEmpty.Text = section switch
        {
            UserSection.Tracks => "Пользователь пока не опубликовал треки", UserSection.Albums => "Альбомов пока нет",
            UserSection.Playlists => "Открытых плейлистов пока нет", UserSection.Reposts => "Репостов пока нет",
            UserSection.Comments => "Пользователь пока не оставил комментарии",
            UserSection.Likes => "Открытых лайков пока нет", UserSection.Followers => "Подписчиков пока нет", _ => "Подписок пока нет"
        };
        if (section == UserSection.Tracks)
            SetPageItems(userTrackList, (data?.Items ?? []).Select(item => item.Track).OfType<SoundCloudTrack>(), LoadingRowStyle.Waveform);
        else if (userCardGrid.IsVisible) SetPageItems(userCardGrid, visibleItems, LoadingRowStyle.Card);
        if (section == UserSection.Overview)
        {
            RenderUserOverview(state);
            SetUserLoading(userOverviewLoading, pending && itemCount == 0, LoadingRowStyle.Waveform, TrackRowLayout.Stride * 2);
            RenderUserSidebar(state); Run(LoadUserPlaylistActionsAsync);
        }
        RenderUserComments(state); RenderUserLinks(state);
        RefreshFollowingButtons(); LoadUserArtwork(state.User);
    }

    private void RefreshUserBiographyOverflow()
    {
        if (userSidebarBiography.ActualWidth <= 0 || !userSidebarAboutSection.IsVisible) return;
        var clipped = userSidebarBiography.ExtentHeight > 180 + .5;
        userBioMore.IsVisible = clipped;
    }

    private static string UserMetricCount(long? count) => count switch
    {
        null => "—", >= 1_000_000 => (count.Value / 1_000_000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (count.Value / 1_000d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K", _ => count.Value.ToString("N0")
    };

    private static string UserLocation(SoundCloudUser user)
    {
        var country = user.CountryCode;
        if (!string.IsNullOrWhiteSpace(country))
            try { country = new System.Globalization.RegionInfo(country).DisplayName; } catch (ArgumentException) { }
        return string.Join(", ", new[] { user.City, country }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private void RenderUserOverview(UserProfileState state)
    {
        var activity = state.Sections.GetValueOrDefault(UserSection.Overview)?.Items;
        if (activity is { Length: > 0 } && renderedUserId == state.User.Id && ReferenceEquals(activity, renderedUserActivity) && renderedUserPublicationGeneration == navigationGeneration) return;
        var items = (activity ?? []).Where(item => (item.Track != null || item.Playlist != null) && UserItemVisible(item)).ToArray();
        var append = userPublications.Count > 0 && renderedUserId == state.User.Id && renderedUserPublicationGeneration == navigationGeneration &&
            items.Length >= userPublications.Count && items.Take(userPublications.Count).Select(item => item.Key).SequenceEqual(userPublications.Select(entry => entry.Item.Key));
        if (!append) ClearUserOverview();
        renderedUserId = state.User.Id; renderedUserActivity = activity; renderedUserPublicationGeneration = navigationGeneration;
        foreach (var item in items.Skip(userPublications.Count)) userOverviewBody.Add(UserPublication(item, state.User.Id));
        if (items.Length == 0 && !state.Loading.Contains(UserSection.Overview))
            userOverviewBody.Add(new TextBlock().Text("Здесь появятся публикации и репосты пользователя").FontSize(14).Foreground(Muted));
    }

    private void ClearUserOverview()
    {
        foreach (var row in userPublicationRows) { ClearLibraryTrackRow(row); row.TrackArtGlass.Dispose(); likedRows.Remove(row.Root); }
        userPublicationRows.Clear(); userPublications.Clear(); userPublicationLoads.Clear(); userPlaylistPlayIcons.Clear(); userPlaylistActionViews.Clear();
        foreach (var row in userPreviewRows) { BindCompactTrackRow(row, null); compactTrackRows.Remove(row); }
        userPreviewRows.Clear();
        foreach (var cover in userPublicationCovers) StopCardArtwork(cover);
        userPublicationCovers.Clear();
        userOverviewBody.Clear(); renderedUserActivity = null; renderedUserId = 0;
    }
    private void RenderUserLinks(UserProfileState state)
    {
        if (ReferenceEquals(renderedUserLinks, state.Links)) return;
        renderedUserLinks = state.Links; userAboutLinks.Clear(); userSidebarLinks.Clear();
        userSidebarLinks.IsVisible = state.Links.Length > 0;
        var icons = new Dictionary<string, List<Image>>();
        foreach (var host in new[] { userAboutLinks, userSidebarLinks })
            foreach (var link in state.Links)
            {
                var uri = new Uri(link.Url);
                var label = string.IsNullOrWhiteSpace(link.Title) ? uri.Host : link.Title;
                var text = new TextBlock().Text(label!).FontSize(14).Foreground(Color.FromRgb(166, 195, 246)).TextTrimming(TextTrimming.CharacterEllipsis);
                var icon = Icons.View("globe", 14).StretchMode(Stretch.UniformToFill);
                var origin = uri.GetLeftPart(UriPartial.Authority);
                if (!icons.TryGetValue(origin, out var siteIcons)) icons[origin] = siteIcons = [];
                siteIcons.Add(icon);
                var thumbnail = new Border().Width(14).Height(14).CornerRadius(7).ClipToBounds().Child(icon).CenterVertical().Column(0);
                host.Add(TrackButtons.Action(new Grid().Columns("20,*").Rows("Auto").Spacing(8).Children(thumbnail, text.Column(1)),
                    () => OpenExternalUrl(link.Url, () => ShowUserProfileError("Не удалось открыть ссылку")), TrackButtons.Text, 32).Padding(0).Left());
            }
        if (icons.Count > 0) Run(() => LoadUserLinkIconsAsync(state.Links, icons));
    }
    private async Task LoadUserLinkIconsAsync(UserWebProfile[] links, Dictionary<string, List<Image>> icons)
    {
        var loader = descriptionFavicons ??= new(imageDiskCache);
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(icons.Take(32).Select(async site =>
        {
            try
            {
                await gate.WaitAsync(lifetime.Token);
                try
                {
                    if (disposed || !ReferenceEquals(renderedUserLinks, links)) return;
                    var source = await loader.LoadAsync(new Uri(site.Key), lifetime.Token);
                    if (source == null || disposed || !ReferenceEquals(renderedUserLinks, links)) return;
                    foreach (var icon in site.Value) icon.Source = source;
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is HttpRequestException or IOException or ArgumentException or NotSupportedException) { }
        }));
    }
    private void ResetUserLoading()
    {
        foreach (var view in new[] { userOverviewLoading, userSectionLoading, userLikesLoading, userFollowersLoading, userFollowingLoading, userCommentsLoading })
            view.SetLoading(false);
        if (!userArtworkReady) StopCardArtwork(userAvatar);
    }

    private static void SetUserLoading(LibraryLoadingView view, bool loading, LoadingRowStyle style, double height)
    {
        view.SetLoading(loading);
        if (!loading) return;
        view.Skeleton.ConfigureLoadingRow(TrackRowLayout.CoverSize, 3, style);
        view.Skeleton.Height = height;
    }

    // Keep decoded hero images across navigation; the disk cache also survives restarts.
    private readonly Storage.AsyncResourceCache<string, ImageSource> userHeroImages = new(64, ArtworkCacheBudget, ArtworkBytes);
    private Task<ImageSource?> UserHeroImageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<ImageSource?>(null);
        return userHeroImages.GetAsync(url, () => FetchImageAsync(url, lifetime.Token, maximumSide: 2048), lifetime.Token);
    }

    private void LoadUserArtwork(SoundCloudUser user)
    {
        var key = (user.Id, user.AvatarUrl, user.BannerUrl);
        if (userArtworkKey == key && userArtworkGeneration == navigationGeneration) return;
        if (userArtworkKey.Id != user.Id || userArtworkKey.Avatar != user.AvatarUrl)
        { userAvatar.Opacity = 0; SetCardArtwork(userAvatar, null, user.AvatarUrl, "user-circle"); userHeader.Reset(); }
        else if (userArtworkKey.Banner != user.BannerUrl) userHeader.SetBanner(null);
        userArtworkKey = key; userArtworkReady = false;
        var generation = userArtworkGeneration = navigationGeneration;
        var avatarTask = UserHeroImageAsync(user.AvatarUrl);
        var bannerTask = UserHeroImageAsync(user.BannerUrl);
        Run(async () =>
        {
            await Task.WhenAll(LoadAvatar(), LoadBanner());
            if (IsUserProfileCurrent(user.Id, generation) && userArtworkKey == key) userArtworkReady = true;

            async Task LoadAvatar()
            {
                var avatar = await avatarTask;
                if (!IsUserProfileCurrent(user.Id, generation) || userArtworkKey != key) return;
                SetCardArtwork(userAvatar, avatar, user.AvatarUrl, "user-circle", finished: true); userAvatar.Opacity = 1;
                if (avatar == null) return;
                var palette = await playerPalettes.GetValue(avatar, image => Task.Run(() => PlayerPalette.FromArtwork(image), lifetime.Token));
                if (IsUserProfileCurrent(user.Id, generation) && userArtworkKey == key) userHeader.SetPalette(palette);
            }
            async Task LoadBanner()
            {
                var banner = await bannerTask;
                if (IsUserProfileCurrent(user.Id, generation) && userArtworkKey == key) userHeader.SetBanner(banner);
            }
        });
    }
}
