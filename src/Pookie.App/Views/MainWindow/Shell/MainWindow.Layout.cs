using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private const double DefaultWindowWidth = 1280;
    private const int StartupBrandFadeDurationMs = 500;
    private const int StartupCurtainSlideDurationMs = 760;

    private Border playerChrome = null!;
    private Border playerContentFrame = null!;
    private Border contentSurface = null!;
    private Border startupBackdrop = null!;
    private Border contentFrame = null!;
    private FrameworkElement startupHeader = null!;
    private FrameworkElement startupContent = null!;
    private ScrollViewer shellScroll = null!;
    private ShellPageScroll shellPageScroll = null!;
    private ShellChromeGlass headerGlass = null!;
    private ContentBottomFade contentBottomFade = null!;
    private StackPanel topNavigation = null!;
    private HeaderSearchField topSearchBar = null!;
    private TextBox topSearchInput = null!;
    private Grid startupSplash = null!;
    private Border startupCurtain = null!;
    private Grid startupBrandLayer = null!;
    private StackPanel startupBrand = null!;
    private PathShape startupLogo = null!;
    private ProgressRing startupSpinner = null!;
    private double startupCurtainOffset = -1;
    private readonly Dictionary<Button, LikedTrackTile> libraryTiles = [];
    private ItemsControl libraryGrid = null!;
    private readonly List<Action> navVisualRefreshers = [];

    private static readonly Color Muted = Color.FromRgb(151, 151, 151);
    private static readonly Color Surface = Color.FromRgb(24, 24, 24);
    private static readonly Color HeaderSurface = Surface;
    private static readonly Color Raised = Color.FromRgb(43, 43, 43);
    private static readonly Color Edge = Color.FromRgb(61, 61, 61);
    private static readonly Color NavHover = Color.FromRgb(192, 192, 192);

    private FrameworkElement Layout()
    {
        // Background for the startup reveal and navigation chrome.
        startupBackdrop = new Border().Background(Surface).Height(96).Top();

        startupHeader = Header();
        // Playback and page scrolling do not change the fixed navigation bar.
        // Descendant invalidation still refreshes hover, text and search edits.
        if (OperatingSystem.IsWindows()) startupHeader.CacheMode = new BitmapCache();
        startupHeader.Opacity = 0;
        startupHeader.IsHitTestVisible = false;

        startupContent = Library();
        VisualTree.Visit(startupContent, element =>
        {
            if (element is ScrollViewer viewer) viewer.CornerRadius = 0;
            if (element is ScrollableItemsBase items) items.CornerRadius = 0;
        });
        startupContent.Opacity = 0;
        startupContent.IsHitTestVisible = false;
        contentFrame = new Border().MaxWidth(1440).Width(DefaultWindowWidth).CenterHorizontal()
            .Padding(36, ShellChromeGlass.HeaderHeight + 30, 36, 0).Child(startupContent);
        shellScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).CornerRadius(0);
        var glassContent = new GlassContentFrame(contentFrame);
        shellPageScroll = new ShellPageScroll(glassContent, shellScroll, PageContentScroll, args =>
        {
            if (smoothScrolls.TryGetValue(shellScroll, out var motion)) return motion.ScrollWheel(args.Delta.Y);
            var before = shellScroll.VerticalOffset;
            shellScroll.SetScrollOffsets(0, before - args.Delta.Y * ThemeManager.DefaultMetrics.ScrollWheelStep);
            return Math.Abs(shellScroll.VerticalOffset - before) > .01;
        }, () => page.Value != Page.Chats ? ContentBottomFade.FadeHeight : 0);
        shellScroll.Content = shellPageScroll;
        homeWaveLayout = new HomeWaveLayout(shellScroll, WaveSettingsView());
        waveSettingsOpen.Changed += () => homeWaveLayout.SetOpen(waveSettingsOpen.Value);
        page.Changed += () =>
        {
            if (page.Value != Page.Home) waveSettingsOpen.Value = false;
            UpdateContentFrameWidth(Window?.ClientSize.Width ?? contentSurface.ActualWidth);
        };
        contentSurface = new Border().Background(Surface).Child(homeWaveLayout);
        headerGlass = new ShellChromeGlass(glassContent, Surface).Height(ShellChromeGlass.HeaderHeight).Top().IsHitTestVisible(false);
        shellScroll.ScrollChanged += UpdateScrolledChrome;
        contentBottomFade = new ContentBottomFade(Surface).Height(ContentBottomFade.FadeHeight).Bottom().IsHitTestVisible(false);
        contentBottomFade.Transitions = [Transition.Create(ContentBottomFade.StrengthProperty, 220, Easing.CubicBezier(.2, 0, 0, 1))];
        contentBottomFade.Bind(ContentBottomFade.StrengthProperty, playerVisible, visible => visible ? ContentBottomFade.PlayerOpacity : 1d);
        // Use the arranged viewport as well as native resize notifications. Some platforms
        // apply programmatic window sizes without delivering ClientSizeChanged first.
        contentSurface.SizeChanged += e =>
        {
            UpdateContentFrameWidth(e.NewSize.Width);
            UpdateLibraryCardSize(e.NewSize.Width);
        };

        var playerBar = PlayerBar();
        playerChrome.SizeChanged += e => playerBackdrop.Height = e.NewSize.Height;
        playerVisible.Changed += () =>
        {
            if (playerVisible.Value) return;
            queueOpen.Value = false;
            foreach (var menu in shellMenus)
                if (ReferenceEquals(menu.State, queueOpen)) menu.Layer.Close(true);
            playerBackdrop.Height = 0;
        };
        workspace = new Grid().Columns("*").Rows("64,*,Auto").Children(
            playerBackdrop.Row(1).RowSpan(2),
            contentSurface.Row(0).RowSpan(2),
            contentBottomFade.Row(0).RowSpan(2),
            headerGlass.Row(0).RowSpan(2),
            startupHeader.Row(0),
            playerBar.Row(2),
            QueuePanel().Row(0).RowSpan(3),
            ProfilePanel().Row(0).RowSpan(3));
        workspace.IsVisible = false;
        workspace.IsEnabled = false;
        workspace.IsHitTestVisible = false;

        // Keep the backdrop and splash in a single-cell root so they always cover
        // the client area from the very first layout pass.
        return new PresentationRoot().Columns("*").Rows("*").Children(
            startupBackdrop.Row(0), workspace.Row(0), LoginScreen().Row(0), StartupSplash().Row(0), ExpandedPlayer().Row(0), PlaylistPickerView().Row(0), ChatAttachMenuView().Row(0), ChatMessageMenuView().Row(0), ChatGifsView().Row(0), ChatMusicPickerView().Row(0), ChatVideoView().Row(0));
    }

    private FrameworkElement StartupSplash()
    {
        startupLogo = Icons.LogoMark(0, 0);
        startupLogo.Opacity = 0;
        startupSpinner = new ProgressRing().Width(34).Height(34).IsActive(true).CenterHorizontal();
        startupSpinner.Opacity = 0;
        startupBrand = new StackPanel().Vertical().Spacing(22).CenterHorizontal().CenterVertical().Children(
            new Border().Width(138).Height(130).CenterHorizontal().CenterVertical()
                .Child(startupLogo.CenterHorizontal().CenterVertical()),
            startupSpinner);
        startupCurtain = new Border().Background(Surface).ClipToBounds();
        // Loader content belongs to the viewport, never to the moving curtain.
        startupBrandLayer = new Grid().Columns("*").Rows("*").Children(startupBrand);
        startupSplash = new Grid().Columns("*").Rows("*").Children(startupCurtain, startupBrandLayer);
        startupSplash.SizeChanged += e =>
        {
            if (startupCurtainOffset >= 0) startupCurtain.Height = Math.Max(0, e.NewSize.Height - startupCurtainOffset);
        };
        return startupSplash;
    }

    private FrameworkElement Header()
    {
        topNavigation = new StackPanel().Horizontal().Spacing(5).CenterHorizontal().CenterVertical().Width(480).Height(44).Children(
            Nav("house-simple", "Главная", Page.Home), Nav("broadcast", "Лента", Page.Feed), Nav("books", "Библиотека", Page.Library), Nav("chat-circle", "Чаты", Page.Chats),
            NavAction("magnifying-glass", "Поиск", OpenTopSearch));
        topSearchInput = new TextBox().BindText(query).Placeholder("Треки, люди, альбомы, плейлисты")
            .Background(Color.Transparent).BorderThickness(0).FontSize(13).Focusable(true)
            .OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; Run(SearchAsync); CloseTopSearch(clear: false); } })
            .OnLostFocus(() => { if (searching.Value) CloseTopSearch(clear: true); });
        topSearchBar = new HeaderSearchField(topSearchInput).Width(0);
        topNavigation.Transitions = [
            Transition.Create(FrameworkElement.WidthProperty, 190, Easing.CubicBezier(0.2, 0, 0, 1)),
            Transition.Create(UIElement.OpacityProperty, 150, Easing.CubicBezier(0.2, 0, 0, 1))];
        topSearchBar.Transitions = [
            Transition.Create(FrameworkElement.WidthProperty, 220, Easing.CubicBezier(0.2, 0, 0, 1)),
            Transition.Create(UIElement.OpacityProperty, 170, Easing.CubicBezier(0.2, 0, 0, 1))];
        topSearchBar.IsHitTestVisible = false;
        topSearchBar.Opacity = 0;

        profileButton = new Button().StyleName("flat-button").Content(new Grid().Columns("*").Rows("*").Width(30).Height(30).Children(
                new Border().Background(Color.FromRgb(65, 65, 65)).CornerRadius(15).ClipToBounds().BindIsVisible(signedIn)
                    .Child(new Grid().Columns("*").Rows("*").Children(
                        new TextBlock().BindText(profileInitial).FontSize(12).Bold().CenterHorizontal().CenterVertical().BindIsVisible(hasAvatar, value => !value),
                        profileAvatar.BindIsVisible(hasAvatar))),
                Icons.View("user-circle", 24).CenterHorizontal().CenterVertical().BindIsVisible(signedIn, value => !value)))
            .Padding(2).Width(34).Height(34).CornerRadius(17)
            .OnClick(() => { profileOpen.Value = !profileOpen.Value; queueOpen.Value = false; }).CenterVertical();
        var settingsGlyph = Icons.View("gear", 24, Muted);
        var subduedSettings = Surface.Lerp(Muted, .84);
        TrackButtons.SetGlyph(settingsGlyph, "gear", subduedSettings, animate: false);
        settingsButton = new Button().StyleName("flat-button").Background(Color.Transparent).BorderThickness(0).Content(new Grid().Columns("*").Rows("*").Children(
                settingsGlyph.CenterHorizontal().CenterVertical()))
            .Padding(2).Width(34).Height(34).CornerRadius(17)
            .OnMouseEnter(() => TrackButtons.SetGlyph(settingsGlyph, "gear", Muted))
            .OnMouseLeave(() => TrackButtons.SetGlyph(settingsGlyph, "gear", page.Value == Page.Settings ? Muted : subduedSettings))
            .OnClick(() => Run(() => ShowSettingsAsync(settingsSection.Value))).CenterVertical();
        page.Changed += () => TrackButtons.SetGlyph(settingsGlyph, "gear", page.Value == Page.Settings || settingsButton.IsMouseOver ? Muted : subduedSettings);

        return new Grid().Columns("Auto,*,Auto").Rows("*").Padding(36, 8, 36, 8).Children(
            new StackPanel().Horizontal().Spacing(16).CenterVertical().Column(0).Children(
                Icons.LogoView(26, 25).CenterVertical(),
                NavigationPill()),
            new Grid().Columns("*").Rows("*").CenterHorizontal().CenterVertical().ClipToBounds().Column(1).Children(
                topNavigation.CenterHorizontal().CenterVertical(), topSearchBar.CenterHorizontal().CenterVertical()),
            new StackPanel().Horizontal().Spacing(5).CenterVertical().Right().Column(2).Children(settingsButton, profileButton));
    }

    private Button Nav(string icon, string label, Page target)
    {
        bool Active(Page value) => value == target || target == Page.Library && IsLibrary(value);
        var normalGlyph = Icons.View(icon, 18, Muted);
        var hoverGlyph = Icons.View(icon, 18, NavHover);
        var activeGlyph = Icons.View(icon, 18, Color.FromRgb(255, 255, 255));
        hoverGlyph.Opacity = 0;
        activeGlyph.Opacity = 0;
        foreach (var glyphLayer in new[] { normalGlyph, hoverGlyph, activeGlyph })
            glyphLayer.Transitions = [Transition.Create(UIElement.OpacityProperty, 250, Easing.CubicBezier(0.2, 0, 0, 1))];
        var glyph = new Grid().Columns("*").Rows("*").Width(18).Height(18).Children(
            normalGlyph.CenterHorizontal().CenterVertical(),
            hoverGlyph.CenterHorizontal().CenterVertical(),
            activeGlyph.CenterHorizontal().CenterVertical());
        var textColor = new ObservableValue<Color>(Active(page.Value) ? Color.FromRgb(255, 255, 255) : Muted);
        var text = new TextBlock().Text(label).FontSize(11).SemiBold()
            .Bind(TextElement.ForegroundProperty, textColor).CenterVertical();
        text.Transitions = [Transition.Create(TextElement.ForegroundProperty, 250, Easing.CubicBezier(0.2, 0, 0, 1))];
        bool hovered = false;
        void Refresh()
        {
            var active = Active(page.Value);
            normalGlyph.Opacity = active || hovered ? 0 : 1;
            hoverGlyph.Opacity = !active && hovered ? 1 : 0;
            activeGlyph.Opacity = active ? 1 : 0;
            textColor.Value = active ? Color.FromRgb(255, 255, 255) : hovered ? NavHover : Muted;
        }
        Refresh();
        navVisualRefreshers.Add(Refresh);
        return new Button().StyleSheet(sectionButtonStyles).StyleName("section-button")
            .Background(Color.Transparent).BorderThickness(0).Padding(11, 0).Height(40).CornerRadius(20)
            .BindIsEnabled(page, value => value != target)
            .Content(new StackPanel().Horizontal().Spacing(6).CenterVertical().Children(glyph.CenterVertical(), text))
            .OnMouseEnter(() => { hovered = true; Refresh(); })
            .OnMouseLeave(() => { hovered = false; Refresh(); })
            .OnClick(() => { if (page.Value != target) Run(() => NavigateAsync(target)); });
    }

    private Button NavAction(string icon, string label, Action action)
    {
        var normalGlyph = Icons.View(icon, 18, Muted);
        var hoverGlyph = Icons.View(icon, 18, NavHover);
        hoverGlyph.Opacity = 0;
        normalGlyph.Transitions = [Transition.Create(UIElement.OpacityProperty, 250, Easing.CubicBezier(0.2, 0, 0, 1))];
        hoverGlyph.Transitions = [Transition.Create(UIElement.OpacityProperty, 250, Easing.CubicBezier(0.2, 0, 0, 1))];
        var glyph = new Grid().Columns("*").Rows("*").Width(18).Height(18).Children(
            normalGlyph.CenterHorizontal().CenterVertical(), hoverGlyph.CenterHorizontal().CenterVertical());
        var textColor = new ObservableValue<Color>(Muted);
        var text = new TextBlock().Text(label).FontSize(11).SemiBold().Bind(TextElement.ForegroundProperty, textColor).CenterVertical();
        text.Transitions = [Transition.Create(TextElement.ForegroundProperty, 250, Easing.CubicBezier(0.2, 0, 0, 1))];
        void SetHovered(bool hovered)
        {
            normalGlyph.Opacity = hovered ? 0 : 1;
            hoverGlyph.Opacity = hovered ? 1 : 0;
            textColor.Value = hovered ? NavHover : Muted;
        }
        return new Button().StyleName("flat-button").Background(Color.Transparent).BorderThickness(0).Padding(11, 0).Height(40).CornerRadius(20)
            .Content(new StackPanel().Horizontal().Spacing(6).CenterVertical().Children(glyph.CenterVertical(), text))
            .OnMouseEnter(() => SetHovered(true))
            .OnMouseLeave(() => SetHovered(false))
            .OnClick(action);
    }

    private void RefreshNavVisuals()
    {
        foreach (var refresh in navVisualRefreshers) refresh();
    }

    private void OpenTopSearch()
    {
        if (searching.Value) return;
        query.Value = "";
        searching.Value = true;
        topNavigation.Width = 0; topNavigation.Opacity = 0; topNavigation.IsHitTestVisible = false;
        topSearchBar.Width = 480; topSearchBar.Opacity = 1; topSearchBar.IsHitTestVisible = true;
        topSearchInput.Focus();
    }

    private void CloseTopSearch(bool clear)
    {
        if (!searching.Value) return;
        searching.Value = false;
        topSearchBar.Width = 0; topSearchBar.Opacity = 0; topSearchBar.IsHitTestVisible = false;
        topNavigation.Width = 480; topNavigation.Opacity = 1; topNavigation.IsHitTestVisible = true;
        if (clear) query.Value = "";
    }

    private FrameworkElement Library()
    {
        var listBody = new Grid().Columns("*").Rows("*").Children(
            HomePageView().BindIsVisible(page, value => value == Page.Home),
            CreateHomeLoadingView().IsVisible(false),
            FeedPageView().BindIsVisible(page, value => value == Page.Feed),
            SearchPage().BindIsVisible(page, value => value == Page.Search),
            ChatsPage().BindIsVisible(page, value => value == Page.Chats),
            TrackDetailPageView().BindIsVisible(page, value => value == Page.Track),
            UserProfilePageView().BindIsVisible(page, value => value == Page.UserProfile),
            LibraryOverview().BindIsVisible(page, value => value == Page.Library),
            LikedTracksPage().BindIsVisible(page, value => value == Page.LibraryTracks),
            CollectionPage().BindIsVisible(page, IsCollectionPage),
            SettingsPageView().BindIsVisible(page, value => value == Page.Settings));
        return new DockPanel().LastChildFill().Spacing(18).Children(
            LibraryTabs().DockTop().BindIsVisible(page, IsLibrary),
            listBody);
    }

    private static TextBlock LikedSectionTitle() => new TextBlock().Text("Понравившиеся треки").FontSize(22).Bold().CenterVertical();

    private FrameworkElement LibraryOverview()
    {
        libraryGrid = CreateLikedGrid(libraryTiles);
        libraryGrid.Height = likedArtworkSize + 90;
        overviewLikesLoadingView = CreateLibraryLoadingView(libraryGrid, preview: true);
        var overview = new StackPanel().Vertical().Spacing(32).Padding(0, 14).Children(
            OverviewSection("Недавно прослушанное", "recent", Page.LibraryHistory),
            new StackPanel().Vertical().Spacing(18).Children(
                SectionHeader("Понравившиеся треки", ShowLibraryTracks), overviewLikesLoadingView.Root,
                overviewLikesEmpty = new TextBlock().Text("Здесь пока ничего нет.")
                    .Foreground(Muted).FontSize(13).TextWrapping(TextWrapping.Wrap).BindIsVisible(hasLibraryTracks, value => !value && !likesLoading)),
            OverviewSection("Плейлисты", "playlists", Page.LibraryPlaylists),
            OverviewSection("Альбомы", "albums", Page.LibraryAlbums),
            OverviewSection("Понравившиеся станции", "stations", Page.LibraryStations),
            OverviewSection("Подписки", "following", Page.LibraryFollowing));
        return overviewScroll = new ScrollViewer().Content(overview).Background(Color.Transparent).BorderThickness(0).Padding(0);
    }

    private void ShowLibraryTracks()
    {
        if (!demo && me != null && libraryLikes == null) { Run(() => LikesAsync(true)); return; }
        likedActionError.Value = "";
        BeginNavigation(new(Page.LibraryTracks));
        if (libraryLikes != null) ReplaceTracks(libraryLikes);
        RefreshNavVisuals();
        eyebrow.Value = "ТВОЯ КОЛЛЕКЦИЯ";
        heading.Value = "Понравившиеся треки";
        RefreshLikedViews();
    }

    private void RefreshLibraryCards()
    {
        hasLibraryTracks.Value = tracks.Count > 0;
        libraryGrid.Items(tracks.Take(6).ToArray(), track => track.Title);
        libraryGrid.Height = Math.Max(1, Math.Ceiling(Math.Min(6, tracks.Count) / (double)libraryColumns)) * (likedArtworkSize + 90);
    }

    private async Task<ImageSource?> GetLibraryArtworkAsync(SoundCloudTrack track)
    {
        if (!libraryCoverCache.TryGetValue(track.Id, out var source))
        {
            source = await SharedArtworkAsync(track.ArtworkUrl ?? track.User?.AvatarUrl);
            if (source == null || disposed) return null;
            libraryCoverCache[track.Id] = source;
        }
        return source;
    }

    private void UpdateLibraryCardSize(double clientWidth) => UpdateLikedGridSize(Math.Min(clientWidth, 1440) - 72);

    private void SelectLibraryTrack(SoundCloudTrack track)
    {
        likedActionError.Value = "";
        if (current?.Id == track.Id) { Run(ToggleAsync); return; }
        SetQueue(track);
        Run(() => PlayAsync(track));
    }

    private FrameworkElement ProfilePanel() => ShellMenu(profileButton,
        FloatingMenuLayer.CreateCard(new StackPanel().Vertical().Spacing(6).Children(
            new StackPanel().Vertical().Spacing(8).Margin(12, 8, 12, 8).Children(
                new TextBlock().Text("Твой профиль").Bold().FontSize(14),
                new TextBlock().BindText(profile).TextWrapping(TextWrapping.Wrap).FontSize(14),
                new TextBlock().BindText(signedIn, value => value ? "SoundCloud подключён" : "Войди, чтобы слушать свою библиотеку и ленту подписок.")
                    .Foreground(Muted).FontSize(12).TextWrapping(TextWrapping.Wrap)),
            MenuChoice("user-circle", "Мой профиль", () => { profileOpen.Value = false; if (me != null) Run(() => OpenUserProfileAsync(me)); }).BindIsVisible(signedIn),
            MenuChoice("user-circle", "Войти в SoundCloud", () => { profileOpen.Value = false; Run(LoginAsync); }).BindIsVisible(signedIn, value => !value),
            MenuChoice("sign-out", "Выйти из SoundCloud", () => { profileOpen.Value = false; Run(LogoutAsync); }).BindIsVisible(signedIn)), 300),
        profileOpen, FloatingMenuPlacement.BelowRight);

    private void OpenIllustrationCredits() =>
        OpenExternalUrl("https://icons8.com", () => status.Value = "Не удалось открыть сайт Icons8");

    private FrameworkElement QueuePanel()
    {
        var close = TrackButtons.Icon(Icons.View("caret-down", 20, PlayerSecondaryText), () => queueOpen.Value = false, TrackButtons.Text, 32);
        var heading = new Grid().Columns("*,32").Rows("*").Spacing(12).Margin(8, 4, 4, 8).DockTop().Children(
            new StackPanel().Vertical().Spacing(5).CenterVertical().Column(0).Children(
                new TextBlock().Text("Очередь").FontSize(20).SemiBold(),
                new TextBlock().BindText(queueStatus).Foreground(PlayerSecondaryText).FontSize(12)
                    .TextTrimming(TextTrimming.CharacterEllipsis)), close.CenterVertical().Column(1));
        var card = new Border().Background(Color.Transparent).BorderThickness(0).Padding(16, 16, 16, 12)
            .Child(new DockPanel().LastChildFill().Spacing(4).Children(heading, queueList));
        var drawer = new PlayerQueueDrawer(queueButton, playerChrome, playerContentFrame, playerBackdrop, card);
        RegisterShellMenu(queueButton, drawer, queueOpen);
        return drawer;
    }

    private static Button IconButton(string name, Action action) => new Button().StyleName("flat-button")
        .Content(Icons.View(name, 20)).Width(34).Height(34).CornerRadius(12).OnClick(action);

    private ListBox CreateTrackList()
    {
        var result = new ListBox().Items(Array.Empty<string>()).ItemHeight(64)
            .Background(Color.Transparent).BorderThickness(0).CornerRadius(12);
        result.ZebraStriping = false;
        result.ItemTemplate = new DelegateTemplate<SoundCloudTrack>(context =>
        {
            var row = CreateCompactTrackRow();
            context.Register("row", row.Root); return row.Root;
        }, (_, track, _, context) =>
        {
            var row = compactTrackRows.First(row => row.Root == context.Get<Grid>("row"));
            BindCompactTrackRow(row, track);
        }, (_, _, _, context) =>
        {
            var row = compactTrackRows.First(row => row.Root == context.Get<Grid>("row"));
            BindCompactTrackRow(row, null);
        });
        return result;
    }
}
