using System.Numerics;
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
    private long firstTrackWheelAfterResize;
    private bool trackResizePaintPending;
    private TaskCompletionSource? trackResizePaint;
    private readonly DispatcherTimer trackResizeCoalescer = new(TimeSpan.FromMilliseconds(1));
    private ShellHeader startupHeader = null!;
    private FrameworkElement startupContent = null!;
    private ScrollViewer shellScroll = null!;
    private ShellPageScroll shellPageScroll = null!;
    private ContentEdgeFade contentTopFade = null!;
    private ContentEdgeFade contentBottomFade = null!;
    private Grid topNavigation = null!;
    private const string TopSearchPlaceholder = "Трек, исполнитель или строка песни";
    private HeaderSearchField topSearchBar = null!;
    private TextBox topSearchInput = null!;
    private TransformDecorator topNavigationMotion = null!;
    private readonly AnimationClock topSearchMotion = new(TimeSpan.FromMilliseconds(320), Easing.Linear);
    private Grid startupSplash = null!;
    private Border startupCurtain = null!;
    private Grid startupBrandLayer = null!;
    private StackPanel startupBrand = null!;
    private PathShape startupLogo = null!;
    private LoadingSpinner startupSpinner = null!;
    private double startupCurtainOffset = -1;
    private readonly Dictionary<Button, LikedTrackTile> libraryTiles = [];
    private ItemsControl libraryGrid = null!;
    private readonly List<Action> navVisualRefreshers = [];

    private static readonly Color Muted = TextAppearance.Secondary;
    private static readonly Color MutedIcon = Color.FromRgb(151, 151, 151);
    private static readonly Color Surface = PopupAppearance.AppBackground;
    private static readonly Color HeaderSurface = Surface;
    private static readonly Color Raised = Color.FromRgb(43, 43, 43);
    private static readonly Color Edge = Color.FromRgb(61, 61, 61);
    private static readonly Color NavInk = Color.FromRgb(200, 200, 204);
    private static readonly Color NavHover = Color.FromRgb(230, 230, 234);
    private static readonly Color NavText = Color.FromArgb(196, 255, 255, 255);
    private static readonly Color NavTextHover = Color.FromArgb(228, 255, 255, 255);

    private FrameworkElement Layout()
    {
        // Background for the startup reveal.
        startupBackdrop = new Border().Background(Surface).Height(96).Top();

        startupHeader = Header();
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
        contentFrame = new Border()
            .Padding(36, ShellHeader.HeaderHeight + 30, 36, 0).Child(startupContent);
        shellScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).CornerRadius(0);
        if (OperatingSystem.IsWindows()) shellScroll.ScrollChanged += ObservePlaylistPointerMotion;
        shellPageScroll = new ShellPageScroll(contentFrame, shellScroll, PageContentScroll, ScrollShellWheel,
            () => page.Value != Page.Chats ? ContentEdgeFade.BottomHeight : 0,
            () => page.Value == Page.Chats ? double.PositiveInfinity : 1440);
        shellScroll.Content = shellPageScroll;
        page.Changed += () =>
        {
            EndTrackResizeCoalescing();
            var inset = page.Value == Page.Chats ? 16 : 36;
            contentFrame.Padding = new Thickness(inset, ShellHeader.HeaderHeight + 30, inset, 0);
            UpdateLibraryCardSize(Window?.ClientSize.Width ?? contentSurface.ActualWidth);
        };
        contentTopFade = new ContentEdgeFade(Surface, fromTop: true) { Strength = .85 };
        contentTopFade.Height(ShellHeader.HeaderHeight + 32).Top().IsHitTestVisible(false);
        // The header's backdrop samples this layer on both the live and replay paths.
        contentSurface = new Border().Background(Surface).Child(
            new Grid().Columns("*").Rows("*").Children(shellScroll, contentTopFade));
        contentBottomFade = new ContentEdgeFade(Surface).Height(ContentEdgeFade.BottomHeight).Bottom().IsHitTestVisible(false);
        contentBottomFade.Transitions = [Transition.Create(ContentEdgeFade.StrengthProperty, 220, Easing.CubicBezier(.2, 0, 0, 1))];
        contentBottomFade.Bind(ContentEdgeFade.StrengthProperty, playerVisible, visible => visible ? ContentEdgeFade.PlayerOpacity : 1d);
        // Use the arranged viewport as well as native resize notifications. Some platforms
        // apply programmatic window sizes without delivering ClientSizeChanged first.
        contentSurface.SizeChanged += e =>
        {
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
            new ShellHeader.BackdropLayer(startupHeader).Row(0),
            playerBar.Row(2),
            QueuePanel().Row(0).RowSpan(3),
            ProfilePanel().Row(0).RowSpan(3));
        workspace.IsVisible = false;
        workspace.IsEnabled = false;
        workspace.IsHitTestVisible = false;

        // Keep the backdrop and splash in a single-cell root so they always cover
        // the client area from the very first layout pass.
        return new PresentationRoot().Columns("*").Rows("*").Children(
            startupBackdrop.Row(0), workspace.Row(0), LoginScreen().Row(0), StartupSplash().Row(0), ExpandedPlayer().Row(0), PlaylistPickerView().Row(0), PlaylistEditorView().Row(0), ShareView().Row(0), ChatAttachMenuView().Row(0), ChatMessageMenuView().Row(0), ChatGifsView().Row(0), ChatMusicPickerView().Row(0), ChatVideoView().Row(0), EmbeddedLoginPanel().Row(0));
    }

    private bool ScrollShellWheel(MouseWheelEventArgs args)
    {
        var immediate = page.Value == Page.Track && firstTrackWheelAfterResize != 0 &&
            System.Diagnostics.Stopwatch.GetElapsedTime(firstTrackWheelAfterResize).TotalMilliseconds < 120;
        firstTrackWheelAfterResize = 0;
        var released = ReleaseTrackResizeCoalescing(invalidate: false);
        bool moved;
        if (smoothScrolls.TryGetValue(shellScroll, out var motion))
            moved = motion.ScrollWheel(args.Delta.Y, startImmediately: immediate);
        else
        {
            var before = shellScroll.VerticalOffset;
            shellScroll.SetScrollOffsets(0, before - args.Delta.Y * ThemeManager.DefaultMetrics.ScrollWheelStep);
            moved = Math.Abs(shellScroll.VerticalOffset - before) > .01;
        }
        if (moved && immediate && trackResizePaintPending) PaintResizedTrackWindow();
        else if (released) Window.InvalidateVisual();
        return moved;
    }

    private FrameworkElement StartupSplash()
    {
        startupLogo = Icons.LogoMark(0, 0);
        startupLogo.Opacity = 0;
        startupSpinner = new LoadingSpinner().Width(34).Height(34).IsActive(true).CenterHorizontal();
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

    private ShellHeader Header()
    {
        var searchTrigger = Nav("magnifying-glass", "Поиск", Page.Search, OpenTopSearch);
        topNavigation = new Grid().Columns("Auto,*,Auto,*,Auto,*,Auto,*,Auto").Rows("*")
            .Width(ShellHeader.NavigationWidth).Height(ShellHeader.NavigationButtonHeight).ClipToBounds().Children(
                Nav("house-simple", "Главная", Page.Home).Column(0), Nav("broadcast", "Лента", Page.Feed).Column(2),
                Nav("books", "Библиотека", Page.Library).Column(4), Nav("chat-circle", "Чаты", Page.Chats).Column(6),
                searchTrigger.Column(8));
        topNavigationMotion = new TransformDecorator { Child = topNavigation };
        topSearchInput = new TextBox().BindText(query).Placeholder(TopSearchPlaceholder)
            .Background(Color.Transparent).BorderThickness(0).FontSize(13).Focusable(false)
            .OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; Run(SearchAsync); CloseTopSearch(clear: false); } })
            .OnLostFocus(() => { if (searching.Value) CloseTopSearch(clear: true); });
        topSearchInput.IsTabStop = false;
        topSearchBar = new HeaderSearchField(topSearchInput, shellTrigger: searchTrigger)
            .Width(ShellHeader.NavigationWidth).IsHitTestVisible(false);
        query.Changed += RefreshSearchLinkPreview;

        profileButton = new Button().StyleName("flat-button").Content(new Grid().Columns("*").Rows("*").Width(30).Height(30).Children(
                new Border().Background(Color.FromRgb(65, 65, 65)).CornerRadius(15).ClipToBounds().BindIsVisible(signedIn)
                    .Child(new Grid().Columns("*").Rows("*").Children(
                        new TextBlock().BindText(profileInitial).FontSize(12).Bold().CenterHorizontal().CenterVertical().BindIsVisible(hasAvatar, value => !value),
                        profileAvatar.BindIsVisible(hasAvatar))),
                Icons.View("user-circle", 24).CenterHorizontal().CenterVertical().BindIsVisible(signedIn, value => !value)))
            .Padding(2).Width(ShellHeader.AccountButtonSize).Height(ShellHeader.AccountButtonSize).CornerRadius(ShellHeader.AccountButtonSize / 2)
            .OnClick(() => { profileOpen.Value = !profileOpen.Value; queueOpen.Value = false; }).CenterVertical();
        var settingsGlyph = Icons.TintableView("gear", 24, out var settingsInk);
        var settings = new HeaderButton(new CornerRadius(ShellHeader.AccountButtonSize / 2))
            .Width(ShellHeader.AccountButtonSize).Height(ShellHeader.AccountButtonSize).CenterVertical();
        var settingsIcon = settings.WithActivationMotion(settingsGlyph, progress => Matrix3x2.CreateRotation(progress * MathF.PI / 2));
        settings.Content(settingsIcon.Center()).OnClick(() => Run(() => ShowSettingsAsync(settingsSection.Value)));
        settings.UseAnimatedInk(settingsInk, Surface.Lerp(NavInk, .85), NavInk, NavInk);
        settingsButton = settings;
        page.Changed += () => settings.SetSelected(page.Value == Page.Settings);

        return new ShellHeader(() => contentSurface,
            new StackPanel().Horizontal().Spacing(12).CenterVertical().Children(
                Icons.LogoView(26, 25).CenterVertical(),
                NavigationPill()),
            new Grid().Columns("*").Rows("*").CenterHorizontal().CenterVertical().ClipToBounds().Children(
                topNavigationMotion.CenterHorizontal().CenterVertical(), topSearchBar.CenterHorizontal().CenterVertical()),
            new StackPanel().Horizontal().Spacing(5).CenterVertical().Children(settingsButton, profileButton),
            () => OperatingSystem.IsWindows() && page.Value == Page.Track,
            () => OperatingSystem.IsWindows() && page.Value == Page.Track && TrackScrollInMotion());
    }

    private Button Nav(string icon, string label, Page target, Action? action = null)
    {
        bool Active(Page value) => value == target || target == Page.Library && IsLibrary(value);
        var glyph = Icons.TintableView(icon, 18, out var ink);
        var text = new TextBlock().Text(label).FontSize(11).SemiBold().CenterVertical();
        var button = new HeaderButton(new CornerRadius(ShellHeader.NavigationButtonHeight / 2)).Padding(11, 0).Height(ShellHeader.NavigationButtonHeight)
            .BindIsEnabled(page, value => action != null || value != target);
        Func<float, Matrix3x2>? activation = target switch
        {
            Page.Home => progress => Matrix3x2.CreateTranslation(0, -3.2f * MathF.Sin(MathF.PI * progress)),
            Page.Feed => progress => Matrix3x2.CreateScale(1 + .18f * MathF.Sin(MathF.PI * progress)),
            Page.Library => progress => Matrix3x2.CreateRotation(-.24f * MathF.Sin(2 * MathF.PI * progress) * (1 - progress)),
            Page.Chats => progress =>
            {
                var bounce = MathF.Sin(2 * MathF.PI * progress) * (1 - progress);
                return Matrix3x2.CreateScale(1 + .14f * bounce, 1 - .14f * bounce) * Matrix3x2.CreateTranslation(0, 2.6f * bounce);
            },
            _ => null
        };
        // The damped motions already ease each swing. Give both phases time to read.
        var damped = target is Page.Library or Page.Chats;
        FrameworkElement iconView = activation == null ? glyph : button.WithActivationMotion(glyph, activation,
            durationMs: damped ? 440 : 300, easing: damped ? Easing.Linear : null);
        button.Content(new StackPanel().Horizontal().Spacing(6).CenterVertical().Children(iconView.CenterVertical(), text));
        button.UseAnimatedInk(ink, NavInk, NavHover, Color.White, (text, NavText, NavTextHover));
        void Refresh() => button.SetSelected(Active(page.Value));
        Refresh();
        navVisualRefreshers.Add(Refresh);
        return button.OnClick(action ?? (() => { if (page.Value != target) Run(() => NavigateAsync(target)); }));
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
        SetTopNavigationInput(false);
        topSearchBar.IsHitTestVisible = true;
        topSearchInput.Placeholder = TopSearchPlaceholder;
        topSearchInput.Focusable = true;
        topSearchInput.IsTabStop = true;
        topSearchInput.Focus();
        AnimateTopSearch(1);
    }

    private void CloseTopSearch(bool clear)
    {
        if (!searching.Value) return;
        searching.Value = false;
        topSearchBar.IsHitTestVisible = false;
        topSearchInput.Placeholder = "";
        topSearchInput.IsTabStop = false;
        if (topSearchInput.IsFocused) Window.FocusManager.ClearFocus();
        topSearchInput.Focusable = false;
        if (clear) query.Value = "";
        AnimateTopSearch(0);
    }

    private void AnimateTopSearch(double target)
    {
        topSearchMotion.Stop();
        var from = topSearchBar.Reveal;
        topSearchMotion.Duration = TimeSpan.FromMilliseconds(Math.Max(1, Math.Abs(target - from) * (target == 1 ? 320 : 250)));
        topSearchMotion.TickCallback = progress => PaintTopSearch(from + (target - from) * progress);
        topSearchMotion.CompletedCallback = Finish;
        PaintTopSearch(from);
        if (from != target) topSearchMotion.Start();
        else Finish();

        void Finish()
        {
            PaintTopSearch(target);
            if (target == 0) SetTopNavigationInput(true);
        }
    }

    private void PaintTopSearch(double reveal)
    {
        var exit = HeaderSearchField.SearchPhase(reveal, 0, .46);
        topNavigationMotion.Transform = Matrix3x2.CreateTranslation(0, (float)(-24 * exit));
        topNavigation.Opacity = 1 - exit;
        topSearchBar.SetReveal(reveal, reveal > 0 || searching.Value);
    }

    private void SetTopNavigationInput(bool enabled)
    {
        topNavigation.IsHitTestVisible = enabled;
        foreach (var button in topNavigation.Children.OfType<Button>())
            button.IsTabStop = button.Focusable = enabled;
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

    private void UpdateLibraryCardSize(double clientWidth)
    {
        // The track page has its own responsive layout. Reflow the hidden
        // library grids at the current width when their page is shown again.
        if (page.Value != Page.Track) UpdateLikedGridSize(Math.Min(clientWidth, 1440) - 72);
    }

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
        var heading = new Grid().Columns("*,32").Rows("*").Spacing(12).Margin(24, 4, 20, 8).DockTop().Children(
            new StackPanel().Vertical().Spacing(5).CenterVertical().Column(0).Children(
                new TextBlock().Text("Очередь").FontSize(20).SemiBold(),
                new TextBlock().BindText(queueStatus).Foreground(PlayerSecondaryText).FontSize(12)
                    .TextTrimming(TextTrimming.CharacterEllipsis)), close.CenterVertical().Column(1));
        var card = new Border().Background(Color.Transparent).BorderThickness(0).Padding(0, 16, 0, 12)
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
