using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<SearchSection> searchSection = new(SearchSection.All);
    private readonly ObservableValue<string> searchHeading = new("Поиск"), searchStatus = new("");
    private readonly ObservableValue<bool> searchLoading = new(false), searchEmpty = new(false);
    private LibraryPage? searchResults;
    private ItemsControl searchList = null!;
    private const double SearchShadowGutter = 14;
    private LibraryLoadingView searchLoadingView = null!;
    private bool searchLayoutPending;
    private int renderedSearchColumns;
    private (object Key, double Y, long Generation)? searchScrollAnchor;

    private enum SearchBlockKind { Hero, Cards, Track, LibraryTrack, Heading }
    private sealed class SearchBlock(SearchBlockKind Kind, string Title = "", LibraryItem? Item = null, LibraryItem[]? Items = null)
    {
        public SearchBlockKind Kind { get; } = Kind;
        public string Title { get; } = Title;
        public LibraryItem? Item { get; set; } = Item;
        public LibraryItem[]? Items { get; set; } = Items;
    }

    private FrameworkElement SearchPage()
    {
        searchList = new ItemsControl().Items(Array.Empty<SearchBlock>(), block => block.Title)
            .ItemHeight(64).VariableHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .ItemPadding(new Thickness(SearchShadowGutter, 0, 18, 0));
        searchList.ItemTemplate = SearchResultTemplate();
        searchList.SizeChanged += e =>
        {
            UpdateLikedGridSize(e.NewSize.Width - SearchShadowGutter);
            searchLayoutPending = true;
        };
        searchLoadingView = CreateLibraryLoadingView(new ScrollLayoutHost(RestoreSearchAnchor)
            // Extend the scroll viewport into the page gutter so it doesn't clip the card's shadow.
            // ItemPadding restores the original content alignment inside that wider viewport.
            .Background(Color.Transparent).BorderThickness(0).Padding(0).Margin(-SearchShadowGutter, 0, 0, 0).Content(searchList));
        searchLoadingView.Skeleton.SetGeometry(likedArtworkSize, libraryColumns, false);
        searchLoadingView.Skeleton.CompactList = false;
        searchLoading.Changed += () =>
        {
            searchLoadingView.Skeleton.SetGeometry(likedArtworkSize, libraryColumns, searchSection.Value == SearchSection.Tracks);
            searchLoadingView.SetLoading(searchLoading.Value); RefreshSearchViews();
        };
        return new DockPanel().LastChildFill().Spacing(22).Children(
            new StackPanel().Vertical().Spacing(18).DockTop().Children(
                new TextBlock().BindText(searchHeading).FontSize(22).Bold().TextTrimming(TextTrimming.CharacterEllipsis),
                new StackPanel().Horizontal().Spacing(24).Children(
                    SearchTab("Всё", SearchSection.All), SearchTab("Треки", SearchSection.Tracks),
                    SearchTab("Люди", SearchSection.People), SearchTab("Альбомы", SearchSection.Albums),
                    SearchTab("Плейлисты", SearchSection.Playlists))),
            new Grid().Columns("*").Rows("*").Children(searchLoadingView.Root,
                new TextBlock().BindText(searchStatus).Foreground(Muted).FontSize(14).Top().Margin(0, 18)
                    .TextWrapping(TextWrapping.Wrap).BindIsVisible(searchEmpty)));
    }

    private Button SearchTab(string title, SearchSection section) =>
        SectionTab(title, searchSection, section, () => SelectSearchSection(section));

    private void SelectSearchSection(SearchSection section)
    {
        if (searchSection.Value == section) return;
        query.Value = navigationHistory[navigationIndex].Route.Search ?? query.Value;
        Run(() => SearchAsync(section));
    }

    private SearchBlock[] SearchBlocks()
    {
        var items = searchResults?.Items ?? [];
        if (items.Length == 0) return [];
        var blocks = new List<SearchBlock>();
        void Cards(string title, IEnumerable<LibraryItem> entries)
        {
            var rows = entries.Chunk(Math.Max(1, libraryColumns)).ToArray();
            for (var i = 0; i < rows.Length; i++) blocks.Add(new(SearchBlockKind.Cards, i == 0 ? title : "", Items: rows[i]));
        }
        if (searchSection.Value == SearchSection.All)
        {
            blocks.Add(new(SearchBlockKind.Hero, Item: items[0], Items: items.Where(item => item.Track != null).Take(4).ToArray()));
            Cards("Исполнители", items.Where(item => item.User != null));
            Cards("Альбомы", items.Where(item => item.IsAlbum));
            Cards("Плейлисты", items.Where(item => item.Playlist != null && !item.IsAlbum));
            var remaining = items.Where(item => item.Track != null).Skip(4).ToArray();
            if (remaining.Length > 0) blocks.Add(new(SearchBlockKind.Heading, "Ещё треки"));
            blocks.AddRange(remaining.Select(item => new SearchBlock(SearchBlockKind.Track, Item: item)));
        }
        else if (searchSection.Value == SearchSection.Tracks)
            blocks.AddRange(items.Where(item => item.Track != null).Select(item => new SearchBlock(SearchBlockKind.LibraryTrack, Item: item)));
        else Cards("", items);
        return blocks.ToArray();
    }

    private void RefreshSearchViews()
    {
        if (searchList == null) return;
        renderedSearchColumns = libraryColumns;
        searchList.ItemHeight = searchSection.Value == SearchSection.Tracks ? TrackRowLayout.Stride : 64;
        var blocks = SearchBlocks();
        if (pageItems.TryGetValue(searchList, out var source))
        {
            var retained = Enumerable.Range(0, source.View.Count).Select(source.View.GetItem).OfType<SearchBlock>()
                .ToDictionary(block => PageItemKey(block));
            for (var i = 0; i < blocks.Length; i++)
                if (blocks[i] is { Kind: SearchBlockKind.Cards } block && retained.TryGetValue(PageItemKey(block), out var old) && old.Title == block.Title)
                {
                    // Adding cards to a partially filled row doesn't change its height.
                    // Updating that row in place keeps the presenter's measured height;
                    // a Replace notification would discard it even far above the viewport.
                    var changed = !SameSearchBlock(old, block);
                    old.Items = block.Items; blocks[i] = old;
                    if (changed)
                        foreach (var view in searchViews.Values.Where(view => ReferenceEquals(view.Block, old)))
                            view.CardGrid.Items(old.Items ?? [], item => item.Title);
                }
        }
        SetPageItems(searchList, blocks, searchSection.Value == SearchSection.Tracks ? LoadingRowStyle.Waveform
            : blocks.LastOrDefault()?.Kind == SearchBlockKind.Track ? LoadingRowStyle.Compact : LoadingRowStyle.CardRow, SameSearchBlock);
        searchEmpty.Value = !searchLoading.Value && (searchResults?.Items.Length ?? 0) == 0;
    }

    private static bool SameSearchBlock(object a, object b) => a is SearchBlock x && b is SearchBlock y
        ? x.Kind == y.Kind && x.Title == y.Title && Equals(x.Item, y.Item) &&
            (x.Items ?? []).SequenceEqual(y.Items ?? [])
        : Equals(a, b);

    private void SetSearchResults(LibraryPage results)
    {
        searchResults = results;
        ReplaceTracks(new(results.Items.Where(item => item.Track != null).Select(item => item.Track!).ToArray(), null));
        RefreshSearchViews();
    }

    private void CaptureSearchAnchor()
    {
        if (searchScrollAnchor != null || page.Value != Page.Search || NavigationScroll() is not { } viewer) return;
        var view = searchViews.Values.Where(view => view.Block != null && view.Root.IsVisible &&
            view.Root.Bounds.Bottom > viewer.Bounds.Y && view.Root.Bounds.Y < viewer.Bounds.Bottom)
            .OrderBy(view => Math.Abs(view.Root.Bounds.Y - viewer.Bounds.Y)).FirstOrDefault();
        if (view?.Block is { } block)
            searchScrollAnchor = (PageItemKey(block), view.Root.Bounds.Y - viewer.Bounds.Y, navigationGeneration);
    }

    private bool RestoreSearchAnchor()
    {
        if (searchScrollAnchor is not { } anchor) return false;
        if (page.Value != Page.Search || anchor.Generation != navigationGeneration || NavigationScroll() is not { } viewer)
        { searchScrollAnchor = null; return false; }
        var view = searchViews.Values.FirstOrDefault(view => view.Block != null && Equals(PageItemKey(view.Block), anchor.Key));
        if (view == null)
        {
            for (var i = 0; i < searchList.ItemsSource.Count; i++)
                if (searchList.ItemsSource.GetItem(i) is SearchBlock block && Equals(PageItemKey(block), anchor.Key))
                { searchList.ScrollIntoView(i); return true; }
            searchScrollAnchor = null; return false;
        }
        var delta = view.Root.Bounds.Y - viewer.Bounds.Y - anchor.Y;
        if (Math.Abs(delta) < .5) { searchScrollAnchor = null; return false; }
        var before = viewer.VerticalOffset;
        if (smoothScrolls.TryGetValue(viewer, out var motion)) motion.CorrectLayoutOffset(delta);
        else viewer.SetScrollOffsets(0, before + delta);
        if (Math.Abs(viewer.VerticalOffset - before) < .5) { searchScrollAnchor = null; return false; }
        return true;
    }

    private async Task LoadMoreSearchAsync()
    {
        if (searchResults is not { NextHref: { } cursor } previous) return;
        var generation = navigationGeneration; var session = api.Session;
        var token = loading?.Token ?? lifetime.Token;
        var result = await api.GetSearchNextAsync(cursor, token);
        token.ThrowIfCancellationRequested();
        if (disposed || generation != navigationGeneration || api.Session != session || searchResults != previous) return;
        CaptureSearchAnchor();
        SetSearchResults(new(previous.Items.Concat(result.Items).DistinctBy(item => item.Key).ToArray(), result.NextHref));
    }
}
