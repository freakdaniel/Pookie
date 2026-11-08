using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record HomeSection(string Key, StackPanel Root, ItemsControl? Cards, HomeShelf? Shelf, LibraryLoadingView Loading, TextBlock Caption, TextBlock Error)
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
        HomeSection Section(string key, string title, string? caption = null, Action? open = null)
        {
            var shelf = caption == null ? new HomeShelf(this) : null;
            var cards = shelf == null ? CreateCollectionGrid(preview: true, compactRows: true) : null;
            var loading = CreateLibraryLoadingView(shelf?.Root ?? (FrameworkElement)cards!, preview: true);
            loading.Skeleton.PreviewItems = shelf != null ? 4 : 6;
            if (cards != null)
            {
                loading.Skeleton.PreviewCardFooter = CompactCardFooter;
                loading.Skeleton.PreviewCardRowGap = CompactCardRowGap;
            }
            var description = new TextBlock().Text(caption ?? "").FontSize(13).Foreground(Muted)
                .TextTrimming(TextTrimming.CharacterEllipsis).IsVisible(caption != null);
            var error = new TextBlock().Text("Не удалось загрузить эту секцию. Повторим при следующем открытии главной.")
                .FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).IsVisible(false);
            FrameworkElement heading = open == null ? new TextBlock().Text(title).FontSize(22).Bold() : SectionHeader(title, open);
            var root = new StackPanel().Vertical().Spacing(16).Children(
                new StackPanel().Vertical().Spacing(4).Children(heading, description), loading.Root, error);
            var section = new HomeSection(key, root, cards, shelf, loading, description, error);
            homeSections.Add(section); return section;
        }
        var recommendations = Section("recommendations", "Открытия для тебя", "По твоим лайкам и недавним прослушиваниям");
        var madeForYou = Section("made-for-you", "Сделано для тебя");
        var releases = Section("releases", "Новое от подписок", "Свежие треки за последние 30 дней", () => Run(() => NavigateAsync(Page.Feed)));
        homeEmpty = new TextBlock().Text("Здесь появится твоя музыка\nНайди и послушай несколько треков — подберём следующие.")
            .FontSize(18).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Margin(0, 24).IsVisible(false);
        homeScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(
            new StackPanel().Vertical().Spacing(28).Padding(0, 8, 0, 24).Children(HomeMixCarouselView(),
                HomeShortcutsView(),
                HomeChartView(), recommendations.Root, madeForYou.Root, releases.Root, homeEmpty));
        RenderHome(); return homeScroll;
    }

    private LibraryItem[] HomeItems(string key) => homeContent == null ? [] : key switch
    {
        "recent" => homeContent.Recent, "recommendations" => homeContent.Recommendations,
        "releases" => homeContent.Releases, "mixes" => homeContent.Discover?.Mixes ?? [],
        "made-for-you" => homeContent.Discover?.MadeForYou ?? [], "trends" => homeContent.Discover?.Trending ?? [], _ => []
    };

    private void BindHomeSectionItems(HomeSection section)
    {
        var items = HomeItems(section.Key);
        if (section.Key == "recommendations")
        {
            section.Loading.Skeleton.PreviewItems = libraryColumns;
            if (items.Length > libraryColumns) items = items[..libraryColumns];
        }
        if (!section.BoundItems.SequenceEqual(items))
        {
            section.BoundItems = items;
            section.Cards?.Items(items, item => item.Key);
            section.Shelf?.SetItems(items);
        }
        if (section.Cards is { } cards) SetCollectionPreviewHeight(cards, compactRows: true);
    }

    private void RenderHome(bool refreshChart = false)
    {
        if (homeEmpty == null) return;
        RenderHomeMixCarousel(HomeItems("mixes"));
        RenderHomeChart(HomeItems("trends"), refreshChart);
        foreach (var section in homeSections)
        {
            BindHomeSectionItems(section);
            var hasItems = section.BoundItems.Length > 0;
            section.Loading.SetLoading(!hasItems && homePending.Contains(section.Key));
            section.Error.IsVisible = !hasItems && homeErrors.Contains(section.Key) && !homePending.Contains(section.Key);
            section.Loading.Root.IsVisible = hasItems || homePending.Contains(section.Key);
            section.Root.IsVisible = hasItems || homePending.Contains(section.Key) || section.Error.IsVisible;
            if (section.Key == "recommendations") section.Caption.Text = homeContent?.Seeds.Length > 0
                ? "Похоже на " + string.Join(" и ", homeContent.Seeds.Select(title => "«" + title + "»"))
                : "По твоим лайкам и недавним прослушиваниям";
        }
        RefreshHomeShortcuts();
        homeEmpty.IsVisible = homePending.Count == 0 && !homeMixSection.IsVisible && !homeChart.IsVisible && homeSections.All(section => !section.Root.IsVisible);
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
        homeLikesCount = null; homeLikesPreview = []; ++homeAccountGeneration;
        homeContent = null; homeSession = null; homeLoadedAt = default;
        ResetHomeChartCache();
        homePending.Clear(); homeErrors.Clear(); RenderHome();
    }

    private async Task PlayHomeTrackAsync(SoundCloudTrack track, Button card)
    {
        if (current?.Id == track.Id) { await ToggleAsync(); return; }
        var section = homeSections.FirstOrDefault(section => section.Cards?.IsAncestorOf(card) == true);
        var data = HomeItems(section?.Key ?? "").Select(item => item.Track).OfType<SoundCloudTrack>().ToArray();
        if (section == null || data.Length == 0) { SelectLibraryTrack(track); return; }
        StartQueueContext(new("home:" + section.Key, section.Key switch
        { "releases" => "Новое от подписок", _ => "Для тебя" },
            "Сейчас играет с главной", Kind: PlaybackContextKind.Recommendations), data, track);
        await PlayAsync(track);
    }
}
