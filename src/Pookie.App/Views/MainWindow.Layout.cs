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
    private StackPanel topNavigation = null!;
    private Border topSearchBar = null!;
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
    private static readonly Color HeaderSurface = Color.FromRgb(34, 34, 34);
    private static readonly Color Raised = Color.FromRgb(43, 43, 43);
    private static readonly Color Edge = Color.FromRgb(61, 61, 61);
    private static readonly Color NavHover = Color.FromRgb(192, 192, 192);

    private FrameworkElement Layout()
    {
        // This strip sits behind the top bar and rounded content corners only.
        // It fades into the normal chrome color while the splash curtain moves.
        startupBackdrop = new Border().Background(Surface).Height(96).Top();

        startupHeader = Header();
        startupHeader.Opacity = 0;
        startupHeader.IsHitTestVisible = false;

        startupContent = Library();
        startupContent.Opacity = 0;
        startupContent.IsHitTestVisible = false;
        contentFrame = new Border().MaxWidth(1440).Width(DefaultWindowWidth).CenterHorizontal()
            .Padding(36, 30).Child(startupContent);
        contentSurface = new Border().Background(Surface).CornerRadius(new CornerRadius(30, 30, 0, 0))
            .Margin(0, 8, 0, 0).Child(contentFrame);
        // Use the arranged viewport as well as native resize notifications. Some platforms
        // apply programmatic window sizes without delivering ClientSizeChanged first.
        contentSurface.SizeChanged += e =>
        {
            UpdateContentFrameWidth(e.NewSize.Width);
            UpdateLibraryCardSize(e.NewSize.Width);
        };

        var playerBar = PlayerBar();
        playerChrome.SizeChanged += e =>
        {
            var reveal = Math.Clamp(e.NewSize.Height / PlayerBarHeight, 0, 1);
            contentSurface.CornerRadius(new CornerRadius(30, 30, 30 * reveal, 30 * reveal));
            playerBackdrop.Height = e.NewSize.Height + 30 * reveal;
        };
        playerVisible.Changed += () =>
        {
            if (playerVisible.Value) return;
            playerBackdrop.Height = 0;
            contentSurface.CornerRadius(new CornerRadius(30, 30, 0, 0));
            contentSurface.Margin = new Thickness(0, 8, 0, 0);
        };
        workspace = new Grid().Columns("*").Rows("56,*,Auto").Children(
            playerBackdrop.Row(1).RowSpan(2),
            startupHeader.Row(0),
            contentSurface.Row(1),
            playerBar.Row(2),
            QueuePanel().Row(1),
            ProfilePanel().Row(0).RowSpan(3),
            SettingsPanel().Row(0).RowSpan(3));
        workspace.IsVisible = false;
        workspace.IsEnabled = false;
        workspace.IsHitTestVisible = false;

        // Keep the backdrop and splash in a single-cell root so they always cover
        // the client area from the very first layout pass.
        return new Grid().Columns("*").Rows("*").Children(
            startupBackdrop.Row(0), workspace.Row(0), LoginScreen().Row(0), StartupSplash().Row(0));
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
        startupCurtain = new Border().Background(Surface).CornerRadius(new CornerRadius(30, 30, 0, 0)).ClipToBounds();
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
        topNavigation = new StackPanel().Horizontal().Spacing(5).CenterHorizontal().CenterVertical().Width(390).Height(44).Children(
            Nav("house-simple", "Главная", Page.Home), Nav("broadcast", "Лента", Page.Feed), Nav("books", "Библиотека", Page.Library),
            NavAction("magnifying-glass", "Поиск", OpenTopSearch));
        topSearchInput = new TextBox().BindText(query).Placeholder("Найти трек или исполнителя")
            .Background(Color.Transparent).BorderThickness(0).FontSize(13).Focusable(true)
            .OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; Run(SearchAsync); CloseTopSearch(clear: false); } })
            .OnLostFocus(() => { if (searching.Value) CloseTopSearch(clear: true); });
        topSearchBar = new Border().Background(Color.FromRgb(44, 44, 44)).BorderThickness(0)
            .CornerRadius(15).Padding(11, 6).Width(0).Height(40).CenterVertical().ClipToBounds()
            .Child(new Grid().Columns("Auto,*").Rows("*").Spacing(8).Children(
                Icons.View("magnifying-glass", 18).CenterVertical().Column(0), topSearchInput.CenterVertical().Column(1)));
        topNavigation.Transitions = [
            Transition.Create(FrameworkElement.WidthProperty, 190, Easing.CubicBezier(0.2, 0, 0, 1)),
            Transition.Create(UIElement.OpacityProperty, 150, Easing.CubicBezier(0.2, 0, 0, 1))];
        topSearchBar.Transitions = [
            Transition.Create(FrameworkElement.WidthProperty, 220, Easing.CubicBezier(0.2, 0, 0, 1)),
            Transition.Create(UIElement.OpacityProperty, 170, Easing.CubicBezier(0.2, 0, 0, 1))];
        topSearchBar.IsHitTestVisible = false;
        topSearchBar.Opacity = 0;

        var profileButton = new Button().StyleName("flat-button").Content(new Grid().Columns("*").Rows("*").Width(30).Height(30).Children(
                new Border().Background(Color.FromRgb(65, 65, 65)).CornerRadius(15).ClipToBounds().BindIsVisible(signedIn)
                    .Child(new Grid().Columns("*").Rows("*").Children(
                        new TextBlock().BindText(profileInitial).FontSize(12).Bold().CenterHorizontal().CenterVertical().BindIsVisible(hasAvatar, value => !value),
                        profileAvatar.BindIsVisible(hasAvatar))),
                Icons.View("user-circle", 24).CenterHorizontal().CenterVertical().BindIsVisible(signedIn, value => !value)))
            .Padding(2).Width(34).Height(34).CornerRadius(17)
            .OnClick(() => { profileOpen.Value = !profileOpen.Value; settingsOpen.Value = false; queueOpen.Value = false; }).CenterVertical();
        var settingsGlyph = Icons.View("gear", 24, Muted);
        settingsGlyph.Transitions = [Transition.Create(UIElement.OpacityProperty, 150, Easing.CubicBezier(0.2, 0, 0, 1))];
        settingsGlyph.Opacity = 0.84;
        var settingsButton = new Button().StyleName("flat-button").Background(Color.Transparent).BorderThickness(0).Content(new Grid().Columns("*").Rows("*").Children(
                settingsGlyph.CenterHorizontal().CenterVertical()))
            .Padding(2).Width(34).Height(34).CornerRadius(17)
            .OnMouseEnter(() => settingsGlyph.Opacity = 1)
            .OnMouseLeave(() => settingsGlyph.Opacity = 0.84)
            .OnClick(() => { settingsOpen.Value = !settingsOpen.Value; profileOpen.Value = false; queueOpen.Value = false; }).CenterVertical();

        return new Grid().Columns("Auto,*,Auto").Rows("*").Padding(36, 8, 36, 0).Children(
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
        return new Button().StyleName("flat-button").Background(Color.Transparent).BorderThickness(0).Padding(11, 8).CornerRadius(13)
            .Content(new StackPanel().Horizontal().Spacing(6).CenterVertical().Children(glyph.CenterVertical(), text))
            .OnMouseEnter(() => { hovered = true; Refresh(); })
            .OnMouseLeave(() => { hovered = false; Refresh(); })
            .OnClick(() => Run(() => NavigateAsync(target)));
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
        return new Button().StyleName("flat-button").Background(Color.Transparent).BorderThickness(0).Padding(11, 8).CornerRadius(13)
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
        topSearchBar.Width = 390; topSearchBar.Opacity = 1; topSearchBar.IsHitTestVisible = true;
        topSearchInput.Focus();
    }

    private void CloseTopSearch(bool clear)
    {
        if (!searching.Value) return;
        searching.Value = false;
        topSearchBar.Width = 0; topSearchBar.Opacity = 0; topSearchBar.IsHitTestVisible = false;
        topNavigation.Width = 390; topNavigation.Opacity = 1; topNavigation.IsHitTestVisible = true;
        if (clear) query.Value = "";
    }

    private FrameworkElement Library()
    {
        var pageHeading = new Grid().Columns("*,Auto").Rows("Auto").Children(
            new StackPanel().Vertical().Spacing(5).Column(0).Children(
                new TextBlock().BindText(eyebrow).FontSize(11).Foreground(Muted),
                new TextBlock().BindText(heading).FontSize(30).Bold()),
            new Border().Background(Raised).CornerRadius(18).Padding(16, 10).CenterVertical().Column(1)
                .Child(new StackPanel().Horizontal().Spacing(8).Children(Icons.View("headphones", 18),
                    new TextBlock().Text("Твоя музыка. Твой ритм.").Foreground(Muted).FontSize(12).CenterVertical())))
            .BindIsVisible(page, value => !IsLibrary(value));
        var chips = new StackPanel().Horizontal().Spacing(8).BindIsVisible(page, value => value == Page.Home).Children(
            SearchChip("Для спокойного вечера", "ambient"), SearchChip("Электроника", "electronic"),
            SearchChip("Хип-хоп", "hip hop"), SearchChip("В ритме джаза", "jazz"));
        var listHead = new StackPanel().Vertical().Spacing(10).Children(
            new TextBlock().BindText(status).Foreground(Muted).FontSize(12).TextWrapping(TextWrapping.Wrap)
                .BindIsVisible(page, value => value is Page.Home or Page.Feed),
            new Grid().Columns("44,48,*,64").Rows("Auto").Spacing(12).Padding(12, 0).Children(
                new TextBlock().Text("#").FontSize(11).Foreground(Muted).Column(0),
                new TextBlock().Text("ТРЕК / ИСПОЛНИТЕЛЬ").FontSize(11).Foreground(Muted).Column(2),
                new TextBlock().Text("ВРЕМЯ").FontSize(11).Foreground(Muted).Right().Column(3)))
            .BindIsVisible(page, value => value is Page.Home or Page.Feed);
        var listBody = new Grid().Columns("*").Rows("*").Children(
            list.BindIsVisible(page, value => value is Page.Home or Page.Feed),
            LibraryOverview().BindIsVisible(page, value => value == Page.Library),
            LikedTracksPage().BindIsVisible(page, value => value == Page.LibraryTracks),
            CollectionPage().BindIsVisible(page, value => value > Page.LibraryTracks));
        return new DockPanel().LastChildFill().Spacing(18).Children(
            new StackPanel().Vertical().Spacing(18).DockTop().Children(LibraryTabs(), pageHeading, chips, listHead),
            new Grid().Columns("*,Auto").Rows("Auto").DockBottom().Children(
                new TextBlock().BindText(listStatus).Foreground(Muted).FontSize(12).CenterVertical().Column(0),
                new Button().StyleName("flat-button").Content("Показать ещё").BindIsVisible(hasMore).OnClick(() => Run(MoreAsync)).Column(1))
                .BindIsVisible(page, value => value is Page.Home or Page.Feed),
            listBody);
    }

    private static TextBlock LikedSectionTitle() => new TextBlock().Text("Понравившиеся треки").FontSize(22).Bold().CenterVertical();

    private FrameworkElement LibraryOverview()
    {
        libraryGrid = CreateLikedGrid(libraryTiles);
        libraryGrid.Height = likedArtworkSize + 90;
        var overview = new StackPanel().Vertical().Spacing(32).Padding(0, 14).Children(
            OverviewSection("Недавно прослушанное", "recent", Page.LibraryHistory),
            new StackPanel().Vertical().Spacing(18).Children(
                SectionHeader("Понравившиеся треки", ShowLibraryTracks), libraryGrid,
                new TextBlock().BindText(status, value => value.StartsWith("Воспроизведение полного доступного потока", StringComparison.Ordinal) ? "" : value)
                    .Foreground(Muted).FontSize(13).TextWrapping(TextWrapping.Wrap).BindIsVisible(hasLibraryTracks, value => !value)),
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
            await coverGate.WaitAsync(lifetime.Token);
            try
            {
                if (!libraryCoverCache.TryGetValue(track.Id, out source))
                {
                    source = await FetchLargeArtworkAsync(track, lifetime.Token);
                    if (source == null) return null;
                    if (libraryCoverCache.Count >= 256) libraryCoverCache.Remove(libraryCoverCache.Keys.First());
                    libraryCoverCache[track.Id] = source;
                }
            }
            finally { coverGate.Release(); }
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

    private Button SearchChip(string label, string search) => new Button().StyleName("flat-button").Content(label)
        .Background(Raised).Padding(14, 9).CornerRadius(16).FontSize(12)
        .OnClick(() => { query.Value = search; Run(SearchAsync); });

    private FrameworkElement ProfilePanel() => new Border().Background(Raised).BorderBrush(Edge).BorderThickness(1)
        .CornerRadius(18).Width(300).Padding(20).Top().Right().Margin(0, 64, 28, 0).BindIsVisible(profileOpen)
        .Child(new StackPanel().Vertical().Spacing(14).Children(
            new Grid().Columns("*,Auto").Rows("Auto").Children(
                new TextBlock().Text("Твой профиль").Bold().FontSize(16).CenterVertical().Column(0),
                IconButton("x", () => profileOpen.Value = false).Column(1)),
            new TextBlock().BindText(profile).TextWrapping(TextWrapping.Wrap).FontSize(14),
            new TextBlock().BindText(signedIn, value => value ? "SoundCloud подключён" : "Войди, чтобы слушать свою библиотеку и ленту подписок.")
                .Foreground(Muted).FontSize(12).TextWrapping(TextWrapping.Wrap),
            new Button().Content("Войти в SoundCloud").BindIsVisible(signedIn, value => !value).Padding(14, 12)
                .OnClick(() => Run(LoginAsync)),
            new Button().Content(new StackPanel().Horizontal().Spacing(10).Children(Icons.View("sign-out", 20),
                new TextBlock().Text("Выйти из SoundCloud").CenterVertical())).BindIsVisible(signedIn).Padding(14, 12)
                .OnClick(() => Run(LogoutAsync))));

    private FrameworkElement SettingsPanel() => new Border().Background(Raised).BorderBrush(Edge).BorderThickness(1)
        .CornerRadius(18).Width(300).Padding(20).Top().Right().Margin(0, 64, 28, 0).BindIsVisible(settingsOpen)
        .Child(new StackPanel().Vertical().Spacing(14).Children(
            new Grid().Columns("*,Auto").Rows("Auto").Children(
                new TextBlock().Text("Настройки").Bold().FontSize(16).CenterVertical().Column(0),
                IconButton("x", () => settingsOpen.Value = false).Column(1)),
            new TextBlock().BindText(audioStatus).Foreground(Muted).FontSize(12).TextWrapping(TextWrapping.Wrap),
            new CheckBox().Content("Показывать музыку в Discord").BindIsChecked(discordEnabled).FontSize(12)
                .OnCheckedChanged(value => { if (presence != null) presence.Enabled = value; })));

    private FrameworkElement QueuePanel() => new Border().Background(Raised).BorderBrush(Edge).BorderThickness(1)
        .CornerRadius(22).Padding(18).Width(380).Height(336).Bottom().Right().Margin(0, 0, 36, 16).BindIsVisible(queueOpen)
        .Child(new DockPanel().LastChildFill().Spacing(12).Children(
            new Grid().Columns("*,Auto").Rows("Auto").DockTop().Children(
                new StackPanel().Vertical().Spacing(4).Column(0).Children(
                    new TextBlock().Text("Очередь воспроизведения").FontSize(17).Bold(),
                    new TextBlock().BindText(queueStatus).Foreground(Muted).FontSize(11)),
                IconButton("x", () => queueOpen.Value = false).Column(1)),
            queueList));

    private static Button IconButton(string name, Action action) => new Button().StyleName("flat-button")
        .Content(Icons.View(name, 20)).Width(34).Height(34).CornerRadius(12).OnClick(action);

    private ListBox CreateTrackList(bool compact = false)
    {
        var result = new ListBox().Items(Array.Empty<string>()).ItemHeight(compact ? 60 : 70)
            .Background(Color.Transparent).BorderThickness(0).CornerRadius(12);
        result.ZebraStriping = false;
        result.ItemTemplate = new DelegateTemplate<SoundCloudTrack>(context =>
        {
            var number = new TextBlock().Foreground(Muted).FontSize(12).CenterVertical();
            var cover = Icons.View("music-notes", compact ? 30 : 38);
            var trackTitle = new TextBlock().FontSize(compact ? 12 : 14).TextTrimming(TextTrimming.CharacterEllipsis);
            var author = new TextBlock().FontSize(11).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
            var duration = new TextBlock().FontSize(12).Foreground(Muted).CenterVertical().Right();
            context.Register("number", number); context.Register("cover", cover); context.Register("title", trackTitle);
            context.Register("author", author); context.Register("duration", duration);
            return new Grid().Columns(compact ? "40,*,44" : "44,48,*,64").Rows("*").Spacing(12).Padding(12, 8).Children(
                number.IsVisible(!compact).Column(0),
                new Border().Background(Raised).CornerRadius(8).Width(compact ? 30 : 38).Height(compact ? 30 : 38).Child(cover).CenterVertical().Left().Column(compact ? 0 : 1),
                new StackPanel().Vertical().Spacing(4).CenterVertical().Column(compact ? 1 : 2).Children(trackTitle, author),
                duration.Column(compact ? 2 : 3));
        }, (_, track, index, context) =>
        {
            context.Get<TextBlock>("number").Text = (index + 1).ToString("00");
            context.Get<TextBlock>("title").Text = track.Title;
            context.Get<TextBlock>("author").Text = track.Author + (track.Access is "blocked" or "preview" ? " · Ограничен" : "");
            context.Get<TextBlock>("duration").Text = FormatTime(track.DurationSeconds);
            var cover = context.Get<Image>("cover");
            cover.Source = Icons.Source("music-notes");
            rowTracks[cover] = track.Id;
            Run(() => LoadRowArtworkAsync(cover, track));
        }, (_, _, _, context) => rowTracks.Remove(context.Get<Image>("cover")));
        return result;
    }
}
