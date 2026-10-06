using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private static bool IsLibrary(Page value) => value >= Page.Library;
    private readonly Dictionary<string, LibraryPage> libraryPages = [];
    private readonly Dictionary<string, Task<LibraryPage>> libraryLoads = [];
    private readonly Dictionary<string, DateTimeOffset> libraryLoadedAt = [];
    private readonly Dictionary<string, string> overviewErrors = [];
    private readonly List<(ItemsControl Grid, bool Preview)> collectionGrids = [];
    private readonly Dictionary<string, (ItemsControl Grid, TextBlock Empty)> overviewSections = [];
    private readonly Dictionary<Button, (LibraryItem Item, Image Cover, Border Frame, TextBlock Title, TextBlock Author)> collectionCards = [];
    private readonly Dictionary<string, ImageSource> collectionImages = [];
    private readonly ObservableValue<string> librarySectionTitle = new("");
    private readonly ObservableValue<string> librarySectionStatus = new("");
    private readonly ObservableValue<bool> librarySectionMore = new(false);
    private readonly List<LibraryItem> localRecent = [];
    private long overviewRefreshGeneration;
    private readonly HashSet<string> overviewPending = [];
    private readonly Dictionary<string, LibraryLoadingView> overviewLoadingViews = [];
    private LibraryLoadingView collectionLoadingView = null!;
    private LibraryLoadingView likesLoadingView = null!, overviewLikesLoadingView = null!;
    private bool likesLoading;
    private TextBlock overviewLikesEmpty = null!;
    private ItemsControl collectionGrid = null!;
    private TrackPage? libraryLikes;
    private LibraryPage? activeCollection;
    private string activeLibrarySource = "";

    private static string SectionTitle(Page target) => target switch {
        Page.LibraryPlaylists => "Плейлисты", Page.LibraryAlbums => "Альбомы", Page.LibraryStations => "Станции",
        Page.LibraryFollowing => "Подписки", Page.LibraryHistory => "История", _ => "Библиотека" };
    private static string SectionSource(Page target) => target switch {
        Page.LibraryPlaylists or Page.LibraryAlbums => "collections", Page.LibraryStations => "stations",
        Page.LibraryFollowing => "following", Page.LibraryHistory => "history", _ => "recent" };

    private Button SectionHeader(string title, Action click)
    {
        var hover = new ObservableValue<bool>(false);
        var arrow = Icons.View("arrow-right", 26).Bind(UIElement.OpacityProperty, hover, value => value ? 1d : 0d);
        arrow.Transitions = [Transition.Create(UIElement.OpacityProperty, 160)];
        return new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Height(34).Left()
            .Content(new StackPanel().Horizontal().Spacing(8).Children(
                new TextBlock().Text(title).FontSize(22).Bold().CenterVertical(), arrow.CenterVertical()))
            .OnMouseEnter(() => hover.Value = true).OnMouseLeave(() => hover.Value = false).OnClick(click);
    }

    private FrameworkElement OverviewSection(string title, string source, Page target)
    {
        var grid = CreateCollectionGrid(true);
        var empty = new TextBlock().Text("Загрузка…").FontSize(13).Foreground(Muted).Margin(0, 8);
        overviewSections[source] = (grid, empty);
        var loadingView = CreateLibraryLoadingView(new Grid().Columns("*").Rows("Auto").Children(grid, empty), preview: true);
        overviewLoadingViews[source] = loadingView;
        return new StackPanel().Vertical().Spacing(18).Children(
            SectionHeader(title, () => Run(() => ShowLibrarySectionAsync(target))), loadingView.Root);
    }

    private FrameworkElement CollectionPage()
    {
        collectionGrid = CreateCollectionGrid(false);
        collectionLoadingView = CreateLibraryLoadingView(collectionGrid);
        return new DockPanel().LastChildFill().Spacing(22).Padding(0, 14).Children(
            new TextBlock().BindText(librarySectionTitle).FontSize(22).Bold().DockTop(),
            new StackPanel().Vertical().Spacing(8).DockBottom().Children(
                new TextBlock().BindText(librarySectionStatus).FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap),
                new Button().StyleName("flat-button").Content("Показать ещё").BindIsVisible(librarySectionMore)
                    .Left().OnClick(() => Run(LoadMoreLibraryAsync))), collectionLoadingView.Root);
    }

    private ItemsControl CreateCollectionGrid(bool preview)
    {
        var grid = new ItemsControl().Items(Array.Empty<LibraryItem>(), item => item.Title)
            .Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0, 0, 24, 0))
            .WrapPresenter(likedArtworkSize + 24, likedArtworkSize + 90);
        collectionGrids.Add((grid, preview));
        if (preview) grid.Height = likedArtworkSize + 90;
        grid.ItemTemplate = new DelegateTemplate<LibraryItem>(context =>
        {
            var image = new Image().StretchMode(Stretch.UniformToFill);
            var frame = new Border().CornerRadius(6).ClipToBounds().Child(image);
            var title = new TextBlock().FontSize(13).SemiBold().Height(20).TextTrimming(TextTrimming.CharacterEllipsis);
            var author = new TextBlock().FontSize(12).SemiBold().Height(19).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
            var followersIcon = Icons.View("user", 13, Muted).CenterVertical();
            var subtitle = new StackPanel().Horizontal().Spacing(4).Children(followersIcon, author.CenterVertical());
            var root = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Top()
                .Content(new StackPanel().Vertical().Spacing(1).Children(frame, title.Margin(0, 7, 0, 0), subtitle));
            root.Click += () => { if (collectionCards.TryGetValue(root, out var card)) Run(() => OpenLibraryItemAsync(card.Item)); };
            context.Register("root", root); context.Register("cover", image); context.Register("frame", frame);
            context.Register("title", title); context.Register("author", author);
            context.Register("subtitle", subtitle); context.Register("followers", followersIcon);
            return root;
        }, (_, item, _, context) =>
        {
            var root = context.Get<Button>("root"); var image = context.Get<Image>("cover"); var frame = context.Get<Border>("frame");
            var title = context.Get<TextBlock>("title"); var author = context.Get<TextBlock>("author");
            collectionCards[root] = (item, image, frame, title, author);
            var subtitle = context.Get<StackPanel>("subtitle");
            title.Text = item.Title; author.Text = item.Subtitle;
            var artist = item.User != null;
            title.TextAlignment = artist ? TextAlignment.Center : TextAlignment.Left;
            subtitle.HorizontalAlignment = artist ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            context.Get<Image>("followers").IsVisible = artist;
            title.Margin = new Thickness(0, artist ? 12 : 7, 0, 0);
            frame.CornerRadius = item.User != null ? likedArtworkSize / 2 : 6;
            SetCollectionCardSize(root, image, frame);
            image.Source = item.ArtworkUrl != null && collectionImages.TryGetValue(item.ArtworkUrl, out var cached) ? cached : Icons.Source(item.User != null ? "user-circle" : "music-notes");
            Run(() => LoadCollectionImageAsync(root, item));
        }, (_, _, _, context) => collectionCards.Remove(context.Get<Button>("root")));
        return grid;
    }

    private void SetCollectionCardSize(Button root, Image image, Border frame)
    {
        root.Width = likedArtworkSize; frame.Width = frame.Height = likedArtworkSize;
        image.Width = image.Height = likedArtworkSize;
        if (collectionCards.TryGetValue(root, out var card))
        {
            card.Title.Width = likedArtworkSize;
            card.Author.MaxWidth = likedArtworkSize - (card.Item.User != null ? 17 : 0);
            if (card.Item.User != null) frame.CornerRadius = likedArtworkSize / 2;
        }
    }

    private async Task LoadCollectionImageAsync(Button root, LibraryItem item)
    {
        if (item.ArtworkUrl == null || collectionImages.ContainsKey(item.ArtworkUrl)) return;
        await coverGate.WaitAsync(lifetime.Token);
        ImageSource? source;
        try { source = await FetchLargeArtworkAsync(new SoundCloudTrack { ArtworkUrl = item.ArtworkUrl }, lifetime.Token); }
        finally { coverGate.Release(); }
        if (source == null || disposed) return;
        if (collectionImages.Count >= 256) collectionImages.Remove(collectionImages.Keys.First());
        collectionImages[item.ArtworkUrl] = source;
        if (collectionCards.TryGetValue(root, out var card) && card.Item.Key == item.Key) card.Cover.Source = source;
    }

    private async Task<LibraryPage> LoadLibrarySourceAsync(string source)
    {
        if (libraryPages.TryGetValue(source, out var cached) && (demo || source is not ("recent" or "history") ||
            libraryLoadedAt.TryGetValue(source, out var at) && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(source == "recent" ? 2 : 15))) return cached;
        if (demo || me == null) return new([], null);
        if (!libraryLoads.TryGetValue(source, out var request))
            libraryLoads[source] = request = api.GetLibraryAsync(source, me.Id, lifetime.Token);
        var session = api.Session;
        try
        {
            var result = await request;
            if (!disposed && api.Session == session) { libraryPages[source] = result; libraryLoadedAt[source] = DateTimeOffset.UtcNow; overviewErrors.Remove(source); if (source == "collections") { overviewErrors.Remove("playlists"); overviewErrors.Remove("albums"); } }
            return result;
        }
        finally { if (libraryLoads.GetValueOrDefault(source) == request) libraryLoads.Remove(source); }
    }

    private async Task RefreshOverviewDataAsync()
    {
        var generation = ++overviewRefreshGeneration;
        var session = api.Session;
        bool Current() => !disposed && generation == overviewRefreshGeneration && api.Session == session;
        overviewPending.UnionWith(["recent", "collections", "stations", "following"]);
        RefreshOverviewSections();
        try
        {
            foreach (var source in new[] { "recent", "collections", "stations", "following" })
            {
                if (!Current()) return;
                try { await LoadLibrarySourceAsync(source); }
                catch (OperationCanceledException) { return; }
                catch (SoundCloudException error)
                {
                    if (!Current()) return;
                    SetOverviewStatus(source, error.Message);
                    // A protection block must not trigger a chain of further requests.
                    if (error.StatusCode is 401 or 403 or 429 || error.RequiresBrowserVerification) return;
                    continue;
                }
                catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or HttpRequestException)
                { if (!Current()) return; SetOverviewStatus(source, FriendlyError(error)); continue; }
                if (!Current()) return;
                overviewPending.Remove(source);
                RefreshOverviewSections();
            }
        }
        finally { if (Current()) { overviewPending.Clear(); RefreshOverviewSections(); } }
    }

    private void SetOverviewStatus(string source, string message)
    {
        overviewPending.Remove(source);
        foreach (var key in source == "collections" ? new[] { "playlists", "albums" } : new[] { source })
            if (overviewSections.TryGetValue(key, out var section)) { overviewErrors[key] = message; section.Empty.Text = message; }
        RefreshOverviewSections();
    }

    private LibraryItem[] LibraryItems(string key)
    {
        var source = key is "playlists" or "albums" ? "collections" : key;
        var items = libraryPages.GetValueOrDefault(source)?.Items ?? [];
        return key switch {
            "playlists" => items.Where(item => !item.IsAlbum).ToArray(),
            "albums" => items.Where(item => item.IsAlbum).ToArray(),
            "recent" => demo ? localRecent.Concat(items).DistinctBy(item => item.Key).ToArray() : items,
            "history" => localRecent.Where(item => item.Track != null).Concat(items).DistinctBy(item => item.Key).ToArray(),
            _ => items };
    }

    private void RefreshOverviewSections()
    {
        foreach (var (key, section) in overviewSections)
        {
            var items = LibraryItems(key).Take(6).ToArray();
            var source = key is "playlists" or "albums" ? "collections" : key;
            overviewLoadingViews[key].SetLoading(overviewPending.Contains(source) && !libraryPages.ContainsKey(source) && items.Length == 0);
            section.Grid.Items(items, item => item.Title);
            section.Grid.Height = Math.Max(1, Math.Ceiling(items.Length / (double)libraryColumns)) * (likedArtworkSize + 90);
            section.Grid.IsVisible = items.Length > 0;
            section.Empty.IsVisible = items.Length == 0;
            section.Empty.Text = overviewErrors.GetValueOrDefault(key) ?? (me == null && !demo ? "Войди в SoundCloud, чтобы открыть этот раздел." : "Здесь пока ничего нет.");
        }
    }

    private Task ShowLibrarySectionAsync(Page target) => NavigateRouteAsync(new(target), async generation =>
    {
        librarySectionTitle.Value = SectionTitle(target); librarySectionStatus.Value = "Загрузка…";
        librarySectionMore.Value = false;
        activeLibrarySource = SectionSource(target);
        activeCollection = libraryPages.GetValueOrDefault(activeLibrarySource);
        if (activeCollection != null) RenderLibrarySection();
        else collectionGrid.Items(Array.Empty<LibraryItem>(), item => item.Title);
        collectionLoadingView.SetLoading(!libraryPages.ContainsKey(activeLibrarySource) && !demo && me != null);
        var session = api.Session;
        var result = await LoadLibrarySourceAsync(activeLibrarySource);
        if (generation != navigationGeneration || disposed || api.Session != session) return;
        activeCollection = result;
        RenderLibrarySection();
    });

    private void RenderLibrarySection()
    {
        var items = page.Value == Page.LibraryCollection ? activeCollection?.Items ?? [] : LibraryItems(page.Value switch {
            Page.LibraryPlaylists => "playlists", Page.LibraryAlbums => "albums", _ => activeLibrarySource });
        collectionGrid.Items(items, item => item.Title);
        librarySectionMore.Value = activeCollection?.NextHref != null;
        librarySectionStatus.Value = items.Length > 0 ? "" : me == null && !demo ? "Войди в SoundCloud, чтобы открыть этот раздел." : "Здесь пока ничего нет.";
    }

    private async Task LoadMoreLibraryAsync()
    {
        if (activeCollection?.NextHref is not { } url) return;
        var previous = activeCollection; var target = page.Value; var session = api.Session;
        librarySectionMore.Value = false;
        try
        {
            var next = await api.GetLibraryNextAsync(url, lifetime.Token);
            if (page.Value != target || api.Session != session || disposed || activeCollection != previous) return;
            activeCollection = new(previous.Items.Concat(next.Items).DistinctBy(item => item.Key).ToArray(), next.NextHref);
            if (target != Page.LibraryCollection) libraryPages[activeLibrarySource] = activeCollection;
            RenderLibrarySection(); RefreshOverviewSections();
        }
        finally { librarySectionMore.Value = activeCollection?.NextHref != null; }
    }

    private async Task OpenLibraryItemAsync(LibraryItem item)
    {
        if (item.Track is { } track)
        {
            if (current?.Id == track.Id) { await ToggleAsync(); return; }
            queueTracks.Clear(); queueTracks.AddRange((activeCollection?.Items ?? LibraryItems("recent")).Where(i => i.Track != null).Select(i => i.Track!));
            if (!queueTracks.Any(t => t.Id == track.Id)) queueTracks.Add(track);
            playbackHistory.Clear(); shuffleBag.Clear(); RefreshQueue();
            await PlayAsync(track); return;
        }
        await NavigateRouteAsync(new(Page.LibraryCollection, Item: item), async _ =>
        {
            librarySectionTitle.Value = item.Title; librarySectionStatus.Value = "Загрузка…"; librarySectionMore.Value = false;
            activeCollection = null; collectionGrid.Items(Array.Empty<LibraryItem>(), i => i.Title);
            collectionLoadingView.SetLoading(!demo && me != null);
            var token = BeginLoad(); var session = api.Session;
            if (demo) { librarySectionStatus.Value = "В демо-режиме подборки не загружаются."; return; }
            var result = await api.GetCollectionTracksAsync(item, token);
            if (disposed || token.IsCancellationRequested || api.Session != session) return;
            activeCollection = new(result.Tracks.Select(LibraryItem.FromTrack).ToArray(), result.NextHref);
            RenderLibrarySection();
        });
    }

    private void ClearLibraryData()
    {
        ++overviewRefreshGeneration;
        libraryPages.Clear(); libraryLoads.Clear(); libraryLoadedAt.Clear(); overviewErrors.Clear(); collectionImages.Clear(); localRecent.Clear(); libraryLikes = null; activeCollection = null;
        overviewPending.Clear(); SetLikesLoading(false); collectionLoadingView.SetLoading(false);
        RefreshOverviewSections();
    }

    private readonly List<LibraryLoadingView> libraryLoadingViews = [];

    private LibraryLoadingView CreateLibraryLoadingView(FrameworkElement content, bool preview = false)
    {
        var view = new LibraryLoadingView(content, preview);
        view.Skeleton.SetGeometry(likedArtworkSize, libraryColumns, false);
        libraryLoadingViews.Add(view);
        return view;
    }

    private void SetLikesLoading(bool loading)
    {
        if (likesLoading == loading) return;
        likesLoading = loading;
        likesLoadingView.SetLoading(loading); overviewLikesLoadingView.SetLoading(loading);
        overviewLikesEmpty.IsVisible = !loading && !hasLibraryTracks.Value;
        RefreshLikedViews();
    }
}
