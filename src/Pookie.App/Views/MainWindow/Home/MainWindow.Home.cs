using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record HomeSection(string Key, StackPanel Root, ItemsControl Cards, LibraryLoadingView Loading, TextBlock Caption, TextBlock Error)
    {
        internal LibraryItem[] BoundItems { get; set; } = [];
    }
    private readonly List<HomeSection> homeSections = [];
    private readonly HashSet<string> homePending = [], homeErrors = [];
    private ScrollViewer homeScroll = null!;
    private TextBlock homeEmpty = null!;
    private HomeContent? homeContent;
    private WebSession? homeSession;
    private DateTimeOffset homeLoadedAt;

    private FrameworkElement HomePageView()
    {
        HomeSection Section(string key, string title, string caption, Action? open = null)
        {
            var cards = CreateCollectionGrid(preview: true);
            var loading = CreateLibraryLoadingView(cards, preview: true);
            loading.Skeleton.PreviewItems = key == "recommendations" ? 12 : 6;
            var description = new TextBlock().Text(caption).FontSize(13).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
            var error = new TextBlock().Text("Не удалось загрузить эту секцию. Повторим при следующем открытии главной.")
                .FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).IsVisible(false);
            FrameworkElement heading = open == null ? new TextBlock().Text(title).FontSize(22).Bold() : SectionHeader(title, open);
            var root = new StackPanel().Vertical().Spacing(16).Children(
                new StackPanel().Vertical().Spacing(4).Children(heading, description), loading.Root, error);
            var section = new HomeSection(key, root, cards, loading, description, error);
            homeSections.Add(section); return section;
        }
        var recommendations = Section("recommendations", "Открытия для тебя", "По твоим лайкам и недавним прослушиваниям");
        var releases = Section("releases", "Новое от подписок", "Свежие треки за последние 30 дней", () => Run(() => NavigateAsync(Page.Feed)));
        homeEmpty = new TextBlock().Text("Здесь появится твоя музыка\nНайди и послушай несколько треков — подберём следующие.")
            .FontSize(18).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Margin(0, 24).IsVisible(false);
        homeScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(
            new StackPanel().Vertical().Spacing(28).Padding(0, 8, 0, 24).Children(HomeHeroView(),
                HomeShortcutsView(),
                recommendations.Root, releases.Root, homeEmpty));
        RenderHome(); return homeScroll;
    }

    private LibraryItem[] HomeItems(string key) => homeContent == null ? [] : key switch
    {
        "recent" => homeContent.Recent, "recommendations" => homeContent.Recommendations,
        "releases" => homeContent.Releases, "mixes" => homeContent.Mixes, _ => []
    };

    private void RenderHome()
    {
        if (homeEmpty == null) return;
        foreach (var section in homeSections)
        {
            var items = HomeItems(section.Key);
            if (!section.BoundItems.SequenceEqual(items))
            { section.BoundItems = items; section.Cards.Items(items, item => item.Title); }
            section.Cards.Height = Math.Max(1, Math.Ceiling(items.Length / (double)libraryColumns)) * (likedArtworkSize + 90);
            section.Loading.SetLoading(items.Length == 0 && homePending.Contains(section.Key));
            section.Error.IsVisible = items.Length == 0 && homeErrors.Contains(section.Key) && !homePending.Contains(section.Key);
            section.Loading.Root.IsVisible = items.Length > 0 || homePending.Contains(section.Key);
            section.Root.IsVisible = items.Length > 0 || homePending.Contains(section.Key) || section.Error.IsVisible;
            if (section.Key == "recommendations") section.Caption.Text = homeContent?.Seeds.Length > 0
                ? "Похоже на " + string.Join(" и ", homeContent.Seeds.Select(title => "«" + title + "»"))
                : "По твоим лайкам и недавним прослушиваниям";
        }
        RenderHomeHero();
        RefreshHomeShortcuts();
        homeEmpty.IsVisible = homePending.Count == 0 && homeSections.All(section => !section.Root.IsVisible);
    }

    private void RefreshCachedHome()
    {
        if (homeContent != null) homeContent = HomeRecommendations.RefreshRecent(homeContent, localRecent);
        RenderHome();
    }

    private void ResetHomeLoading()
    {
        homePending.Clear();
        foreach (var section in homeSections) section.Loading.SetLoading(false);
        RenderHome();
    }

    private void ClearHome()
    {
        ResetHomeHero();
        homeContent = null; homeSession = null; homeLoadedAt = default;
        homePending.Clear(); homeErrors.Clear(); RenderHome();
    }

    private async Task PlayHomeTrackAsync(SoundCloudTrack track, Button card)
    {
        if (current?.Id == track.Id) { await ToggleAsync(); return; }
        var section = homeSections.FirstOrDefault(section => section.Cards.IsAncestorOf(card));
        var data = HomeItems(section?.Key ?? "").Select(item => item.Track).OfType<SoundCloudTrack>().ToArray();
        if (section == null || data.Length == 0) { SelectLibraryTrack(track); return; }
        StartQueueContext(new("home:" + section.Key, section.Key switch
        { "releases" => "Новое от подписок", _ => "Для тебя" },
            "Сейчас играет с главной", Kind: PlaybackContextKind.Recommendations), data, track);
        await PlayAsync(track);
    }
}
