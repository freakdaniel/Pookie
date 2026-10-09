using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> likesAsList = new(false);
    private readonly ObservableValue<bool> likedEmpty = new(false);
    private readonly ObservableValue<string> likedActionError = new("");
    private readonly Dictionary<Button, LikedTrackTile> likedTiles = [];
    private readonly Dictionary<Image, long> likedArtworkTracks = [];
    private ItemsControl likedGrid = null!;
    private ItemsControl likedList = null!;
    private TextBox likedFilter = null!;
    private string likedFilterText = "";
    private double likedArtworkSize = 176;
    private int libraryColumns = 6;

    private FrameworkElement LibraryTabs() => new StackPanel().Horizontal().Spacing(22)
        .BindIsVisible(page, value => IsLibrary(value)).Children(
            LibraryTab("Обзор", Page.Library, () =>
            {
                if (!demo && me != null && libraryLikes == null) { Run(() => LikesAsync()); return; }
                BeginNavigation(new(Page.Library));
                RefreshNavVisuals();
                if (libraryLikes != null) ReplaceTracks(libraryLikes);
                RefreshLibraryCards();
                RefreshOverviewSections();
                Run(RefreshOverviewDataAsync);
            }),
            LibraryTab("Лайки", Page.LibraryTracks, ShowLibraryTracks),
            LibraryTab("Плейлисты", Page.LibraryPlaylists, () => Run(() => ShowLibrarySectionAsync(Page.LibraryPlaylists))),
            LibraryTab("Альбомы", Page.LibraryAlbums, () => Run(() => ShowLibrarySectionAsync(Page.LibraryAlbums))),
            LibraryTab("Станции", Page.LibraryStations, () => Run(() => ShowLibrarySectionAsync(Page.LibraryStations))),
            LibraryTab("Подписки", Page.LibraryFollowing, () => Run(() => ShowLibrarySectionAsync(Page.LibraryFollowing))),
            LibraryTab("История", Page.LibraryHistory, () => Run(() => ShowLibrarySectionAsync(Page.LibraryHistory))));

    private Button LibraryTab(string label, Page target, Action action) => SectionTab(label, page, target, action);

    private ItemsControl CreateLikedGrid(Dictionary<Button, LikedTrackTile> tiles)
    {
        var grid = new ItemsControl().Items(Array.Empty<SoundCloudTrack>(), track => track.Title)
            .Background(Color.Transparent).BorderThickness(0).Padding(0)
            .ItemPadding(new Thickness(0, 0, 24, 0)).WrapPresenter(likedArtworkSize + 24, likedArtworkSize + 90);
        grid.ItemTemplate = new DelegateTemplate<SoundCloudTrack>(context =>
        {
            var tile = new LikedTrackTile(SelectLibraryTrack, ArtworkLayer);
            tile.SetSize(likedArtworkSize);
            AttachTrackQueueMenu(tile.Root, () => tile.Track);
            AttachTrackTitle(tile.Title, () => tile.Track);
            context.Register("tile", tile.Root);
            tiles.Add(tile.Root, tile);
            return tile.Root;
        }, (_, track, _, context) =>
        {
            var tile = tiles[context.Get<Button>("tile")];
            tile.Track = track;
            tile.Title.Text = track.Title;
            tile.Author.Text = track.Author;
            tile.SetSize(likedArtworkSize);
            tile.SetPlaying(current?.Id == track.Id, isPlaying.Value, animate: false);
            SetCardArtwork(tile.Cover, libraryCoverCache.GetValueOrDefault(track.Id), track.ArtworkUrl ?? track.User?.AvatarUrl);
            likedArtworkTracks[tile.Cover] = track.Id;
            Run(() => LoadLikedArtworkAsync(tile.Cover, track));
        }, (_, _, _, context) =>
        {
            var tile = tiles[context.Get<Button>("tile")];
            tile.Track = null;
            tile.Reset();
            StopCardArtwork(tile.Cover);
            likedArtworkTracks.Remove(tile.Cover);
        });
        grid.SizeChanged += e => UpdateLikedGridSize(e.NewSize.Width);
        return grid;
    }

    private FrameworkElement LikedTracksPage()
    {
        likedGrid = CreateLikedGrid(likedTiles);
        likedList = CreateLikedList();
        likesLoadingView = CreateLibraryLoadingView(new Grid().Columns("*").Rows("*").Children(
            likedGrid.BindIsVisible(likesAsList, value => !value), likedList.BindIsVisible(likesAsList)));
        likesAsList.Changed += () =>
        {
            likesLoadingView.Skeleton.SetGeometry(likedArtworkSize, libraryColumns, likesAsList.Value);
            if (paginationLoading.Value && page.Value == Page.LibraryTracks) RefreshPaginationSkeletons(true);
        };
        likedFilter = new TextBox().Placeholder("Фильтр по треку или исполнителю").FontSize(12)
            .Background(Raised).BorderThickness(0).Padding(12, 8).CornerRadius(6)
            .OnTextChanged(value => { likedFilterText = value; RefreshLikedViews(); });
        var tools = new StackPanel().Horizontal().Spacing(6).CenterVertical().Children(
            new TextBlock().Text("Вид").FontSize(12).Foreground(Muted).CenterVertical().Margin(0, 0, 4, 0),
            LikesViewButton("squares-four", false), LikesViewButton("list-bullets", true));
        return new DockPanel().LastChildFill().Spacing(22).Padding(0, 14).Children(
            new Grid().Columns("*,Auto,280").Rows("Auto").Spacing(16).DockTop().Children(
                LikedSectionTitle().Column(0),
                tools.Column(1), likedFilter.Column(2)),
            new Grid().Columns("*").Rows("*").Children(
                likesLoadingView.Root,
                new TextBlock().Text("По этому фильтру ничего не найдено.")
                    .Foreground(Muted).FontSize(14)
                    .TextWrapping(TextWrapping.Wrap).Top().Margin(0, 24).BindIsVisible(likedEmpty)));
    }

    private Button LikesViewButton(string icon, bool showList)
    {
        var image = Icons.View(icon, 20);
        image.Bind(Image.SourceProperty, likesAsList, value => Icons.Source(icon, value == showList ? Color.White : Muted));
        return new Button().Background(Raised).BorderThickness(0).Padding(7).Width(34).Height(34).CornerRadius(5)
            .Content(image.CenterHorizontal().CenterVertical()).OnClick(() => likesAsList.Value = showList);
    }

    private void RefreshLikedViews()
    {
        if (page.Value != Page.LibraryTracks) return;
        var filter = likedFilterText.Trim();
        var visible = tracks.Where(track => filter.Length == 0 ||
            track.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            track.Author.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        SetPageItems(likedGrid, visible, LoadingRowStyle.Card);
        SetPageItems(likedList, visible, LoadingRowStyle.Waveform);
        likedEmpty.Value = !likesLoading && visible.Length == 0;
    }

    private void UpdateLikedGridSize(double width)
    {
        if (likedGrid == null || width <= 0) return;
        // Reserve the scrollbar gutter before it appears so additional rows cannot change column count.
        var available = Math.Max(200, width - 18);
        var columns = Math.Clamp((int)Math.Floor((available + 24) / (170 + 24)), 1, 6);
        if (libraryColumns != columns) searchLayoutPending = true;
        libraryColumns = columns;
        var cellWidth = Math.Floor(available / columns);
        var artworkSize = cellWidth - 24;
        if (Math.Abs(artworkSize - likedArtworkSize) < 0.1) return;
        likedArtworkSize = artworkSize;
        foreach (var view in libraryLoadingViews)
            view.Skeleton.SetGeometry(artworkSize, columns, view == homeLoadingView || (view == searchLoadingView ? searchSection.Value == SearchSection.Tracks : view == likesLoadingView && likesAsList.Value));
        foreach (var grid in new[] { libraryGrid, likedGrid })
            if (grid != null) { grid.WrapPresenter(cellWidth, artworkSize + 90); grid.InvalidateMeasure(); }
        foreach (var tile in likedTiles.Values.Concat(libraryTiles.Values)) tile.SetSize(artworkSize);
        if (libraryGrid != null) libraryGrid.Height = Math.Max(1, Math.Ceiling(Math.Min(6, tracks.Count) / (double)columns)) * (artworkSize + 90);
        foreach (var (grid, preview) in collectionGrids)
        {
            grid.WrapPresenter(cellWidth, artworkSize + 90); grid.InvalidateMeasure();
            if (preview) grid.Height = Math.Max(1, Math.Ceiling(grid.ItemsSource.Count / (double)columns)) * (artworkSize + 90);
        }
        foreach (var (root, card) in collectionCards) SetCollectionCardSize(root, card.Cover, card.Frame);
    }

    private void RefreshLikedPlayback()
    {
        foreach (var tile in likedTiles.Values.Concat(libraryTiles.Values)) tile.SetPlaying(tile.Track != null && tile.Track.Id == current?.Id, isPlaying.Value);
        foreach (var card in collectionCards.Values)
            card.Playback.SetPlaying(card.Item.Track != null && card.Item.Track.Id == current?.Id, isPlaying.Value);
        RefreshLikedRows();
    }

    private async Task LoadLikedArtworkAsync(Image image, SoundCloudTrack track)
    {
        var source = await GetLibraryArtworkAsync(track);
        if (!disposed && likedArtworkTracks.TryGetValue(image, out var id) && id == track.Id)
            SetCardArtwork(image, source, track.ArtworkUrl ?? track.User?.AvatarUrl, finished: true);
    }
}
