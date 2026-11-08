using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> playlistCollectionVisible = new(false);
    private readonly Storage.ResourceCache<string, CollectionDetail> collectionDetails = new(64, new Storage.CacheBudget(16 * 1024 * 1024), detail => 512L + detail.Tracks.Tracks.Length * 1024L);
    private ItemsControl collectionTrackList = null!;
    private ItemsControl collectionTrackCards = null!;
    private ItemsControl collectionTrackWaves = null!;
    private readonly ObservableValue<TrackViewMode> collectionMode = new(TrackViewMode.List);
    private readonly Dictionary<Grid, (LikedTrackRow Row, int Position)> collectionWaveRows = [];
    private readonly Dictionary<Button, (LikedTrackTile Tile, int Position)> collectionTrackTiles = [];
    private double collectionTrackArtworkSize = 176;
    private Image collectionCover = null!, collectionBackdrop = null!, collectionPlayIcon = null!;
    private TextBlock collectionTitle = null!, collectionAuthor = null!, collectionMeta = null!, collectionDescription = null!;
    private Button collectionPlay = null!, collectionCopy = null!, collectionDescriptionToggle = null!;
    private TextBox collectionFilter = null!;
    private StackPanel collectionDescriptionHost = null!;
    private ScrollViewer collectionDescriptionScroll = null!;
    private TextBlock collectionEmpty = null!;
    private string collectionFilterText = "";
    private bool collectionDescriptionExpanded;
    private sealed record CollectionTrackEntry(SoundCloudTrack Track, int Position);
    private LibraryItem? displayedCollection;
    private SoundCloudUser? CollectionAuthor => displayedCollection?.Playlist?.User;
    private ItemsControl CollectionTrackView => collectionMode.Value switch { TrackViewMode.Waveform => collectionTrackWaves, TrackViewMode.List => collectionTrackList, _ => collectionTrackCards };
    private ScrollViewer? CollectionScroll => (playlistCollectionVisible.Value ? CollectionTrackView : collectionGrid).FindVisualChild<ScrollViewer>() as ScrollViewer;

    private FrameworkElement PlaylistCollectionPage()
    {
        collectionTrackList = CreateCollectionTrackList();
        collectionTrackCards = CreateCollectionTrackCards();
        collectionTrackWaves = CreateCollectionTrackWaves();
        collectionMode.Changed += () => { RefreshCollectionTracks(); CollectionScroll?.SetScrollOffsets(0, 0); };
        collectionCover = new Image().StretchMode(Stretch.UniformToFill);
        collectionBackdrop = new Image().StretchMode(Stretch.UniformToFill).IsHitTestVisible(false);
        collectionBackdrop.Transitions = [Transition.Create(UIElement.OpacityProperty, 240)];
        collectionTitle = new TextBlock().FontSize(28).Bold().TextWrapping(TextWrapping.Wrap).MaxHeight(70).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionAuthor = new TextBlock().FontSize(14).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionMeta = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionDescription = new TextBlock().FontSize(14).FontWeight(FontWeight.Normal).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(48).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionPlayIcon = Icons.View("play-solid", 28, TrackButtons.OnSurface).Center();
        collectionPlay = TrackButtons.Icon(collectionPlayIcon,
            () => Run(PlayDisplayedCollectionAsync), TrackButtons.Glass, 64);
        var author = TrackButtons.Action(collectionAuthor, () =>
        {
            if (CollectionAuthor is { } user) Run(() => OpenLibraryItemAsync(new("user:" + user.Id, user.Username, "Исполнитель", user.AvatarUrl, User: user)));
        }, TrackButtons.Text, 20).Padding(0).Left();
        collectionCopy = TrackButtons.Icon(Icons.View("copy", 20), () =>
        { if (TrackLink(displayedCollection?.Playlist?.PermalinkUrl) is { } uri) CopyText(uri.AbsoluteUri); }, TrackButtons.Glass, 44).Hint("Скопировать ссылку");
        collectionDescriptionToggle = TrackButtons.Action(new TextBlock().Text("Подробнее").FontSize(13), () =>
        {
            collectionDescriptionExpanded = !collectionDescriptionExpanded;
            RefreshCollectionDescription();
        }, TrackButtons.Text, 32).Padding(0).Left();
        var titleBlock = new Grid().Columns("64,*").Rows("Auto").Spacing(18).Children(collectionPlay.Column(0).Top(),
            new StackPanel().Vertical().Spacing(7).Column(1).Children(collectionTitle,
                new Grid().Columns("Auto,*").Rows("Auto").Spacing(6).Children(author.Column(0).CenterVertical(),
                    collectionMeta.Column(1).CenterVertical())));
        var coverFrame = new Border().CornerRadius(10).ClipToBounds().Child(ArtworkLayer(collectionCover));
        var more = TrackButtons.Icon(Icons.View("dots-three", 22), () => { }, TrackButtons.Glass, 44);
        var menu = PopupAppearance.CreateMenu(MenuPlacement.Below);
        void MenuItem(string id, string label, string icon, Action execute, Func<bool> enabled)
        {
            var command = PopupAppearance.MenuCommand("pookie.collection." + id, label, icon);
            more.Commands.Register(command, execute, enabled); menu.Item(command);
        }
        MenuItem("queue", "Добавить в очередь", "queue", EnqueueDisplayedCollection, () => activeCollection?.Items.Any(item => item.Track != null) == true);
        MenuItem("edit", "Редактировать плейлист", "text-align-left", () => OpenPlaylistEditor(), CanEditDisplayedPlaylist);
        MenuItem("artwork", "Изменить обложку", "image", () => Run(PickPlaylistArtworkAsync), CanEditDisplayedPlaylist);
        MenuItem("delete", "Удалить плейлист", "trash", () => OpenPlaylistEditor(deleting: true), CanEditDisplayedPlaylist);
        MenuItem("open", "Открыть в SoundCloud", "arrow-square-out", () => OpenSoundCloudPage(displayedCollection?.Playlist?.PermalinkUrl),
            () => TrackLink(displayedCollection?.Playlist?.PermalinkUrl) != null);
        PopupAppearance.Attach(more, menu); more.OnClick(() => menu.Toggle(more));
        CreateCollectionActionButtons();
        var hero = new TrackPageColumns(
            new Grid().Columns("*").Rows("Auto,*,44").Children(titleBlock.Row(0),
                new StackPanel().Horizontal().Spacing(TrackButtons.ActionSpacing).Row(2).Children(TrackButtons.Connected(collectionLike, collectionRepost),
                    TrackButtons.Icon(Icons.View("arrow-square-out", 20), () => { if (displayedCollection is { } item) OpenShare(item); }, TrackButtons.Glass, 44).Hint("Поделиться"), collectionCopy,
                    ArtistFollowButton(() => CollectionAuthor, TrackButtons.Glass, 44), more)),
            coverFrame, true, collectionBackdrop, new Border().Background(ArtworkBackdrop.CardScrim), new ArtworkGlassSheen())
            { HeroArtworkMinWidth = 176, HeroArtworkMaxWidth = 208 }.Initialize();
        collectionGlass = AttachCardGlass(hero);
        var header = new CardShadow(new Border().CornerRadius(20).ClipToBounds().Background(Raised).Child(hero))
            .Margin(-12, -8, -12, -16);
        collectionFilter = new TextBox().Placeholder("Фильтр по треку или исполнителю").OnTextChanged(value =>
        {
            collectionFilterText = value; RefreshCollectionTracks();
            CollectionScroll?.SetScrollOffsets(0, 0);
        });
        collectionEmpty = new TextBlock().FontSize(14).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Margin(8, 12, 0, 0).Top().IsVisible(false);
        var tools = new Grid().Columns("*,Auto,320").Rows("Auto").Spacing(16).Children(
            new TextBlock().Text("Треки").FontSize(22).Bold().Margin(8, 0, 0, 0).CenterVertical().Column(0),
            TrackViewSwitch(collectionMode).Column(1), MaterialSearchField.CreateGlass(collectionFilter, liveGlass: true).Column(2));
        collectionDescriptionScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).MaxHeight(180).Content(collectionDescription);
        collectionDescriptionHost = new StackPanel().Vertical().Spacing(4).Margin(24, 0, 24, 0)
            .Children(collectionDescriptionScroll,
                collectionDescriptionToggle);
        return new DockPanel().LastChildFill().Spacing(18).Children(header.DockTop(), collectionDescriptionHost.DockTop(), tools.DockTop(),
            new Grid().Columns("*").Rows("*").Children(collectionTrackList, collectionTrackCards, collectionTrackWaves, collectionEmpty));
    }

    private ItemsControl CreateCollectionTrackList()
    {
        var result = new ItemsControl().Items(Array.Empty<CollectionTrackEntry>(), entry => entry.Track.Title)
            .ItemHeight(64).FixedHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .ItemPadding(new Thickness(0));
        result.ItemTemplate = new DelegateTemplate<CollectionTrackEntry>(context =>
        {
            CompactTrackRow row = null!;
            row = CreateCompactTrackRow(track => Run(() => PlayCollectionTrackAsync(track, row.SourceIndex)), collectionRow: true);
            context.Register("row", row.Root); return row.Root;
        }, (_, entry, _, context) =>
        {
            var row = compactTrackRows.Single(row => row.Root == context.Get<Grid>("row"));
            row.SourceIndex = entry.Position;
            BindCompactTrackRow(row, entry.Track);
        }, (_, _, _, context) =>
        {
            var row = compactTrackRows.Single(row => row.Root == context.Get<Grid>("row"));
            row.SourceIndex = -1; BindCompactTrackRow(row, null);
        });
        return result;
    }

    private async Task PlayCollectionTrackAsync(SoundCloudTrack track, int sourceIndex)
    {
        if (!playlistCollectionVisible.Value || page.Value != Page.LibraryCollection) return;
        if (playbackQueue.Context?.Key == displayedCollection?.Key && current?.Id == track.Id && playbackQueue.Current?.SourceIndex == sourceIndex)
        { await ToggleAsync(); return; }
        SetQueue(track, sourceIndex); await PlayAsync(track);
    }

    private void RefreshCollectionTracks()
    {
        if (!playlistCollectionVisible.Value) return;
        var filter = collectionFilterText.Trim();
        var entries = (activeCollection?.Items ?? []).Select((item, index) => (item.Track, index))
            .Where(item => item.Track != null && (filter.Length == 0 || item.Track.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.Track.Author.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .Select(item => new CollectionTrackEntry(item.Track!, item.index)).ToArray();
        SetPageItems(collectionTrackList, entries, LoadingRowStyle.Compact);
        SetPageItems(collectionTrackCards, entries, LoadingRowStyle.Card);
        SetPageItems(collectionTrackWaves, entries, LoadingRowStyle.Waveform);
        // The fixed-height presenter retains its last arranged rows when its
        // source becomes empty. Hide the entire list, including retained paint
        // and hit targets, until the filter has rows to arrange again.
        collectionTrackList.IsVisible = entries.Length > 0 && collectionMode.Value == TrackViewMode.List;
        collectionTrackCards.IsVisible = entries.Length > 0 && collectionMode.Value == TrackViewMode.Cards;
        collectionTrackWaves.IsVisible = entries.Length > 0 && collectionMode.Value == TrackViewMode.Waveform;
        collectionLoadingView.Skeleton.CompactList = collectionMode.Value == TrackViewMode.List;
        collectionLoadingView.Skeleton.SetGeometry(collectionTrackArtworkSize, Math.Max(1, (int)(collectionTrackCards.ActualWidth / (collectionTrackArtworkSize + 24))), collectionMode.Value != TrackViewMode.Cards);
        collectionEmpty.Text = filter.Length > 0 ? "По этому фильтру ничего не найдено." : "Здесь пока нет треков.";
        collectionEmpty.IsVisible = entries.Length == 0;
    }

    private void RefreshCollectionDescription()
    {
        RefreshCollectionDescriptionGeometry();
        collectionDescription.MaxHeight = collectionDescriptionExpanded ? double.PositiveInfinity : 48;
        collectionDescriptionToggle.IsVisible = collectionDescription.Text.Length > 180 || collectionDescription.Text.Count(c => c == '\n') > 1;
        ((TextBlock)collectionDescriptionToggle.Content!).Text = collectionDescriptionExpanded ? "Свернуть" : "Подробнее";
    }

    private void RefreshCollectionDescriptionGeometry()
    {
        if (collectionDescriptionScroll?.Bounds.Y is > 0)
            collectionDescriptionScroll.MaxHeight = Math.Clamp(Window.ClientSize.Height - collectionDescriptionScroll.Bounds.Y - 180, 48, 180);
    }

    private void EnqueueDisplayedCollection() =>
        EnqueueCollectionTracks(activeCollection?.Items.Select(item => item.Track).OfType<SoundCloudTrack>() ?? []);

    private void SetCollectionPresentation(LibraryItem? item)
    {
        if (displayedCollection?.Key != item?.Key)
        {
            collectionBackdrop.Source = null; collectionBackdrop.Opacity = 0;
            collectionGlass?.RefreshArtwork();
            collectionFilter.Text = ""; collectionFilterText = "";
            collectionDescriptionExpanded = false;
            collectionActionStatusKey = null;
        }
        displayedCollection = item;
        playlistCollectionVisible.Value = page.Value == Page.LibraryCollection && item?.Playlist != null;
        if (!playlistCollectionVisible.Value) return;
        collectionCopy.IsEnabled = TrackLink(item?.Playlist?.PermalinkUrl) != null;
        collectionTitle.Text = item!.Title; collectionAuthor.Text = item.Playlist?.User?.Username ?? item.Subtitle;
        var playlist = item.Playlist!;
        var data = activeCollection?.Items.Select(entry => entry.Track).OfType<SoundCloudTrack>().ToArray() ?? [];
        var count = Math.Max(playlist.TrackCount, data.Length);
        var milliseconds = playlist.Duration > 0 ? playlist.Duration : data.Sum(track => track.Duration);
        collectionMeta.Text = "· " + string.Join(" · ", new[] { playlist.Genre,
            count + " " + RussianPlural(count, "трек", "трека", "треков"),
            milliseconds > 0 ? FormatTime(milliseconds / 1000) : null,
            playlist.Sharing == "private" ? "Приватный" : null,
            DetailAge(playlist.CreatedAt ?? playlist.LastModified) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        collectionDescription.Text = playlist.Description ?? playlist.ShortDescription ?? "";
        collectionDescriptionHost.IsVisible = collectionDescription.IsVisible = !string.IsNullOrWhiteSpace(collectionDescription.Text);
        RefreshCollectionDescription();
        SetCollectionArtwork(collectionCover, item);
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(item);
            if (disposed || generation != navigationGeneration || !ReferenceEquals(displayedCollection, item)) return;
            SetCollectionArtwork(collectionCover, item, source, finished: true);
            var background = source == null ? null : await searchBackdrops.GetValue(source, artwork => Task.Run(() =>
            {
                try { return ArtworkBackdrop.Create(artwork); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { return null; }
            }, lifetime.Token));
            if (disposed || generation != navigationGeneration || !ReferenceEquals(displayedCollection, item)) return;
            collectionBackdrop.Source = background; collectionBackdrop.Opacity = background == null ? 0 : 1;
            collectionGlass?.RefreshArtwork();
        });
        RefreshCollectionPlayback(); RefreshFollowingButtons(); RefreshCollectionActions();
        Run(LoadCollectionActionsAsync);
    }

    private void RefreshCollectionPlayback()
    {
        if (collectionPlay == null || !playlistCollectionVisible.Value) return;
        var playing = playbackQueue.Context?.Key == displayedCollection?.Key && isPlaying.Value;
        collectionPlayIcon.Source = Icons.Source(playing ? "pause-solid" : "play-solid", TrackButtons.OnSurface);
        collectionPlay.IsEnabled = activeCollection?.Items.Any(item => item.Track != null) == true;
        foreach (var (tile, position) in collectionTrackTiles.Values)
            RefreshCollectionTrackTile(tile, position);
    }

    private async Task PlayDisplayedCollectionAsync()
    {
        if (displayedCollection == null || page.Value != Page.LibraryCollection) return;
        if (playbackQueue.Context?.Key == displayedCollection.Key && current != null) { await ToggleAsync(); return; }
        if (activeCollection?.Items.FirstOrDefault(item => item.Track != null)?.Track is not { } first) return;
        SetQueue(first, 0); await PlayAsync(first, queueAdvance: true);
    }

    private static string RussianPlural(long count, string one, string few, string many) => count % 100 is >= 11 and <= 14
        ? many : count % 10 == 1 ? one : count % 10 is >= 2 and <= 4 ? few : many;
}
