using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Image userAvatar = null!, userBanner = null!;
    private TextBlock userName = null!, userFullName = null!, userNotice = null!, userSectionHeading = null!;
    private TextBlock userFollowerCount = null!, userFollowingCount = null!, userTrackCount = null!;
    private Button userRetry = null!, userCopy = null!, userOpen = null!;
    private FrameworkElement userHero = null!, userOverview = null!, userAbout = null!, userTabs = null!;
    private FrameworkElement userVerified = null!, userArtistPro = null!;
    private Border userCountryFlag = null!;
    private Image userCountryFlagImage = null!;
    private Grid userCounters = null!;
    private Button userTracksCounter = null!, userBioMore = null!;
    private TextBlock userBioHeading = null!;
    private ItemsControl userTrackList = null!, userCardGrid = null!;
    private ScrollViewer userOverviewScroll = null!, userAboutScroll = null!;
    private StackPanel userOverviewBody = null!, userAboutLinks = null!, userSidebarLinks = null!;
    private DescriptionTextBlock userDescription = null!;
    private DescriptionLinks userDescriptionLinks = null!;
    private LibraryLoadingView userOverviewLoading = null!, userSectionLoading = null!;
    private LibraryLoadingView userLikesLoading = null!, userFollowersLoading = null!, userFollowingLoading = null!, userCommentsLoading = null!;
    private UserProfileHeader userHeader = null!;
    private TextBlock userEmpty = null!, userAboutDetails = null!;
    private readonly List<CompactTrackRow> userPreviewRows = [];
    private readonly List<LikedTrackRow> userPublicationRows = [];
    private readonly List<(LibraryItem Item, FrameworkElement Root)> userPublications = [];
    private StackPanel userSidebarLikes = null!, userSidebarFollowers = null!, userSidebarFollowing = null!;
    private FrameworkElement userLikesSection = null!, userFollowersSection = null!, userFollowingSection = null!, userSidebarAboutSection = null!;
    private LibraryItem[]? renderedUserActivity, renderedUserLikes, renderedUserFollowers, renderedUserFollowing;
    private readonly List<CompactTrackRow> userSidebarRows = [];
    private DescriptionLinks userBioLinks = null!;
    private long renderedUserId;
    private (long Id, string? Avatar, string? Banner) userArtworkKey;
    private UserWebProfile[]? renderedUserLinks;
    private bool userArtworkReady;
    private long userArtworkGeneration;

    private FrameworkElement UserProfilePageView()
    {
        userAvatar = new Image().StretchMode(Stretch.UniformToFill);
        userAvatar.Transitions = [Transition.Create(UIElement.OpacityProperty, 240)];
        userBanner = new Image().StretchMode(Stretch.UniformToFill).IsHitTestVisible(false);
        var avatar = new Border().Width(144).Height(144).CornerRadius(72).ClipToBounds().Child(ArtworkLayer(userAvatar));
        userName = new TextBlock().FontSize(34).Bold().TextTrimming(TextTrimming.CharacterEllipsis);
        userVerified = new Grid().Columns("*").Rows("*").Width(28).Height(28).Children(
            Icons.View("seal-fill", 28, Color.FromRgb(111, 183, 255)).Center(),
            Icons.View("check", 13, Color.FromRgb(15, 39, 64)).Center()).ToolTip("Подтверждённый пользователь");
        userCountryFlagImage = new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.StretchMode(Stretch.UniformToFill);
        userCountryFlag = new Border().Width(24).Height(18).CornerRadius(3).ClipToBounds()
            .BorderBrush(Color.FromArgb(48, 255, 255, 255)).BorderThickness(.5).Child(userCountryFlagImage);
        var proColor = Color.FromRgb(245, 205, 151);
        userArtistPro = new Border().CornerRadius(8).Padding(8, 4).Left()
            .Background(Color.FromArgb(120, 59, 42, 22)).BorderBrush(Color.FromArgb(45, 255, 205, 143)).BorderThickness(1)
            .Child(new StackPanel().Horizontal().Spacing(5).Children(Icons.View("crown-simple-fill", 13, proColor).CenterVertical(),
                new TextBlock().Text("Artist Pro").FontSize(11).SemiBold().Foreground(proColor).CenterVertical()))
            .ToolTip("Подписка SoundCloud Artist Pro");
        userFullName = new TextBlock().FontSize(15).Foreground(TrackButtons.OnSurface).TextTrimming(TextTrimming.CharacterEllipsis);
        userFollowerCount = new TextBlock(); userFollowingCount = new TextBlock(); userTrackCount = new TextBlock();
        Button Counter(TextBlock value, string title, UserSection section)
        {
            value.FontSize(17).Bold().TextTrimming(TextTrimming.CharacterEllipsis);
            var content = new StackPanel().Vertical().Spacing(5).Children(value,
                new TextBlock().Text(title).FontSize(10).Foreground(Muted));
            var button = TrackButtons.Action(content, () => Run(() => OpenUserSectionAsync(section)), TrackButtons.Tonal, 74)
                .CornerRadius(12).Padding(10, 14);
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            ((MaterialTrackButton)button).Palette(Raised, TrackButtons.OnSurface);
            return button;
        }
        userTracksCounter = Counter(userTrackCount, "Треков", UserSection.Tracks).Column(2);
        userCounters = new Grid().Columns("*,*,*").Rows("Auto").Spacing(8).Children(Counter(userFollowerCount, "Подписчиков", UserSection.Followers).Column(0),
            Counter(userFollowingCount, "Подписок", UserSection.Following).Column(1), userTracksCounter);
        var follow = ArtistFollowButton(() => userProfileState?.User, TrackButtons.Glass, 42, withLabel: true);
        userCopy = TrackButtons.Icon(Icons.View("copy", 20), () =>
        { if (TrackLink(userProfileState?.User.PermalinkUrl) is { } uri) CopyText(uri.AbsoluteUri); }, TrackButtons.Glass, 42);
        userOpen = TrackButtons.Icon(Icons.View("arrow-square-out", 20), () =>
        { if (userProfileState?.User.PermalinkUrl is { } url) OpenExternalUrl(url, () => ShowUserProfileError("Не удалось открыть профиль в браузере")); }, TrackButtons.Glass, 42);
        var actions = new StackPanel().Horizontal().Spacing(10).Children(follow, userCopy, userOpen);
        var identity = new StackPanel().Vertical().Spacing(7).Children(
            new UserIdentityLine(userName.MaxWidth(600), userVerified, userCountryFlag).Initialize().Left(),
            userArtistPro, userFullName, actions.Margin(0, 10, 0, 0)).CenterVertical();
        var heroContent = new Grid().Columns("144,*").Rows("Auto").Spacing(24).Padding(28)
            .Children(avatar.Column(0).CenterVertical(), identity.Column(1));
        userHeader = new UserProfileHeader(heroContent, userBanner).Initialize();
        userHeaderGlass = new CoverGlassBackdrop(userBanner, background: userHeader.BackgroundLayer);
        userHeader.BackgroundChanged += userHeaderGlass.Invalidate;
        foreach (var button in new[] { follow, userCopy, userOpen }.OfType<MaterialTrackButton>()) button.AttachGlassBackdrop(userHeaderGlass);
        userHero = new CardShadow(new Border().Background(Raised).CornerRadius(20).ClipToBounds().Child(userHeader))
            .Margin(-12, -8, -12, -12);
        userTabs = new StackPanel().Horizontal().Spacing(22).Padding(8, 0).Children(
            Enum.GetValues<UserSection>().Where(section => section <= UserSection.About).Select(section => (Element)SectionTab(
                UserSectionTitle(section), userSection, section, () => Run(() => OpenUserSectionAsync(section)))).ToArray());
        userSectionHeading = new TextBlock().FontSize(22).Bold();
        userNotice = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        userRetry = TrackButtons.Action(new TextBlock().Text("Повторить").FontSize(13), RetryUserProfile, TrackButtons.Tonal, 32).Left();
        userEmpty = new TextBlock().FontSize(14).Foreground(Muted).Margin(8, 16).TextWrapping(TextWrapping.Wrap);
        userTrackList = CreateUserTrackList(); userCardGrid = CreateCollectionGrid(false);
        userOverviewBody = new ViewportStackPanel { Viewport = () => userOverviewScroll.Bounds }.Vertical().Spacing(36);
        userOverviewLoading = CreateLibraryLoadingView(userOverviewBody, preview: true);
        userOverviewLoading.Skeleton.Preview = false;
        var activity = new StackPanel().Vertical().Spacing(36).Children(
            new TextBlock().Text("Последняя активность").FontSize(21).Bold(), userOverviewLoading.Root);
        var biography = new DescriptionTextBlock { FontSize = 13, Foreground = Muted, MaxHeight = 180 };
        userBioLinks = new(biography, uri => OpenExternalUrl(uri.AbsoluteUri, () => ShowUserProfileError("Не удалось открыть ссылку")))
        { LoadIcon = demo ? null : (uri, token) => (descriptionFavicons ??= new(imageDiskCache)).LoadAsync(uri, token) };
        userSidebarBiography = biography;
        userSidebarLinks = new StackPanel().Vertical().Spacing(6);
        userSidebarLikes = new StackPanel().Vertical().Spacing(6);
        userSidebarFollowers = new StackPanel().Horizontal().Spacing(0);
        userSidebarFollowing = new StackPanel().Vertical().Spacing(8);
        userLikesLoading = CreateLibraryLoadingView(userSidebarLikes, preview: true);
        userFollowersLoading = CreateLibraryLoadingView(userSidebarFollowers, preview: true);
        userFollowingLoading = CreateLibraryLoadingView(userSidebarFollowing, preview: true);
        foreach (var view in new[] { userLikesLoading, userFollowersLoading, userFollowingLoading }) view.Skeleton.Preview = false;
        userFollowersLoading.Skeleton.AvatarStrip = true;
        userLikesSection = UserSidebarSection("Лайки", userLikesLoading.Root, UserSection.Likes);
        userFollowersSection = UserSidebarSection("Подписчики", userFollowersLoading.Root, UserSection.Followers);
        userFollowingSection = UserSidebarSection("Подписки", userFollowingLoading.Root, UserSection.Following);
        userBioHeading = new TextBlock().Text("О пользователе").FontSize(18).Bold().IsVisible(false);
        userBioMore = TrackButtons.Action(new TextBlock().Text("Подробнее").FontSize(12), () => Run(() => OpenUserSectionAsync(UserSection.About)), TrackButtons.Text, 28).Padding(0).Left().IsVisible(false);
        biography.SizeChanged += _ => RefreshUserBiographyOverflow();
        userSidebarAboutSection = new StackPanel().Vertical().Spacing(12).Children(userBioHeading, biography, userBioMore);
        userSidebarComments = new StackPanel().Vertical().Spacing(18);
        userCommentsLoading = CreateLibraryLoadingView(userSidebarComments, preview: true);
        userCommentsLoading.Skeleton.Preview = false; userCommentsLoading.Skeleton.TextOnly = true;
        userCommentsSection = UserSidebarSection("Последние комментарии", userCommentsLoading.Root, UserSection.Comments);
        userCommentsBody = new ViewportStackPanel { Viewport = () => userCommentsScroll.Bounds }.Vertical().Spacing(24);
        userCommentsScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(8, 0, 8, 0).Content(userCommentsBody);
        var sidebar = new StackPanel().Vertical().Spacing(26).Children(userCounters, userSidebarAboutSection, userSidebarLinks,
            userLikesSection, userFollowersSection, userFollowingSection, userCommentsSection);
        userOverview = new TrackPageColumns(activity, sidebar).Initialize();
        userOverviewScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).Margin(-12, 0, -12, 0).Content(userOverview);
        userDescription = new DescriptionTextBlock { FontSize = 14, FontWeight = FontWeight.Normal, Foreground = Muted };
        userDescriptionLinks = new(userDescription, uri => OpenExternalUrl(uri.AbsoluteUri, () => ShowUserProfileError("Не удалось открыть ссылку")))
        { LoadIcon = demo ? null : (uri, token) => (descriptionFavicons ??= new(imageDiskCache)).LoadAsync(uri, token) };
        userAboutDetails = new TextBlock().FontSize(14).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        userAboutLinks = new StackPanel().Vertical().Spacing(10);
        userAbout = new StackPanel().Vertical().Spacing(22).MaxWidth(900).Left().Children(userAboutDetails, userDescription, userAboutLinks);
        userAboutScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(8, 0, 8, 0).Content(userAbout);
        userSectionLoading = CreateLibraryLoadingView(new Grid().Columns("*").Rows("*").Children(userCommentsScroll, userTrackList, userCardGrid));
        return new DockPanel().LastChildFill().Spacing(22).Children(userHero.DockTop(), userTabs.DockTop(),
            userSectionHeading.DockTop(), new StackPanel().Vertical().Spacing(8).Children(userNotice, userRetry).DockTop(),
            new Grid().Columns("*").Rows("*").Children(userOverviewScroll, userAboutScroll, userSectionLoading.Root, userEmpty.Top()));
    }

    private DescriptionTextBlock userSidebarBiography = null!;
    private FrameworkElement UserSidebarSection(string title, FrameworkElement content, UserSection section) =>
        new StackPanel().Vertical().Spacing(12).Children(SectionHeader(title, () => Run(() => OpenUserSectionAsync(section))), content);

    private ItemsControl CreateUserTrackList()
    {
        var list = new ItemsControl().Items(Array.Empty<SoundCloudTrack>(), track => track.Title).ItemHeight(TrackRowLayout.Stride).FixedHeightPresenter()
            .Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0, 0, 0, TrackRowLayout.Gap));
        list.ItemTemplate = new DelegateTemplate<SoundCloudTrack>(context =>
        {
            var row = CreateUserTrackRow();
            context.Register("row", row.Root); return row.Root;
        }, (_, track, _, context) => BindLibraryTrackRow(likedRows[context.Get<Grid>("row")], track),
        (_, _, _, context) => ClearLibraryTrackRow(likedRows[context.Get<Grid>("row")]));
        return list;
    }
}
