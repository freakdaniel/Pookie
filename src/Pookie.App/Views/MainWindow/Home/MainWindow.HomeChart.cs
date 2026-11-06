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
    private readonly Dictionary<string, CollectionDetail> homeChartCache = new(StringComparer.Ordinal);
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

    private void RenderHomeChart(LibraryItem[] genres)
    {
        if (!homeChartGenres.SequenceEqual(genres))
        {
            var previous = homeChartGenre?.Key;
            homeChartGenres = genres;
            homeChartGenreSlot.Clear();
            var selected = Array.FindIndex(genres, item => item.Key == previous);
            if (selected < 0) selected = Array.FindIndex(genres, item => item.Playlist?.Urn.EndsWith(AllGenresSuffix, StringComparison.Ordinal) == true);
            if (selected < 0 && genres.Length > 0) selected = 0;
            if (selected >= 0)
            {
                var picker = new MaterialSelect(genres.Select(item =>
                    item.Playlist?.Urn.EndsWith(AllGenresSuffix, StringComparison.Ordinal) == true ? "Все жанры" : item.Title).ToArray())
                    .Width(196).Height(40).FontSize(14);
                picker.SelectedIndex = selected;
                picker.SelectionChanged += index => { if (index >= 0) SelectHomeChart(genres[index]); };
                homeChartGenreSlot.Add(picker);
            }
            SelectHomeChart(selected < 0 ? null : genres[selected]);
        }
        homeChart.IsVisible = genres.Length > 0 || homePending.Contains("trends");
        LoadHomeChart();
        RefreshHomeChartLoading();
    }

    private void SelectHomeChart(LibraryItem? genre)
    {
        if (Equals(homeChartGenre, genre)) return;
        CancelHomeChartRequest();
        homeChartGenre = genre;
        homeChartFailed = false;
        SetHomeChartDetail(null);
        LoadHomeChart();
    }

    private void LoadHomeChart()
    {
        if (disposed || page.Value != Page.Home || homeChartGenre is not { } genre || api.Session is not { } session ||
            homeChartDetail != null || homeChartRequest != null || homeChartFailed) return;
        if (homeChartCache.TryGetValue(genre.Key, out var cached)) { SetHomeChartDetail(cached); return; }
        var request = homeChartRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = request.Token;
        SetHomeChartStatus("");
        RefreshHomeChartLoading();
        Run(async () =>
        {
            try
            {
                var detail = await api.GetCollectionDetailAsync(genre, token);
                if (token.IsCancellationRequested || disposed || api.Session != session ||
                    homeChartGenre?.Key != genre.Key || !ReferenceEquals(homeChartRequest, request)) return;
                homeChartCache[genre.Key] = detail;
                SetHomeChartDetail(detail);
            }
            catch (Exception error) when (!token.IsCancellationRequested && api.Session == session &&
                error is SoundCloudException { StatusCode: not 401 } or HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
            {
                homeChartFailed = true;
                RefreshHomeChartLoading();
                SetHomeChartStatus("Не удалось загрузить чарт.");
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
        homeChartDetail = detail;
        foreach (var row in homeChartRows) { BindCompactTrackRow(row, null); compactTrackRows.Remove(row); }
        homeChartRows.Clear();
        homeChartLeft.Clear();
        homeChartRight.Clear();
        var tracks = detail?.Tracks.Tracks.Take(ChartColumnSize * 2).ToArray() ?? [];
        SetHomeChartStatus(detail != null && tracks.Length == 0 ? "В этом чарте пока нет треков." : "");
        for (var index = 0; index < tracks.Length; index++)
        {
            var row = CreateCompactTrackRow(track => Run(() => PlayHomeChartTrackAsync(track)));
            BindCompactTrackRow(row, tracks[index]);
            row.PlaybackButton.IsEnabled = HomeRecommendations.Playable(tracks[index]);
            homeChartRows.Add(row);
            (index < ChartColumnSize ? homeChartLeft : homeChartRight).Add(row.Root);
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
