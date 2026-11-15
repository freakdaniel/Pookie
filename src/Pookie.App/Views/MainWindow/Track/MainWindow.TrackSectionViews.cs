using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ItemsControl trackSectionTrackList = null!, trackSectionCardList = null!;
    private readonly Dictionary<Grid, LikedTrackRow> trackSectionRowPool = [];
    private readonly List<TrackSectionCardView> trackSectionCardPool = [];
    private int trackSectionColumns;
    private double trackSectionCardWidth;

    private sealed class TrackSectionCardRow(string key, LibraryItem[] items, int columns)
    {
        internal string Key = key;
        internal LibraryItem[] Items = items;
        internal int Columns = columns;
        public override bool Equals(object? other) => other is TrackSectionCardRow row && row.Key == Key;
        public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);
    }
    private sealed class TrackSectionCardView(Grid root)
    {
        internal readonly Grid Root = root;
        internal readonly List<CollectionCardView> Cards = [];
        internal TrackSectionCardRow? Row;
        internal long Revision;
    }

    private FrameworkElement TrackSectionTracksView()
    {
        trackSectionTrackList = new ItemsControl().Items(Array.Empty<SoundCloudTrack>(), track => track.Title)
            .FixedHeightPresenter().ItemHeight(TrackRowLayout.Stride).ItemPadding(new Thickness(0, 0, 0, TrackRowLayout.Gap))
            .Background(Color.Transparent).BorderThickness(0).Padding(0);
        trackSectionTrackList.ItemTemplate = new DelegateTemplate<SoundCloudTrack>(context =>
        {
            var row = CreatePooledLibraryTrackRow(); trackSectionRowPool.Add(row.Root, row);
            context.Register("row", row.Root); return row.Root;
        }, (_, track, _, context) =>
        {
            var row = trackSectionRowPool[context.Get<Grid>("row")];
            BindLibraryTrackRow(row, track); if (!trackSectionRows.Contains(row)) trackSectionRows.Add(row);
        }, (_, _, _, context) =>
        {
            if (!trackSectionRowPool.TryGetValue(context.Get<Grid>("row"), out var row)) return;
            ClearLibraryTrackRow(row); trackSectionRows.Remove(row);
        });
        return new ViewportItemsHost(trackSectionTrackList, () => trackDetailScroll.Bounds, ScrollShellWheel,
            delta => trackDetailScroll.SetScrollOffsets(0, trackDetailScroll.VerticalOffset + delta)).Initialize();
    }

    private FrameworkElement TrackSectionCardsView()
    {
        trackSectionCardList = new ItemsControl().Items(Array.Empty<TrackSectionCardRow>(), row => row.Key)
            .FixedHeightPresenter().ItemHeight(250).ItemPadding(new Thickness(0, 0, 0, 24))
            .Background(Color.Transparent).BorderThickness(0).Padding(0);
        trackSectionCardList.ItemTemplate = new DelegateTemplate<TrackSectionCardRow>(context =>
        {
            var view = new TrackSectionCardView(new Grid().Columns("*").Rows("*").Spacing(24));
            trackSectionCardPool.Add(view); context.Register("root", view.Root); return view.Root;
        }, (_, row, _, context) =>
        {
            var view = trackSectionCardPool.First(v => ReferenceEquals(v.Root, context.Get<Grid>("root")));
            ClearTrackSectionCards(view); view.Row = row; var revision = ++view.Revision;
            var generation = navigationGeneration; var trackId = trackDetailState!.Track.Id;
            view.Root.Columns(string.Join(',', Enumerable.Repeat("*", row.Columns)));
            for (var i = 0; i < row.Items.Length; i++)
            {
                if (i == view.Cards.Count)
                {
                    var created = CreateCollectionCardView(responsive: true);
                    created.Root.OnClick(() => { if (created.Item is { } item) Run(() => OpenLibraryItemAsync(item)); });
                    view.Cards.Add(created); view.Root.Add(created.Root.Column(i));
                }
                var card = view.Cards[i]; var item = row.Items[i];
                card.Root.IsVisible = true; card.Item = item; card.SetArtist(item.User != null);
                card.Title.Text = item.Title; card.Author.Text = item.Subtitle; card.Playback.IsVisible = item.Playlist != null;
                SetCollectionArtwork(card.Cover, item); RefreshCollectionCardPlayback(card, animate: false);
                Run(async () =>
                {
                    var source = await GetCollectionArtworkAsync(item);
                    if (view.Revision == revision && ReferenceEquals(card.Item, item) && IsTrackDetailCurrent(trackId, generation))
                        SetCollectionArtwork(card.Cover, item, source, finished: true);
                });
            }
        }, (_, _, _, context) =>
        {
            var view = trackSectionCardPool.FirstOrDefault(v => ReferenceEquals(v.Root, context.Get<Grid>("root")));
            if (view != null) ClearTrackSectionCards(view);
        });
        return new ViewportItemsHost(trackSectionCardList, () => trackDetailScroll.Bounds, ScrollShellWheel,
            delta => trackDetailScroll.SetScrollOffsets(0, trackDetailScroll.VerticalOffset + delta), ConfigureTrackSectionCards, trailingGap: 24).Initialize();
    }

    private void ConfigureTrackSectionCards(double width)
    {
        var columns = Math.Clamp((int)((width + 24) / 194), 1, 6);
        var cover = Math.Max(0, (width - 24 * (columns - 1)) / columns);
        var items = trackDetailState?.Sections.GetValueOrDefault(trackDetailSection.Value)?.Items ?? [];
        trackSectionCardList.ItemHeight = cover + (items.Any(item => item.User != null) ? 53 : 48) + 24;
        if (columns == trackSectionColumns && Math.Abs(cover - trackSectionCardWidth) < .1) return;
        trackSectionColumns = columns; trackSectionCardWidth = cover;
        if (trackDetailState != null && trackDetailSection.Value != TrackSection.Overview)
            SetTrackSectionCardItems(trackDetailState.Sections.GetValueOrDefault(trackDetailSection.Value)?.Items ?? []);
    }

    private void SetTrackSectionCardItems(LibraryItem[] items)
    {
        var rows = items.Where(item => item.Track == null).Chunk(Math.Max(1, trackSectionColumns))
            .Select(chunk => new TrackSectionCardRow(chunk[0].Key, chunk, Math.Max(1, trackSectionColumns))).ToArray();
        SetPageItems(trackSectionCardList, rows, LoadingRowStyle.CardRow,
            (a, b) => a is TrackSectionCardRow x && b is TrackSectionCardRow y && x.Columns == y.Columns && x.Items.SequenceEqual(y.Items));
    }

    private void ClearTrackSectionCards(TrackSectionCardView view)
    {
        view.Row = null; ++view.Revision;
        foreach (var card in view.Cards)
        { card.Item = null; card.Root.IsVisible = false; card.Playback.Reset(); ClearCardArtwork(card.Cover); }
    }

    private IEnumerable<CollectionCardView> BoundTrackSectionCards => trackSectionCardPool.SelectMany(view => view.Cards).Where(card => card.Item != null);

    private void DisposeTrackSectionViews()
    {
        foreach (var row in trackSectionRowPool.Values) { ClearLibraryTrackRow(row); row.ReleaseRenderingResources(); likedRows.Remove(row.Root); }
        trackSectionRowPool.Clear(); trackSectionRows.Clear();
        foreach (var view in trackSectionCardPool) ClearTrackSectionCards(view);
        trackSectionCardPool.Clear();
    }
}
