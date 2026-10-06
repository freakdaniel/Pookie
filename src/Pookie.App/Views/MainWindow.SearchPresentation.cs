using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<StackPanel, SearchResultView> searchViews = [];
    private readonly List<CompactSearchTrack> searchTrackRows = [];

    private sealed record CompactSearchTrack(Grid Root, Image Cover, Image Play, Border Overlay, Button Like,
        Image Heart, TextBlock Title, TextBlock Author, TextBlock Duration)
    {
        public SoundCloudTrack? Track { get; set; }
        public bool Hovered { get; set; }
    }
    private sealed record SearchHero(Grid Root, StackPanel BestSection, StackPanel SongsSection, Border Card,
        Image Cover, Border Frame, TextBlock Title, TextBlock Subtitle, Image Play, CompactSearchTrack[] Tracks)
    {
        public LibraryItem? Item { get; set; }
    }
    private sealed record SearchResultView(StackPanel Root, SearchHero Hero, StackPanel Cards, TextBlock CardsTitle,
        ItemsControl CardGrid, CompactSearchTrack Track, TextBlock Heading, StackPanel LibraryTrackHost)
    {
        public SearchBlock? Block { get; set; }
        public LikedTrackRow? LibraryTrack { get; set; }
    }

    private IDataTemplate SearchResultTemplate() => new DelegateTemplate<SearchBlock>(context =>
    {
        var hero = CreateSearchHero();
        var track = CreateCompactSearchTrack();
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
        BindCompactSearchTrack(view.Track, block.Kind == SearchBlockKind.Track ? block.Item?.Track : null);
        if (block.Kind == SearchBlockKind.Hero) BindSearchHero(view.Hero, block);
        else ClearSearchHero(view.Hero);
        view.CardsTitle.Text = block.Title; view.CardsTitle.IsVisible = block.Title.Length > 0;
        view.Heading.Text = block.Title;
        view.CardGrid.Items(block.Kind == SearchBlockKind.Cards ? block.Items ?? [] : [], item => item.Title);
        view.CardGrid.Height = likedArtworkSize + 90;
        SetSearchHeroGeometry(view.Hero, searchList.ActualWidth);
    }, (_, _, _, context) =>
    {
        var view = searchViews[context.Get<StackPanel>("root")]; view.Block = null;
        BindCompactSearchTrack(view.Track, null); ClearSearchHero(view.Hero);
        if (view.LibraryTrack != null) ClearLibraryTrackRow(view.LibraryTrack);
        view.CardGrid.Items(Array.Empty<LibraryItem>(), item => item.Title);
    });

    private CompactSearchTrack CreateCompactSearchTrack()
    {
        var cover = new Image().Width(44).Height(44).StretchMode(Stretch.UniformToFill);
        var play = Icons.View("play-solid", 18).CenterHorizontal().CenterVertical();
        var overlay = new Border().Background(Color.FromArgb(170, 0, 0, 0)).Child(play);
        overlay.Opacity = 0;
        overlay.Transitions = [Transition.Create(UIElement.OpacityProperty, 150)];
        var frame = new Border().Width(44).Height(44).CornerRadius(5).ClipToBounds()
            .Child(ArtworkLayer(cover).Children(overlay));
        var title = new TextBlock().FontSize(14).SemiBold().Height(21).TextTrimming(TextTrimming.CharacterEllipsis);
        var author = new TextBlock().FontSize(12).Foreground(Muted).Height(19).TextTrimming(TextTrimming.CharacterEllipsis);
        var duration = new TextBlock().FontSize(12).Foreground(Muted).Width(48).Right().CenterVertical();
        var heart = Icons.View("heart", 17, Muted).CenterHorizontal().CenterVertical();
        var like = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Width(28).Height(32)
            .CenterVertical().Content(heart);
        var action = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(new Grid().Columns("44,*").Rows("*").Spacing(12).Children(frame.Column(0).CenterVertical(),
                new StackPanel().Vertical().CenterVertical().Column(1).Children(title, author)));
        var root = new Grid().Columns("*,28,48").Rows("*").Spacing(12).Padding(8, 6).Height(64);
        var hoverFill = new Border().CornerRadius(6).Background(Raised).Column(0).ColumnSpan(3);
        hoverFill.Opacity = 0;
        hoverFill.Transitions = [Transition.Create(UIElement.OpacityProperty, 150)];
        hoverFill.IsHitTestVisible = false;
        root.Children(hoverFill, action.Column(0), like.Column(1), duration.Column(2));
        var row = new CompactSearchTrack(root, cover, play, overlay, like, heart, title, author, duration);
        searchTrackRows.Add(row);
        action.Click += () => { if (row.Track is { } item) SelectLibraryTrack(item); };
        like.Click += () => { if (row.Track is { } item) Run(() => ToggleTrackLikeAsync(item)); };
        root.MouseEnter += () => { row.Hovered = true; hoverFill.Opacity = 1; RefreshCompactSearchTrack(row); };
        root.MouseLeave += () => { row.Hovered = false; hoverFill.Opacity = 0; RefreshCompactSearchTrack(row); };
        return row;
    }

    private void BindCompactSearchTrack(CompactSearchTrack row, SoundCloudTrack? track)
    {
        row.Track = track;
        if (track == null) { StopCardArtwork(row.Cover); return; }
        row.Title.Text = track.Title; row.Author.Text = track.Author; row.Duration.Text = FormatTime(track.DurationSeconds);
        SetCardArtwork(row.Cover, libraryCoverCache.GetValueOrDefault(track.Id), track.ArtworkUrl ?? track.User?.AvatarUrl);
        Run(async () =>
        {
            var source = await GetLibraryArtworkAsync(track);
            if (!disposed && row.Track?.Id == track.Id)
                SetCardArtwork(row.Cover, source, track.ArtworkUrl ?? track.User?.AvatarUrl, finished: true);
        });
        RefreshCompactSearchTrack(row);
    }

    private void RefreshCompactSearchTrack(CompactSearchTrack row)
    {
        if (row.Track is not { } track) return;
        var selected = current?.Id == track.Id;
        row.Play.Source = Icons.Source(selected && isPlaying.Value ? "pause-solid" : "play-solid");
        row.Overlay.Opacity = selected || row.Hovered ? 1 : 0;
        var liked = likedIds.Contains(track.Id);
        row.Heart.Source = Icons.Source(liked ? "heart-filled" : "heart", liked ? LikedHeart : row.Hovered ? Color.White : Muted);
        row.Like.IsEnabled = me != null && !likeBusy && !demo;
    }

    private SearchHero CreateSearchHero()
    {
        var cover = new Image().Width(96).Height(96).StretchMode(Stretch.UniformToFill);
        var frame = new Border().Width(96).Height(96).CornerRadius(6).ClipToBounds().Child(ArtworkLayer(cover));
        var title = new TextBlock().FontSize(24).Bold().MaxHeight(64).TextWrapping(TextWrapping.Wrap)
            .TextTrimming(TextTrimming.CharacterEllipsis);
        var subtitle = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        var play = Icons.View("play-solid", 22, Surface).CenterHorizontal().CenterVertical();
        var card = new Border().Background(Color.FromRgb(29, 29, 29)).CornerRadius(12).Height(256).Padding(20)
            .Child(new Grid().Columns("*,44").Rows("*").Children(
                new StackPanel().Vertical().Spacing(12).Column(0).ColumnSpan(2).Children(frame, title, subtitle),
                new Border().Background(Color.White).CornerRadius(22).Width(44).Height(44).Right().Bottom().Column(1).Child(play)));
        card.Transitions = [Transition.Create(Control.BackgroundProperty, 180)];
        var button = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(card);
        var bestSection = new StackPanel().Vertical().Spacing(16).Children(
            new TextBlock().Text("Лучший результат").FontSize(22).Bold(), button);
        var rows = Enumerable.Range(0, 4).Select(_ => CreateCompactSearchTrack()).ToArray();
        var songsSection = new StackPanel().Vertical().Spacing(16).Children(
            new TextBlock().Text("Треки").FontSize(22).Bold(),
            new StackPanel().Vertical().Children(rows.Select(row => (Element)row.Root).ToArray()));
        var root = new Grid().Columns("*,1.5*").Rows("Auto").Spacing(28).Margin(0, 0, 0, 32)
            .Children(bestSection.Column(0), songsSection.Column(1));
        var hero = new SearchHero(root, bestSection, songsSection, card, cover, frame, title, subtitle, play, rows);
        card.SizeChanged += e => subtitle.MaxWidth = Math.Max(0, e.NewSize.Width - 96);
        button.Click += () =>
        {
            if (hero.Item is not { } item) return;
            if (item.Track is { } track) SelectLibraryTrack(track);
            else Run(() => OpenLibraryItemAsync(item));
        };
        button.MouseEnter += () => card.Background = Color.FromRgb(37, 37, 37);
        button.MouseLeave += () => card.Background = Color.FromRgb(29, 29, 29);
        return hero;
    }

    private void BindSearchHero(SearchHero hero, SearchBlock block)
    {
        var item = block.Item!; hero.Item = item;
        hero.Title.Text = item.Title;
        var kind = item.User != null ? "Исполнитель" : item.Track != null ? "Трек" : item.IsAlbum ? "Альбом" : "Плейлист";
        hero.Subtitle.Text = string.IsNullOrEmpty(item.Subtitle) ? kind : kind + " · " + item.Subtitle;
        hero.Frame.CornerRadius = item.User != null ? 48 : 6;
        var artwork = item.ArtworkUrl ?? item.Track?.User?.AvatarUrl;
        var imageItem = item with { ArtworkUrl = artwork };
        SetCollectionArtwork(hero.Cover, imageItem);
        Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(imageItem);
            if (!disposed && hero.Item?.Key == item.Key)
                SetCollectionArtwork(hero.Cover, imageItem, source, finished: true);
        });
        for (var i = 0; i < hero.Tracks.Length; i++)
        {
            var track = block.Items?.ElementAtOrDefault(i)?.Track;
            hero.Tracks[i].Root.IsVisible = track != null;
            BindCompactSearchTrack(hero.Tracks[i], track);
        }
        hero.SongsSection.IsVisible = block.Items?.Length > 0;
        RefreshSearchHeroPlayback(hero);
    }

    private void ClearSearchHero(SearchHero hero)
    {
        hero.Item = null; StopCardArtwork(hero.Cover);
        foreach (var row in hero.Tracks) BindCompactSearchTrack(row, null);
    }

    private void RefreshSearchHeroPlayback(SearchHero hero) =>
        hero.Play.Source = Icons.Source(hero.Item?.Track is { } track
            ? current?.Id == track.Id && isPlaying.Value ? "pause-solid" : "play-solid" : "arrow-right", Surface);

    private void RefreshSearchPlayback()
    {
        foreach (var row in searchTrackRows) RefreshCompactSearchTrack(row);
        foreach (var view in searchViews.Values) if (view.Hero.Item != null) RefreshSearchHeroPlayback(view.Hero);
    }

    private void UpdateSearchHeroGeometry(double width)
    {
        foreach (var view in searchViews.Values) SetSearchHeroGeometry(view.Hero, width);
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
