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
    private Button collectionPlay = null!, collectionShare = null!, collectionDescriptionToggle = null!;
    private TrackPageColumns collectionHero = null!;
    private Grid collectionTitleBlock = null!, collectionMetadataLine = null!, collectionHeroContent = null!, collectionHeading = null!;
    private StackPanel collectionMixTags = null!;
    private TextBlock collectionMixPlayLabel = null!;
    private Color collectionMixInk = TrackButtons.OnSurface;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, Task<MixBadgePalette?>> mixBadgePalettes = new();
    private TextBlock collectionMixStats = null!;
    private MixHeroShape collectionMixShape = null!;
    private Border collectionHeroFrame = null!, collectionScrim = null!;
    private CardShadow collectionHeroShadow = null!;
    private ArtworkGlassSheen collectionSheen = null!;
    private MixHeroSurface collectionMixSurface = null!, collectionMixScrim = null!;
    private MixHeroArtwork collectionMixArtwork = null!;
    private Image collectionMixPlayIcon = null!;
    private Button collectionAuthorLink = null!, collectionFollow = null!, collectionMore = null!, collectionMixPlay = null!, collectionMixQueue = null!;
    private FrameworkElement collectionSocialActions = null!;
    private TextBox collectionFilter = null!;
    private StackPanel collectionDescriptionHost = null!;
    private ScrollViewer collectionDescriptionScroll = null!;
    private TextBlock collectionEmpty = null!;
    private string collectionFilterText = "";
    private bool collectionDescriptionExpanded;
    private DispatcherTimer? playlistHoverTimer;
    private double playlistHoverOffset;
    private long lastPlaylistMovement;
    private bool playlistHoverSuspended;
    private sealed record CollectionTrackEntry(SoundCloudTrack Track, int Position);
    private LibraryItem? displayedCollection;
    private SoundCloudUser? CollectionAuthor => displayedCollection?.Playlist?.User;
    private ItemsControl CollectionTrackView => collectionMode.Value switch { TrackViewMode.Waveform => collectionTrackWaves, TrackViewMode.List => collectionTrackList, _ => collectionTrackCards };
    private ScrollViewer? CollectionScroll => (playlistCollectionVisible.Value ? CollectionTrackView : collectionGrid).FindVisualChild<ScrollViewer>() as ScrollViewer;
    private bool CompactPlaylistVisible => page.Value == Page.LibraryCollection &&
        playlistCollectionVisible.Value && collectionMode.Value == TrackViewMode.List && workspace.IsVisible;

    private void ObservePlaylistPointerMotion()
    {
        if (disposed) return;
        var offset = shellScroll.VerticalOffset;
        if (offset == playlistHoverOffset) return;
        playlistHoverOffset = offset;
        if (!CompactPlaylistVisible) return;
        lastPlaylistMovement = Environment.TickCount64;
        if (playlistHoverSuspended) return;

        // Scrolling can put several rows per second under a stationary
        // pointer. Their hover animations and glass captures can wait for
        // the row the pointer actually comes to rest on.
        playlistHoverSuspended = true;
        if (playlistHoverTimer == null)
        {
            playlistHoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80));
            playlistHoverTimer.Tick += ResumePlaylistPointer;
        }
        playlistHoverTimer.Start();
        RefreshPlaylistPointerRows();
    }

    private void ResumePlaylistPointer()
    {
        if (CompactPlaylistVisible && Environment.TickCount64 - lastPlaylistMovement < 90) return;
        playlistHoverTimer?.Stop();
        playlistHoverSuspended = false;
        if (CompactPlaylistVisible) RefreshPlaylistPointerRows();
    }

    private void RefreshPlaylistPointerRows()
    {
        foreach (var row in compactTrackRows)
            if (row.CollectionRow && (row.Hovered || row.Trailing.Animating)) RefreshCompactTrackRow(row);
    }

    private FrameworkElement PlaylistCollectionPage()
    {
        collectionTrackList = CreateCollectionTrackList();
        collectionTrackCards = CreateCollectionTrackCards();
        collectionTrackWaves = CreateCollectionTrackWaves();
        collectionMode.Changed += () => { RefreshCollectionTracks(); CollectionScroll?.SetScrollOffsets(0, 0); };
        collectionCover = new Image().StretchMode(Stretch.UniformToFill);
        collectionMixTags = new StackPanel().Horizontal().Spacing(6).Margin(0, -24, 0, 0).IsVisible(false);
        collectionMixShape = new MixHeroShape(collectionMixTags);
        collectionBackdrop = new Image().StretchMode(Stretch.UniformToFill).IsHitTestVisible(false);
        collectionBackdrop.Transitions = [Transition.Create(UIElement.OpacityProperty, 240)];
        collectionTitle = new TextBlock().FontSize(28).Bold().TextWrapping(TextWrapping.Wrap).MaxHeight(70).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionAuthor = new TextBlock().FontSize(14).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionMeta = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionDescription = new TextBlock().FontSize(14).FontWeight(FontWeight.Normal).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(48).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionPlayIcon = Icons.View("play-solid", 28, TrackButtons.OnSurface).Center();
        collectionPlay = TrackButtons.Icon(collectionPlayIcon,
            () => Run(PlayDisplayedCollectionAsync), TrackButtons.Glass, 64);
        var author = collectionAuthorLink = TrackButtons.Action(collectionAuthor, () =>
        {
            if (CollectionAuthor is { } user) Run(() => OpenLibraryItemAsync(new("user:" + user.Id, user.Username, "Исполнитель", user.AvatarUrl, User: user)));
        }, TrackButtons.Text, 20).Padding(0).Left();
        collectionDescriptionToggle = TrackButtons.Action(new TextBlock().Text("Подробнее").FontSize(13), () =>
        {
            collectionDescriptionExpanded = !collectionDescriptionExpanded;
            RefreshCollectionDescription();
        }, TrackButtons.Text, 32).Padding(0).Left();
        var titleBlock = collectionTitleBlock = new Grid().Columns("64,*").Rows("Auto").Spacing(18).Children(collectionPlay.Column(0).Top(),
            collectionHeading = new Grid().Columns("*,Auto").Rows("Auto").Column(1).Children(
                new StackPanel().Vertical().Spacing(7).Column(0).Children(collectionTitle,
                    collectionMetadataLine = new Grid().Columns("Auto,*").Rows("Auto").Spacing(6).Children(author.Column(0).CenterVertical(),
                        collectionMeta.Column(1).CenterVertical())), collectionMixTags.Column(1).Right().Top()));
        var coverFrame = new Border().CornerRadius(10).ClipToBounds().Child(ArtworkLayer(collectionCover));
        var more = collectionMore = TrackButtons.Icon(Icons.View("dots-three", 22), () => { }, TrackButtons.Glass, 44);
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
        collectionMixPlayIcon = Icons.View("play-solid", 18, TrackButtons.OnSurface);
        collectionMixPlayLabel = new TextBlock().Text("Слушать").FontSize(14).SemiBold().Foreground(TrackButtons.OnSurface);
        collectionMixPlay = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(
            collectionMixPlayIcon.CenterVertical(), collectionMixPlayLabel.CenterVertical()),
            () => Run(PlayDisplayedCollectionAsync), TrackButtons.Glass, 44).Hint("Воспроизвести или приостановить микс").IsVisible(false);
        collectionMixQueue = TrackButtons.Icon(Icons.View("queue", 20), EnqueueDisplayedCollection, TrackButtons.Glass, 44)
            .Hint("Добавить микс в очередь").IsVisible(false);
        collectionMixStats = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis).Right().CenterVertical().IsVisible(false);
        var hero = collectionHero = new TrackPageColumns(
            collectionHeroContent = new Grid().Columns("*").Rows("Auto,*,44").Children(titleBlock.Row(0),
                new Grid().Columns("Auto,*").Rows("Auto").Spacing(16).Row(2).Children(
                    new StackPanel().Horizontal().Spacing(TrackButtons.ActionSpacing).Column(0).Children(collectionMixPlay,
                    collectionSocialActions = TrackButtons.Connected(collectionLike, collectionRepost),
                    collectionShare = TrackButtons.Icon(Icons.View("arrow-square-out", 20), () => { if (displayedCollection is { } item) OpenShare(item); }, TrackButtons.Glass, 44).Hint("Поделиться"),
                    collectionFollow = ArtistFollowButton(() => CollectionAuthor, TrackButtons.Glass, 44), collectionMixQueue, more), collectionMixStats.Column(1))),
            coverFrame, true,
            collectionMixSurface = new MixHeroSurface(collectionMixShape, Raised).IsHitTestVisible(false).IsVisible(false),
            collectionBackdrop,
            collectionMixArtwork = new MixHeroArtwork(collectionMixShape, collectionBackdrop).IsHitTestVisible(false).IsVisible(false),
            collectionScrim = new Border().Background(ArtworkBackdrop.CardScrim),
            collectionSheen = new ArtworkGlassSheen(),
            collectionMixScrim = new MixHeroSurface(collectionMixShape, ArtworkBackdrop.CardScrim, edge: true).IsHitTestVisible(false).IsVisible(false))
            { HeroArtworkMinWidth = 176, HeroArtworkMaxWidth = 208 }.Initialize();
        collectionGlass = AttachCardGlass(hero);
        var header = collectionHeroShadow = new CardShadow(collectionHeroFrame = new Border().CornerRadius(20).ClipToBounds().BorderThickness(0).Background(Raised).Child(hero))
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
            ApplyMixButtonPalette(null);
        }
        displayedCollection = item;
        playlistCollectionVisible.Value = page.Value == Page.LibraryCollection && item?.Playlist != null;
        if (!playlistCollectionVisible.Value) return;
        collectionTitle.Text = item!.Title; collectionAuthor.Text = item.Playlist?.User?.Username ?? item.Subtitle;
        var playlist = item.Playlist!;
        var system = IsSystemCollection(item);
        collectionHero.HeroArtworkLeading = system;
        collectionHero.HeroFitContent = system;
        collectionHero.HeroArtworkMinWidth = system ? 144 : 176;
        collectionHero.HeroArtworkMaxWidth = system ? 160 : 208;
        collectionHeroContent.Rows("Auto,*,44");
        collectionMixShape.Enabled = system;
        collectionMixSurface.IsVisible = collectionMixScrim.IsVisible = collectionMixArtwork.IsVisible = collectionMixStats.IsVisible = system;
        collectionBackdrop.IsVisible = collectionScrim.IsVisible = collectionSheen.IsVisible = !system;
        collectionHeroFrame.Background = system ? Color.Transparent : Raised;
        collectionHeroShadow.DrawShadow = !system;
        collectionHero.InvalidateMeasure();
        collectionTitle.FontSize = system ? 48 : 28;
        collectionTitle.MaxHeight = system ? 116 : 70;
        collectionPlay.IsVisible = collectionAuthorLink.IsVisible = !system;
        collectionSocialActions.IsVisible = collectionFollow.IsVisible = collectionMore.IsVisible = !system;
        collectionMixPlay.IsVisible = collectionMixQueue.IsVisible = system;
        collectionTitleBlock.Columns(system ? "0,*" : "64,*").Spacing(system ? 0 : 18);
        collectionHeading.Spacing(system ? 20 : 0);
        collectionMetadataLine.Spacing(system ? 0 : 6);
        collectionMeta.FontSize = system ? 14 : 12;
        var data = activeCollection?.Items.Select(entry => entry.Track).OfType<SoundCloudTrack>().ToArray() ?? [];
        RefreshMixTags(system ? MixTags(playlist, data) : []);
        var count = Math.Max(playlist.TrackCount, data.Length);
        var milliseconds = playlist.Duration > 0 ? playlist.Duration : data.Sum(track => track.Duration);
        collectionMeta.Text = (system ? "" : "· ") + string.Join(" · ", new[] { system ? null : playlist.Genre,
            count + " " + RussianPlural(count, "трек", "трека", "треков"),
            milliseconds > 0 ? FormatTime(milliseconds / 1000) : null,
            playlist.Sharing == "private" ? "Приватный" : null,
            DetailAge(playlist.CreatedAt ?? playlist.LastModified) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var artists = system ? MixArtists(playlist, data) : "";
        collectionMixStats.Text = system ? collectionMeta.Text : "";
        if (system) collectionMeta.Text = artists;
        collectionDescription.Text = playlist.Description ?? playlist.ShortDescription ?? "";
        collectionDescriptionHost.IsVisible = collectionDescription.IsVisible = !string.IsNullOrWhiteSpace(collectionDescription.Text) &&
            (!system || collectionDescription.Text.Trim() != artists);
        RefreshCollectionDescription();
        SetCollectionArtwork(collectionCover, item);
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(item);
            if (disposed || generation != navigationGeneration || !ReferenceEquals(displayedCollection, item)) return;
            SetCollectionArtwork(collectionCover, item, source, finished: true);
            var accent = source != null && item.Playlist?.Urn.Contains(":your-moods:", StringComparison.Ordinal) == true
                ? await mixBadgePalettes.GetValue(source, artwork => Task.Run(() => MixBadgePalette.FromArtwork(artwork), lifetime.Token)) : null;
            if (disposed || generation != navigationGeneration || !ReferenceEquals(displayedCollection, item)) return;
            ApplyMixButtonPalette(accent);
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
        TrackButtons.SetGlyph(collectionMixPlayIcon, playing ? "pause-solid" : "play-solid", collectionMixInk);
        SetMixPlayLabel(playing ? "Пауза" : "Слушать");
        collectionPlay.IsEnabled = activeCollection?.Items.Any(item => item.Track != null) == true;
        collectionMixPlay.IsEnabled = collectionMixQueue.IsEnabled = collectionPlay.IsEnabled;
        foreach (var (tile, position) in collectionTrackTiles.Values)
            RefreshCollectionTrackTile(tile, position);
    }

    private void SetMixPlayLabel(string text)
    {
        if (collectionMixPlayLabel.Text == text && double.IsFinite(collectionMixPlay.Width)) return;
        var initial = !double.IsFinite(collectionMixPlay.Width);
        collectionMixPlayLabel.Text = text;
        var content = (FrameworkElement)collectionMixPlay.Content!;
        content.Measure(new Size(double.PositiveInfinity, collectionMixPlay.Height));
        collectionMixPlay.Width = content.DesiredSize.Width + collectionMixPlay.Padding.Left + collectionMixPlay.Padding.Right;
        if (initial) collectionMixPlay.Transitions = [Transition.Create(FrameworkElement.WidthProperty, 240, Easing.CubicBezier(.2, 0, 0, 1))];
    }

    private void ApplyMixButtonPalette(MixBadgePalette? palette)
    {
        collectionMixInk = palette?.Foreground ?? TrackButtons.OnSurface;
        ((MaterialTrackButton)collectionMixPlay).GlassAccent(palette?.Surface, collectionMixInk);
        TrackButtons.SetTextColor(collectionMixPlayLabel, collectionMixInk);
        var playing = playbackQueue.Context?.Key == displayedCollection?.Key && isPlaying.Value;
        TrackButtons.SetGlyph(collectionMixPlayIcon, playing ? "pause-solid" : "play-solid", collectionMixInk);
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
