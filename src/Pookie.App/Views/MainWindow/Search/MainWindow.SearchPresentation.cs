using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<StackPanel, SearchResultView> searchViews = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, Task<ImageSource?>> searchBackdrops = new();

    private sealed record SearchHero(Grid Root, StackPanel BestSection, StackPanel SongsSection, Border Card,
        Image Cover, Image Backdrop, Border Frame, TextBlock Title, TextBlock Subtitle, Image Play, CompactTrackRow[] Tracks)
    {
        public LibraryItem? Item { get; set; }
        public ImageSource? BackgroundArtwork { get; set; }
        public long ArtworkGeneration { get; set; }
        public long BindingGeneration { get; set; }
    }
    private sealed record SearchResultView(StackPanel Root, SearchHero Hero, StackPanel Cards, TextBlock CardsTitle,
        ItemsControl CardGrid, CompactTrackRow Track, TextBlock Heading, StackPanel LibraryTrackHost)
    {
        public SearchBlock? Block { get; set; }
        public LikedTrackRow? LibraryTrack { get; set; }
    }

    private IDataTemplate SearchResultTemplate() => new DelegateTemplate<SearchBlock>(context =>
    {
        var hero = CreateSearchHero();
        var track = CreateCompactTrackRow();
        track.Root.Margin = new Thickness(-8, 0, -8, 0);
        var cardsTitle = new TextBlock().FontSize(22).Bold();
        var cardGrid = CreateCollectionGrid(true);
        var cards = new StackPanel().Vertical().Spacing(16).Margin(0, 0, 0, 24).Children(cardsTitle, cardGrid);
        var heading = new TextBlock().FontSize(22).Bold().Margin(0, 8, 0, 14);
        var libraryTrackHost = new StackPanel().Vertical().Margin(0, 0, 0, 36);
        var root = new StackPanel().Vertical().Children(hero.Root, cards, track.Root, heading, libraryTrackHost);
        var view = new SearchResultView(root, hero, cards, cardsTitle, cardGrid, track, heading, libraryTrackHost);
        searchViews[root] = view;
        context.Register("root", root);
        return root;
    }, (_, block, _, context) =>
    {
        var view = searchViews[context.Get<StackPanel>("root")]; view.Block = block;
        view.Hero.Root.IsVisible = block.Kind == SearchBlockKind.Hero;
        view.Cards.IsVisible = block.Kind == SearchBlockKind.Cards;
        view.Track.Root.IsVisible = block.Kind == SearchBlockKind.Track;
        view.Heading.IsVisible = block.Kind == SearchBlockKind.Heading;
        view.LibraryTrackHost.IsVisible = block.Kind == SearchBlockKind.LibraryTrack;
        if (block.Kind == SearchBlockKind.LibraryTrack && block.Item?.Track is { } libraryTrack)
        {
            if (view.LibraryTrack == null)
            {
                view.LibraryTrack = CreateLibraryTrackRow();
                view.LibraryTrackHost.Children(view.LibraryTrack.Root);
            }
            BindLibraryTrackRow(view.LibraryTrack, libraryTrack);
        }
        else if (view.LibraryTrack != null) ClearLibraryTrackRow(view.LibraryTrack);
        BindCompactTrackRow(view.Track, block.Kind == SearchBlockKind.Track ? block.Item?.Track : null);
        if (block.Kind == SearchBlockKind.Hero) BindSearchHero(view.Hero, block);
        else ClearSearchHero(view.Hero);
        view.CardsTitle.Text = block.Title; view.CardsTitle.IsVisible = block.Title.Length > 0;
        view.Heading.Text = block.Title;
        view.CardGrid.Items(block.Kind == SearchBlockKind.Cards ? block.Items ?? [] : [], item => item.Title);
        view.CardGrid.Height = likedArtworkSize + 90;
        SetSearchHeroGeometry(view.Hero, searchList.ActualWidth - SearchShadowGutter);
    }, (_, _, _, context) =>
    {
        var view = searchViews[context.Get<StackPanel>("root")]; view.Block = null;
        BindCompactTrackRow(view.Track, null); ClearSearchHero(view.Hero);
        if (view.LibraryTrack != null) ClearLibraryTrackRow(view.LibraryTrack);
        view.CardGrid.Items(Array.Empty<LibraryItem>(), item => item.Title);
    });

    private SearchHero CreateSearchHero()
    {
        var cover = new Image().Width(96).Height(96).StretchMode(Stretch.UniformToFill);
        var frame = new Border().Width(96).Height(96).CornerRadius(6).ClipToBounds().Child(ArtworkLayer(cover));
        var title = new TextBlock().FontSize(24).Bold().MaxHeight(64).TextWrapping(TextWrapping.Wrap)
            .TextTrimming(TextTrimming.CharacterEllipsis);
        var subtitle = new TextBlock().FontSize(12).Foreground(Color.FromArgb(185, 255, 255, 255)).TextTrimming(TextTrimming.CharacterEllipsis);
        var play = Icons.View("play-solid", 22, Surface).CenterHorizontal().CenterVertical();
        var backdrop = new Image().StretchMode(Stretch.UniformToFill);
        backdrop.ImageScaleQuality = ImageScaleQuality.HighQuality;
        backdrop.IsHitTestVisible = false;
        backdrop.Opacity = 0;
        backdrop.Transitions = [Transition.Create(UIElement.OpacityProperty, 240)];
        var hover = new Border().Background(Color.FromArgb(12, 255, 255, 255));
        hover.IsHitTestVisible = false; hover.Opacity = 0;
        hover.Transitions = [Transition.Create(UIElement.OpacityProperty, 180)];
        var content = new Grid().Columns("*,44").Rows("*").Padding(20).Children(
                new StackPanel().Vertical().Spacing(12).Column(0).ColumnSpan(2).Children(frame, title, subtitle),
                new Border().Background(Color.White).CornerRadius(22).Width(44).Height(44).Right().Bottom().Column(1).Child(play));
        var card = new Border().Background(Color.FromRgb(29, 29, 29)).CornerRadius(12).Height(256).Padding(0).ClipToBounds()
            .Child(new Grid().Columns("*").Rows("*").Children(backdrop, hover, content));
        var button = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(card);
        // Compensate the shadow's reserved space so the card keeps its existing alignment and size.
        var shadow = new ShadowDecorator().BlurRadius(14).OffsetY(5).ShadowColor(Color.FromArgb(72, 0, 0, 0))
            .CornerRadius(12).Margin(-14, -9, -14, -19).Child(button);
        var bestSection = new StackPanel().Vertical().Spacing(16).Children(
            new TextBlock().Text("Лучший результат").FontSize(22).Bold(), shadow);
        var rows = Enumerable.Range(0, 4).Select(_ => CreateCompactTrackRow()).ToArray();
        var songsSection = new StackPanel().Vertical().Spacing(16).Margin(-8, 0, -8, 0).Children(
            new TextBlock().Text("Треки").FontSize(22).Bold().Margin(8, 0),
            new StackPanel().Vertical().Children(rows.Select(row => (Element)row.Root).ToArray()));
        var root = new Grid().Columns("*,1.5*").Rows("Auto").Spacing(28).Margin(0, 0, 0, 32)
            .Children(bestSection.Column(0), songsSection.Column(1));
        var hero = new SearchHero(root, bestSection, songsSection, card, cover, backdrop, frame, title, subtitle, play, rows);
        AttachTrackQueueMenu(button, () => hero.Item?.Track);
        card.SizeChanged += e => subtitle.MaxWidth = Math.Max(0, e.NewSize.Width - 96);
        button.Click += () =>
        {
            if (hero.Item is not { } item) return;
            if (item.Track is { } track) SelectLibraryTrack(track);
            else Run(() => OpenLibraryItemAsync(item));
        };
        button.MouseEnter += () => hover.Opacity = 1;
        button.MouseLeave += () => hover.Opacity = 0;
        return hero;
    }

    private void BindSearchHero(SearchHero hero, SearchBlock block)
    {
        var item = block.Item!; hero.Item = item;
        var binding = ++hero.BindingGeneration;
        hero.Title.Text = item.Title;
        var kind = item.User != null ? "Исполнитель" : item.Track != null ? "Трек" : item.IsAlbum ? "Альбом" : "Плейлист";
        hero.Subtitle.Text = string.IsNullOrEmpty(item.Subtitle) ? kind : kind + " · " + item.Subtitle;
        hero.Frame.CornerRadius = item.User != null ? 48 : 6;
        var artwork = item.ArtworkUrl ?? item.Track?.User?.AvatarUrl;
        var imageItem = item with { ArtworkUrl = artwork };
        SetCollectionArtwork(hero.Cover, imageItem);
        SetSearchHeroBackdrop(hero, CachedCollectionArtwork(imageItem));
        Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(imageItem);
            if (!disposed && hero.BindingGeneration == binding && hero.Item?.Key == item.Key)
            {
                SetCollectionArtwork(hero.Cover, imageItem, source, finished: true);
                SetSearchHeroBackdrop(hero, source ?? CachedCollectionArtwork(imageItem));
            }
        });
        for (var i = 0; i < hero.Tracks.Length; i++)
        {
            var track = block.Items?.ElementAtOrDefault(i)?.Track;
            hero.Tracks[i].Root.IsVisible = track != null;
            BindCompactTrackRow(hero.Tracks[i], track);
        }
        hero.SongsSection.IsVisible = block.Items?.Length > 0;
        RefreshSearchHeroPlayback(hero);
    }

    private void ClearSearchHero(SearchHero hero)
    {
        ++hero.BindingGeneration;
        hero.Item = null; StopCardArtwork(hero.Cover);
        SetSearchHeroBackdrop(hero, null);
        foreach (var row in hero.Tracks) BindCompactTrackRow(row, null);
    }

    private void SetSearchHeroBackdrop(SearchHero hero, ImageSource? source)
    {
        if (ReferenceEquals(hero.BackgroundArtwork, source)) return;
        hero.BackgroundArtwork = source;
        var generation = ++hero.ArtworkGeneration;
        hero.Backdrop.Opacity = 0;
        hero.Backdrop.Source = null;
        if (source == null) return;
        Run(async () =>
        {
            var background = await searchBackdrops.GetValue(source, artwork => Task.Run(() =>
            {
                try { return ArtworkBackdrop.Create(artwork); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { return null; }
            }, lifetime.Token));
            if (disposed || hero.Item == null || hero.ArtworkGeneration != generation) return;
            hero.Backdrop.Source = background;
            hero.Backdrop.Opacity = background == null ? 0 : 1;
        });
    }

    private void RefreshSearchHeroPlayback(SearchHero hero) =>
        hero.Play.Source = Icons.Source(hero.Item?.Track is { } track
            ? current?.Id == track.Id && isPlaying.Value ? "pause-solid" : "play-solid" : "arrow-right", Surface);

    private void RefreshSearchPlayback()
    {
        foreach (var row in compactTrackRows) RefreshCompactTrackRow(row);
        foreach (var view in searchViews.Values) if (view.Hero.Item != null) RefreshSearchHeroPlayback(view.Hero);
    }

    private void UpdateSearchHeroGeometry(double width)
    {
        foreach (var view in searchViews.Values) SetSearchHeroGeometry(view.Hero, width - SearchShadowGutter);
    }

    private static void SetSearchHeroGeometry(SearchHero hero, double width)
    {
        var stacked = width is > 0 and < 700;
        hero.BestSection.MaxWidth = hero.SongsSection.IsVisible ? double.PositiveInfinity : 420;
        hero.BestSection.HorizontalAlignment = hero.SongsSection.IsVisible ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        hero.Root.Columns(stacked || !hero.SongsSection.IsVisible ? "*" : "*,1.5*").Rows(stacked ? "Auto,Auto" : "Auto");
        hero.BestSection.Column(0).Row(0);
        hero.SongsSection.Column(stacked ? 0 : 1).Row(stacked ? 1 : 0);
    }
}
