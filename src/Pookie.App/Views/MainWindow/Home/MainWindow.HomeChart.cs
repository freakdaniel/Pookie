using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Home;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private const int ChartColumnSize = 5;
    private const int ChartRowHeight = 64;
    private const int ChartRowSpacing = 4;
    private const string AllGenresSuffix = ":trending-by-genre:all-genres";
    private LibraryItem[] homeChartGenres = [];
    private readonly Dictionary<string, (CollectionDetail Detail, long Revision)> homeChartCache = new(StringComparer.Ordinal);
    private long homeChartRevision;
    private readonly List<CompactTrackRow> homeChartRows = [];
    private LibraryItem? homeChartGenre;
    private CollectionDetail? homeChartDetail;
    private CancellationTokenSource? homeChartRequest;
    private StackPanel homeChart = null!;
    private StackPanel homeChartGenreSlot = null!, homeChartLeft = null!, homeChartRight = null!;
    private Grid homeChartColumns = null!;
    private LibraryLoadingView homeChartLeftLoading = null!, homeChartRightLoading = null!;
    private TextBlock homeChartStatus = null!;
    private Button homeChartOpen = null!, homeChartRetry = null!;
    private bool homeChartFailed, homeChartWide = true;

    private FrameworkElement HomeChartView()
    {
        homeChartGenreSlot = new StackPanel().Vertical();
        homeChartOpen = TrackButtons.Action(new TextBlock().Text("Смотреть всё →").FontSize(13).SemiBold(),
            () => { if (homeChartGenre is { } genre) Run(() => OpenLibraryItemAsync(genre)); }, TrackButtons.Text, 40);
        homeChartRetry = TrackButtons.Action(new TextBlock().Text("Повторить").FontSize(13),
            () => { homeChartFailed = false; LoadHomeChart(); }, TrackButtons.Text, 32).Left().IsVisible(false);
        homeChartStatus = new TextBlock().FontSize(14).Foreground(Muted).Margin(4, 10).IsVisible(false);
        homeChartLeft = new StackPanel().Vertical().Spacing(ChartRowSpacing);
        homeChartRight = new StackPanel().Vertical().Spacing(ChartRowSpacing);
        homeChartLeftLoading = CreateLibraryLoadingView(homeChartLeft, preview: true);
        homeChartRightLoading = CreateLibraryLoadingView(homeChartRight, preview: true);
        foreach (var loading in new[] { homeChartLeftLoading, homeChartRightLoading })
        {
            loading.Skeleton.ConfigureLoadingRow(44, 1, LoadingRowStyle.Compact);
            loading.Skeleton.PreviewItems = ChartColumnSize;
            loading.Skeleton.CompactRowSpacing = ChartRowSpacing;
            loading.Skeleton.Height = double.NaN;
        }
        homeChartColumns = new Grid().Columns("*,*").Rows("Auto").Spacing(24)
            .Children(homeChartLeftLoading.Root, homeChartRightLoading.Root.Column(1));
        homeChartColumns.SizeChanged += e =>
        {
            var wide = e.NewSize.Width >= 720;
            if (wide == homeChartWide) return;
            homeChartWide = wide;
            homeChartColumns.Columns(wide ? "*,*" : "*").Rows(wide ? "Auto" : "Auto,Auto").Spacing(wide ? 24 : 4);
            homeChartRightLoading.Root.Column(wide ? 1 : 0).Row(wide ? 0 : 1);
            ReserveHomeChartRows();
        };
        var heading = new Grid().Columns("*,Auto,Auto").Rows("Auto").Spacing(10).Children(
            new StackPanel().Vertical().Spacing(4).CenterVertical().Children(
                new TextBlock().Text("Тренды").FontSize(22).Bold(),
                new TextBlock().Text("Популярные треки SoundCloud").FontSize(13).Foreground(Muted)
                    .TextTrimming(TextTrimming.CharacterEllipsis)),
            homeChartGenreSlot.Column(1).CenterVertical(), homeChartOpen.Column(2).CenterVertical());
        page.Changed += () => { if (page.Value != Page.Home) CancelHomeChartRequest(); };
        var body = new Grid().Columns("*").Rows("Auto").Children(homeChartColumns,
            new StackPanel().Vertical().Top().Children(homeChartStatus, homeChartRetry));
        return homeChart = new StackPanel().Vertical().Spacing(16).Children(heading, body).IsVisible(false);
    }

    private static string HomeChartGenreName(LibraryItem item) =>
        item.Playlist?.Urn.EndsWith(AllGenresSuffix, StringComparison.Ordinal) == true ? "Все жанры" : item.Title;

    private void RenderHomeChart(LibraryItem[] genres, bool refresh)
    {
        if (refresh)
        {
            // Previously viewed genres stay available until their next response.
            homeChartRevision++;
            homeChartFailed = false;
        }
        var choicesChanged = !ReferenceEquals(homeChartGenres, genres) &&
            !homeChartGenres.Select(item => (item.Key, Name: HomeChartGenreName(item)))
                .SequenceEqual(genres.Select(item => (item.Key, Name: HomeChartGenreName(item))));
        homeChartGenres = genres;
        if (choicesChanged)
        {
            var previous = homeChartGenre?.Key;
            homeChartGenreSlot.Clear();
            var selected = Array.FindIndex(genres, item => item.Key == previous);
            if (selected < 0) selected = Array.FindIndex(genres, item => item.Playlist?.Urn.EndsWith(AllGenresSuffix, StringComparison.Ordinal) == true);
            if (selected < 0 && genres.Length > 0) selected = 0;
            if (selected >= 0)
            {
                var picker = new MaterialSelect(genres.Select(HomeChartGenreName).ToArray())
                    .Width(196).Height(40).FontSize(14);
                picker.SelectedIndex = selected;
                picker.SelectionChanged += index =>
                {
                    if (index < 0 || index >= homeChartGenres.Length) return;
                    SelectHomeChart(homeChartGenres[index]);
                    LoadHomeChart();
                };
                homeChartGenreSlot.Add(picker);
            }
            SelectHomeChart(selected < 0 ? null : genres[selected]);
        }
        else if (homeChartGenre is { } currentGenre)
            homeChartGenre = genres.First(item => item.Key == currentGenre.Key);
        homeChart.IsVisible = genres.Length > 0 || homePending.Contains("trends");
        LoadHomeChart();
        RefreshHomeChartLoading();
    }

    private void SelectHomeChart(LibraryItem? genre)
    {
        if (homeChartGenre?.Key == genre?.Key) { homeChartGenre = genre; return; }
        CancelHomeChartRequest();
        homeChartGenre = genre;
        homeChartFailed = false;
        SetHomeChartDetail(genre == null ? null : homeChartCache.GetValueOrDefault(genre.Key).Detail);
    }

    private void LoadHomeChart()
    {
        if (disposed || page.Value != Page.Home || homeChartGenre is not { } genre || api.Session is not { } session ||
            homeChartRequest != null || homeChartFailed) return;
        if (homeChartCache.TryGetValue(genre.Key, out var cached) && cached.Revision == homeChartRevision) return;
        var request = homeChartRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = request.Token;
        if (homeChartDetail == null) SetHomeChartStatus("");
        RefreshHomeChartLoading();
        Run(async () =>
        {
            try
            {
                var detail = await api.GetCollectionDetailAsync(genre, token);
                if (token.IsCancellationRequested || disposed || api.Session != session ||
                    homeChartGenre?.Key != genre.Key || !ReferenceEquals(homeChartRequest, request)) return;
                homeChartCache[genre.Key] = (detail, homeChartRevision);
                SetHomeChartDetail(detail);
            }
            catch (Exception error) when (!token.IsCancellationRequested && api.Session == session &&
                error is SoundCloudException { StatusCode: not 401 } or HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
            {
                homeChartFailed = true;
                RefreshHomeChartLoading();
                if (homeChartDetail == null) SetHomeChartStatus("Не удалось загрузить чарт.");
            }
            finally
            {
                if (ReferenceEquals(homeChartRequest, request)) { homeChartRequest = null; request.Dispose(); }
            }
        });
    }

    private void ResetHomeChartCache()
    {
        CancelHomeChartRequest();
        homeChartCache.Clear();
        homeChartFailed = false;
        if (homeChartDetail != null) SetHomeChartDetail(null);
    }

    private void CancelHomeChartRequest()
    {
        if (homeChartRequest is { } request)
        {
            homeChartRequest = null;
            request.Cancel();
            request.Dispose();
        }
        RefreshHomeChartLoading();
    }

    private void SetHomeChartStatus(string text)
    {
        homeChartStatus.Text = text;
        homeChartStatus.IsVisible = text.Length > 0;
        homeChartRetry.IsVisible = homeChartFailed;
    }

    private void SetHomeChartDetail(CollectionDetail? detail)
    {
        var animate = page.Value == Page.Home && detail != null && homeChartDetail?.Item.Key == detail.Item.Key &&
            homeChartRows.Count > 0 && homeChartColumns.Bounds.Bottom > homeScroll.Bounds.Y &&
            homeChartColumns.Bounds.Y < homeScroll.Bounds.Bottom;
        homeChartDetail = detail;
        var tracks = detail?.Tracks.Tracks.DistinctBy(track => track.Id).Take(ChartColumnSize * 2).ToArray() ?? [];
        var wanted = tracks.Select(track => track.Id).ToHashSet();
        var previous = homeChartRows.Select((row, rank) => (Row: row, Rank: rank)).ToDictionary(entry => entry.Row.Track!.Id);
        var focused = Window.FocusManager.FocusedElement;
        var keyboardFocus = focused is Control control && KeyboardFocusFeedback.IsVisible(control);
        foreach (var row in homeChartRows)
        {
            if (wanted.Contains(row.Track!.Id)) continue;
            var motion = (LibraryItemMotion)row.Root.LogicalParent!;
            BindCompactTrackRow(row, null);
            compactTrackRows.Remove(row);
            ((StackPanel)motion.LogicalParent!).Remove(motion);
            motion.Dispose();
        }
        homeChartRows.Clear();
        SetHomeChartStatus(detail != null && tracks.Length == 0 ? "В этом чарте пока нет треков." : "");
        for (var index = 0; index < tracks.Length; index++)
        {
            var track = tracks[index];
            var retained = previous.TryGetValue(track.Id, out var old);
            var row = retained ? old.Row : CreateCompactTrackRow(item => Run(() => PlayHomeChartTrackAsync(item)));
            var motion = retained ? (LibraryItemMotion)row.Root.LogicalParent! : new LibraryItemMotion().Content(row.Root);
            var column = index < ChartColumnSize ? homeChartLeft : homeChartRight;
            var slot = index % ChartColumnSize;
            if (animate && (!retained || old.Rank != index))
                motion.Retarget(retained ? motion.PaintedPosition : null, appear: !retained, afterArrange: true);
            if (slot >= column.Children.Count || !ReferenceEquals(column.Children[slot], motion))
                column.Insert(slot, motion);
            if (!Equals(row.Track, track))
            {
                BindCompactTrackRow(row, track);
                row.PlaybackButton.IsEnabled = HomeRecommendations.Playable(track);
            }
            homeChartRows.Add(row);
        }
        if (focused?.FindVisualRoot() == Window && Window.FocusManager.FocusedElement == null)
        {
            var offset = shellScroll.VerticalOffset;
            KeyboardFocusFeedback.Restore(focused, keyboardFocus);
            if (shellScroll.VerticalOffset != offset) shellScroll.SetScrollOffsets(0, offset);
        }
        RefreshHomeChartLoading();
    }

    private void RefreshHomeChartLoading()
    {
        var loading = !disposed && page.Value == Page.Home && !homeChartFailed && homeChartDetail == null &&
            (homeChartGenre != null || homePending.Contains("trends"));
        homeChartLeftLoading.SetLoading(loading);
        homeChartRightLoading.SetLoading(loading);
        ReserveHomeChartRows();
    }

    private void ReserveHomeChartRows()
    {
        var rows = homeChartWide ? ChartColumnSize : ChartColumnSize * 2;
        // Keep the chart header under the pointer while the next genre loads.
        homeChartColumns.MinHeight = homeChartLeftLoading.Loading
            ? rows * (ChartRowHeight + ChartRowSpacing) - ChartRowSpacing : 0;
    }

    private async Task PlayHomeChartTrackAsync(SoundCloudTrack track)
    {
        if (homeChartDetail is not { } chart || !HomeRecommendations.Playable(track)) return;
        if (current?.Id == track.Id && playbackQueue.Context?.Key == chart.Item.Key) { await ToggleAsync(); return; }
        var tracks = chart.Tracks.Tracks.Where(HomeRecommendations.Playable).ToArray();
        StartQueueContext(new(chart.Item.Key, chart.Item.Title, "Чарт SoundCloud", chart.Tracks.NextHref,
            Kind: PlaybackContextKind.Playlist, ResourceUrn: PlaybackHistoryContextUrn(chart.Item)), tracks, track, preserveManual: true);
        await PlayAsync(track, queueAdvance: true);
    }
}
