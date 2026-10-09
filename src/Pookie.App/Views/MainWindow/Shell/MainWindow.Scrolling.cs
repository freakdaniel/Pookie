using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<ScrollViewer, SmoothScroll> smoothScrolls = [];
    private readonly ObservableValue<bool> paginationLoading = new(false);
    private string? failedPageCursor;
    private readonly HashSet<string> loadedPageCursors = [];
    private int emptyPageStreak;

    private LibraryLoadingView homeLoadingView = null!;
    private FrameworkElement CreateHomeLoadingView()
    {
        homeLoadingView = CreateLibraryLoadingView(list);
        homeLoadingView.Skeleton.CompactList = true;
        homeLoadingView.Skeleton.SetGeometry(likedArtworkSize, libraryColumns, true);
        return homeLoadingView.Root;
    }
    private readonly Dictionary<ScrollableItemsBase, PaginationItems> pageItems = [];
    private readonly List<(LibrarySkeleton Skeleton, PaginationItems Source)> pageSkeletons = [];

    private void SetPageItems<T>(ScrollableItemsBase control, IEnumerable<T> items, LoadingRowStyle style,
        Func<object, object, bool>? equal = null)
    {
        if (!pageItems.TryGetValue(control, out var source))
        {
            source = new PaginationItems(style, PageItemKey, equal); pageItems[control] = source;
            LibrarySkeleton CreateSkeleton()
            {
                var skeleton = new LibrarySkeleton();
                skeleton.ConfigureLoadingRow(likedArtworkSize, libraryColumns, source.Style);
                pageSkeletons.Add((skeleton, source));
                return skeleton;
            }
            if (control is ItemsControl grid)
            { grid.ItemTemplate = new PaginationTemplate(grid.ItemTemplate, CreateSkeleton); grid.ItemsSource = source.View; }
            else if (control is ListBox listBox)
            { listBox.ItemTemplate = new PaginationTemplate(listBox.ItemTemplate, CreateSkeleton); listBox.ItemsSource = source.View; }
        }
        source.Style = style;
        source.SetData(items.Cast<object>());
    }

    private static object PageItemKey(object item) => item switch
    {
        Pookie.SoundCloud.SoundCloudTrack track => ("track", track.Id),
        Pookie.SoundCloud.LibraryItem entry => entry.Key,
        SearchBlock block => (block.Kind, block.Kind switch
        {
            SearchBlockKind.Hero => "hero",
            SearchBlockKind.Cards => block.Items?.FirstOrDefault()?.Key ?? "",
            SearchBlockKind.Track or SearchBlockKind.LibraryTrack => block.Item?.Key ?? "",
            _ => block.Title
        }),
        _ => item
    };

    private void RefreshPaginationSkeletons(bool loading)
    {
        foreach (var existing in pageItems.Values) existing.SetLoading(0);
        if (!loading) return;
        var control = page.Value switch
        {
            Page.Search => searchList,
            Page.LibraryTracks => likesAsList.Value ? likedList : likedGrid,
            > Page.LibraryTracks => collectionGrid,
            _ => (ScrollableItemsBase)list
        };
        if (pageItems.TryGetValue(control, out var source))
            source.SetLoading(source.Style switch
            { LoadingRowStyle.Card => libraryColumns, LoadingRowStyle.CardRow => 1, _ => 3 });
    }

    private void ObservePageScrolling()
    {
        if (disposed) return;
        ClearUnboundExpandedQueueRows();
        if (expandedQueueAnchor == null && !expandedQueueStartPending) expandedQueuePositions.Clear();
        if (page.Value == Page.Search && searchLayoutPending)
        {
            searchLayoutPending = false;
            if (renderedSearchColumns != libraryColumns) RefreshSearchViews();
            UpdateSearchHeroGeometry(searchList.ActualWidth);
            // Apply the new row geometry after the current arrange has finished.
            // Invalidating during SizeChanged can be swallowed by that arrange pass.
            foreach (var view in searchViews.Values) view.Hero.Root.InvalidateMeasure();
            searchList.InvalidateMeasure();
        }
        foreach (var (skeleton, source) in pageSkeletons)
            skeleton.ConfigureLoadingRow(likedArtworkSize, libraryColumns, source.Style);
        if (page.Value is Page.Library or Page.Search)
        {
            DisablePreviewGridScrolling(libraryGrid);
            foreach (var (grid, preview) in collectionGrids)
                if (preview) DisablePreviewGridScrolling(grid);
        }
        foreach (var viewer in new[] { NavigationScroll(), queueList.FindVisualChild<ScrollViewer>() as ScrollViewer })
            if (viewer != null && !smoothScrolls.ContainsKey(viewer))
            {
                smoothScrolls[viewer] = new SmoothScroll(viewer);
                viewer.ScrollChanged += CheckPageEnd;
                viewer.MouseWheel += OnPageWheel;
            }
        CheckPageEnd();
    }

    private static void DisablePreviewGridScrolling(ItemsControl grid)
    {
        if (grid.FindVisualChild<ScrollViewer>() is ScrollViewer viewer) viewer.VerticalScroll = ScrollMode.Disabled;
    }

    private string? PageCursor() => page.Value switch
    {
        Page.Library => null,
        Page.Track => null,
        Page.Search => searchResults?.NextHref,
        > Page.LibraryTracks => activeCollection?.NextHref,
        _ => nextHref
    };

    private int PageItemCount() => page.Value == Page.Search ? searchResults?.Items.Length ?? 0 :
        page.Value > Page.LibraryTracks ? pageItems.GetValueOrDefault(collectionGrid)?.DataCount ?? 0 :
        page.Value == Page.LibraryTracks ? pageItems.GetValueOrDefault(likesAsList.Value ? likedList : likedGrid)?.DataCount ?? 0 : tracks.Count;

    private void OnPageWheel(MouseWheelEventArgs args)
    {
        if (args.Delta.Y < 0 && !paginationLoading.Value)
        {
            failedPageCursor = null; emptyPageStreak = 0;
            CheckPageEnd();
        }
    }

    private void CheckPageEnd()
    {
        if (disposed || !CanUseWorkspace || paginationLoading.Value || navigationHistory[navigationIndex].Pending ||
            pendingNavigationScroll != null || likesLoading || searchLoading.Value || collectionLoadingView.Loading) return;
        var viewer = NavigationScroll();
        var cursor = PageCursor();
        if (viewer == null || viewer.ViewportHeight <= 0 || cursor == null) return;
        var remaining = SmoothScroll.Maximum(viewer) - viewer.VerticalOffset;
        var threshold = Math.Max(160, viewer.ViewportHeight * .5);
        if (remaining > threshold)
        {
            failedPageCursor = null; emptyPageStreak = 0;
            return;
        }
        if (cursor == failedPageCursor || loadedPageCursors.Contains(cursor) || emptyPageStreak >= 3) return;
        Run(LoadNextVisiblePageAsync);
    }

    private async Task LoadNextVisiblePageAsync()
    {
        if (paginationLoading.Value || PageCursor() is not { } cursor) return;
        var generation = navigationGeneration;
        var count = PageItemCount();
        paginationLoading.Value = true;
        RefreshPaginationSkeletons(true);
        try
        {
            if (page.Value == Page.Search) await LoadMoreSearchAsync();
            else if (page.Value > Page.LibraryTracks) await LoadMoreLibraryAsync();
            else await MoreAsync();
            if (disposed || generation != navigationGeneration) return;
            loadedPageCursors.Add(cursor);
            emptyPageStreak = PageItemCount() > count ? 0 : emptyPageStreak + 1;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error)
        {
            if (disposed || generation != navigationGeneration) return;
            failedPageCursor = cursor;
            if (error is Pookie.SoundCloud.SoundCloudException { StatusCode: 401 }) throw;
        }
        finally
        {
            if (!disposed && generation == navigationGeneration)
            { RefreshPaginationSkeletons(false); paginationLoading.Value = false; }
        }
    }

    private void ResetPageScrolling()
    {
        searchScrollAnchor = null;
        foreach (var motion in smoothScrolls.Values) motion.Stop();
        foreach (var source in pageItems.Values) source.SetLoading(0);
        paginationLoading.Value = false;
        failedPageCursor = null; loadedPageCursors.Clear(); emptyPageStreak = 0;
    }

    private void DisposePageScrolling()
    {
        Window.FrameRendered -= ObservePageScrolling;
        foreach (var (viewer, motion) in smoothScrolls)
        { viewer.ScrollChanged -= CheckPageEnd; viewer.MouseWheel -= OnPageWheel; motion.Dispose(); }
        smoothScrolls.Clear();
        foreach (var (skeleton, _) in pageSkeletons) skeleton.SetActive(false);
        foreach (var skeleton in artworkLoading.Values) skeleton.SetActive(false);
    }
}
