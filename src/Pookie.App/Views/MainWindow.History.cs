using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // A collection's identity and a search's query belong to the route, not just its page type.
    private sealed record NavigationRoute(Page Page, string? Search = null, LibraryItem? Item = null)
    {
        public bool Matches(NavigationRoute other) => Page == other.Page && Search == other.Search && Item?.Key == other.Item?.Key;
    }
    private sealed record NavigationSnapshot(TrackPage Tracks, string Heading, string Eyebrow, string Status,
        LibraryPage? Collection, string Source, string CollectionTitle, string CollectionStatus,
        string Filter, bool ListView, double Scroll);
    private sealed class NavigationEntry(NavigationRoute route)
    {
        public NavigationRoute Route { get; } = route;
        public NavigationSnapshot? Snapshot { get; set; }
        public bool Pending { get; set; }
    }
    private readonly List<NavigationEntry> navigationHistory = [new(new(Page.Home))];
    private int navigationIndex;
    private long navigationGeneration;
    private (long Generation, double Offset)? pendingNavigationScroll;
    private readonly ObservableValue<bool> canGoBack = new(false), canGoForward = new(false);
    private Button backButton = null!, forwardButton = null!;
    private ScrollViewer overviewScroll = null!;

    private NavigationSnapshot CaptureNavigation() => new(new(tracks.ToArray(), nextHref), heading.Value, eyebrow.Value, status.Value,
        activeCollection, activeLibrarySource, librarySectionTitle.Value, librarySectionStatus.Value,
        likedFilterText, likesAsList.Value, NavigationScroll()?.VerticalOffset ?? 0);

    private ScrollViewer? NavigationScroll() => page.Value switch
    {
        Page.Library => overviewScroll,
        Page.LibraryTracks => (ScrollViewer?)(likesAsList.Value ? likedList : likedGrid).FindVisualChild<ScrollViewer>(),
        > Page.LibraryTracks => (ScrollViewer?)collectionGrid.FindVisualChild<ScrollViewer>(),
        _ => (ScrollViewer?)list.FindVisualChild<ScrollViewer>()
    };

    private long BeginNavigation(NavigationRoute route, bool pending = false)
    {
        var previous = navigationHistory[navigationIndex];
        previous.Snapshot = CaptureNavigation();
        if (!previous.Route.Matches(route))
        {
            navigationHistory.RemoveRange(navigationIndex + 1, navigationHistory.Count - navigationIndex - 1);
            navigationHistory.Add(new(route));
            navigationIndex++;
            // Keep memory bounded, including searches with many result pages.
            if (navigationHistory.Count > 100) { navigationHistory.RemoveAt(0); navigationIndex--; }
        }
        navigationHistory[navigationIndex].Pending = pending;
        loading?.Cancel();
        SetLikesLoading(false); collectionLoadingView.SetLoading(false);
        var generation = ++navigationGeneration;
        profileOpen.Value = settingsOpen.Value = false;
        CloseTopSearch(clear: false);
        if (page.Value != route.Page) list.SelectedIndex = -1;
        page.Value = route.Page;
        RefreshNavVisuals(); RefreshHistoryButtons();
        return generation;
    }

    private async Task NavigateRouteAsync(NavigationRoute route, Func<long, Task> action)
    {
        if (!CanUseWorkspace) return;
        var generation = BeginNavigation(route, pending: true);
        try { await action(generation); }
        // Late replies/errors from a page we already left cannot replace the new page.
        catch (Exception) when (generation != navigationGeneration || disposed) { }
        catch (Exception error)
        {
            if (route.Page > Page.LibraryTracks) librarySectionStatus.Value = FriendlyError(error);
            throw;
        }
        finally
        {
            if (generation == navigationGeneration && !disposed)
            {
                navigationHistory[navigationIndex].Pending = false;
                SetLikesLoading(false); collectionLoadingView.SetLoading(false);
            }
        }
    }

    private async Task MoveNavigationAsync(int offset)
    {
        if (!CanUseWorkspace) return;
        var index = navigationIndex + offset;
        if (index < 0 || index >= navigationHistory.Count) return;
        navigationHistory[navigationIndex].Snapshot = CaptureNavigation();
        loading?.Cancel();
        SetLikesLoading(false); collectionLoadingView.SetLoading(false);
        var generation = ++navigationGeneration;
        navigationIndex = index;
        var entry = navigationHistory[index];
        profileOpen.Value = settingsOpen.Value = false;
        CloseTopSearch(clear: false);
        page.Value = entry.Route.Page;
        RefreshNavVisuals(); RefreshHistoryButtons();
        if (entry.Pending || entry.Snapshot == null)
        {
            // A page left before its response arrived needs a fresh load, using the same history entry.
            if (entry.Route.Item is { } item) await OpenLibraryItemAsync(item);
            else if (entry.Route.Search is { } search) { query.Value = search; await SearchAsync(); }
            else if (entry.Route.Page > Page.LibraryTracks) await ShowLibrarySectionAsync(entry.Route.Page);
            else if (entry.Route.Page == Page.LibraryTracks) await LikesAsync(true);
            else await NavigateAsync(entry.Route.Page);
            return;
        }
        var state = entry.Snapshot;
        heading.Value = state.Heading; eyebrow.Value = state.Eyebrow; status.Value = state.Status;
        activeCollection = state.Collection; activeLibrarySource = state.Source;
        librarySectionTitle.Value = state.CollectionTitle; librarySectionStatus.Value = state.CollectionStatus;
        query.Value = entry.Route.Search ?? "";
        likedFilter.Text = state.Filter; likesAsList.Value = state.ListView;
        likedActionError.Value = "";
        ReplaceTracks((entry.Route.Page is Page.Library or Page.LibraryTracks) && libraryLikes != null ? libraryLikes : state.Tracks);
        if (entry.Route.Page == Page.Library) { RefreshLibraryCards(); RefreshOverviewSections(); }
        else if (entry.Route.Page > Page.LibraryTracks) RenderLibrarySection();
        // Restore against arranged virtualized extents, rather than a guessed delay.
        pendingNavigationScroll = (generation, state.Scroll);
        Window.InvalidateVisual();
    }

    private void RestoreNavigationScroll()
    {
        if (pendingNavigationScroll is not { } pending) return;
        pendingNavigationScroll = null;
        if (pending.Generation == navigationGeneration && !disposed)
            NavigationScroll()?.SetScrollOffsets(0, pending.Offset);
    }

    private void ResetNavigationHistory()
    {
        loading?.Cancel(); ++navigationGeneration;
        navigationHistory.Clear(); navigationHistory.Add(new(new(Page.Home))); navigationIndex = 0;
        page.Value = Page.Home;
        query.Value = ""; likedFilter.Text = "";
        ReplaceTracks(new([], null));
        eyebrow.Value = "ГЛАВНАЯ"; heading.Value = "На твоей волне";
        RefreshNavVisuals(); RefreshHistoryButtons();
    }

    private void RefreshHistoryButtons()
    {
        canGoBack.Value = navigationIndex > 0;
        canGoForward.Value = navigationIndex < navigationHistory.Count - 1;
    }

    private Border NavigationPill()
    {
        Button Arrow(string icon, ObservableValue<bool> enabled, int offset)
        {
            var hover = new ObservableValue<bool>(false);
            var normal = Icons.View(icon, 17, Muted).Bind(UIElement.OpacityProperty, enabled, value => value ? 1d : .3d);
            var bright = Icons.View(icon, 17, Color.White).Bind(UIElement.OpacityProperty, hover, value => value && enabled.Value ? 1d : 0d);
            bright.Transitions = [Transition.Create(UIElement.OpacityProperty, 180)];
            return new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Width(29).Height(29)
                .BindIsEnabled(enabled).Content(new Grid().Columns("*").Rows("*").Children(
                    normal.CenterHorizontal().CenterVertical(), bright.CenterHorizontal().CenterVertical()))
                .OnMouseEnter(() => hover.Value = true).OnMouseLeave(() => hover.Value = false)
                .OnClick(() => Run(() => MoveNavigationAsync(offset)));
        }
        backButton = Arrow("arrow-left", canGoBack, -1); forwardButton = Arrow("arrow-right", canGoForward, 1);
        return new Border().Background(Raised).CornerRadius(17).Padding(3, 1).CenterVertical()
            .Child(new StackPanel().Horizontal().Spacing(1).Children(backButton, forwardButton));
    }
}
