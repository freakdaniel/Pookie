using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> playlistCollectionVisible = new(false);
    private readonly Dictionary<string, CollectionDetail> collectionDetails = [];
    private ItemsControl collectionTrackList = null!;
    private Image collectionCover = null!, collectionBackdrop = null!, collectionPlayIcon = null!;
    private TextBlock collectionTitle = null!, collectionAuthor = null!, collectionMeta = null!, collectionDescription = null!;
    private Button collectionPlay = null!, collectionCopy = null!;
    private LibraryItem? displayedCollection;
    private SoundCloudUser? CollectionAuthor => displayedCollection?.Playlist?.User;
    private ScrollViewer? CollectionScroll => (playlistCollectionVisible.Value ? collectionTrackList : collectionGrid).FindVisualChild<ScrollViewer>() as ScrollViewer;

    private FrameworkElement PlaylistCollectionPage()
    {
        collectionTrackList = CreateLikedList();
        collectionCover = new Image().StretchMode(Stretch.UniformToFill);
        collectionBackdrop = new Image().StretchMode(Stretch.UniformToFill).IsHitTestVisible(false);
        collectionBackdrop.Transitions = [Transition.Create(UIElement.OpacityProperty, 240)];
        collectionTitle = new TextBlock().FontSize(28).Bold().TextWrapping(TextWrapping.Wrap).MaxHeight(70).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionAuthor = new TextBlock().FontSize(14).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionMeta = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        collectionDescription = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(48).TextTrimming(TextTrimming.CharacterEllipsis);
        collectionPlayIcon = Icons.View("play-solid", 28, TrackButtons.OnPrimary).Center();
        collectionPlay = TrackButtons.Icon(collectionPlayIcon,
            () => Run(PlayDisplayedCollectionAsync), TrackButtons.Filled, 64);
        var author = TrackButtons.Action(collectionAuthor, () =>
        {
            if (CollectionAuthor is { } user) Run(() => OpenLibraryItemAsync(new("user:" + user.Id, user.Username, "Исполнитель", user.AvatarUrl, User: user)));
        }, TrackButtons.Text, 32).Padding(0).Left();
        collectionCopy = TrackButtons.Icon(Icons.View("copy", 20), () =>
        { if (TrackLink(displayedCollection?.Playlist?.PermalinkUrl) is { } uri) CopyText(uri.AbsoluteUri); }, TrackButtons.Glass, 44).ToolTip("Скопировать ссылку");
        var titleBlock = new Grid().Columns("64,*").Rows("Auto").Spacing(18).Children(collectionPlay.Column(0).Top(),
            new StackPanel().Vertical().Spacing(7).Column(1).Children(collectionTitle,
                new Grid().Columns("*,40").Rows("Auto").Spacing(12).Children(author.Column(0).CenterVertical(),
                    ArtistFollowButton(() => CollectionAuthor).Column(1)), collectionMeta));
        var coverFrame = new Border().CornerRadius(10).ClipToBounds().Child(ArtworkLayer(collectionCover));
        var hero = new TrackPageColumns(
            new Grid().Columns("*").Rows("Auto,*,44").Children(titleBlock.Row(0), collectionDescription.Row(1).CenterVertical(),
                collectionCopy.Row(2).Left()),
            coverFrame, true, collectionBackdrop, new Border().Background(Color.FromArgb(145, 18, 18, 18))).Initialize();
        var header = new ShadowDecorator().BlurRadius(12).OffsetY(4).ShadowColor(Color.FromArgb(60, 0, 0, 0)).CornerRadius(20)
            .Margin(-12, -8, -12, -16)
            .Child(new Border().CornerRadius(20).ClipToBounds().Background(Raised).Child(hero));
        return new DockPanel().LastChildFill().Spacing(24).Children(header.DockTop(), collectionTrackList);
    }

    private void SetCollectionPresentation(LibraryItem? item)
    {
        if (displayedCollection?.Key != item?.Key) { collectionBackdrop.Source = null; collectionBackdrop.Opacity = 0; }
        displayedCollection = item;
        playlistCollectionVisible.Value = page.Value == Page.LibraryCollection && item?.Playlist != null;
        if (!playlistCollectionVisible.Value) return;
        collectionCopy.IsEnabled = TrackLink(item?.Playlist?.PermalinkUrl) != null;
        collectionTitle.Text = item!.Title; collectionAuthor.Text = item.Playlist?.User?.Username ?? item.Subtitle;
        var playlist = item.Playlist!;
        var data = activeCollection?.Items.Select(entry => entry.Track).OfType<SoundCloudTrack>().ToArray() ?? [];
        var count = Math.Max(playlist.TrackCount, data.Length);
        var milliseconds = playlist.Duration > 0 ? playlist.Duration : data.Sum(track => track.Duration);
        collectionMeta.Text = count + " " + RussianPlural(count, "трек", "трека", "треков") +
            (milliseconds > 0 ? " · " + FormatTime(milliseconds / 1000) : "") + (playlist.Sharing == "private" ? " · Приватный" : "");
        collectionDescription.Text = playlist.Description ?? playlist.ShortDescription ?? "";
        collectionDescription.IsVisible = !string.IsNullOrWhiteSpace(collectionDescription.Text);
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
        });
        RefreshCollectionPlayback(); RefreshFollowingButtons();
    }

    private void RefreshCollectionPlayback()
    {
        if (collectionPlay == null || !playlistCollectionVisible.Value) return;
        var playing = playbackQueue.Context?.Key == displayedCollection?.Key && isPlaying.Value;
        collectionPlayIcon.Source = Icons.Source(playing ? "pause-solid" : "play-solid", TrackButtons.OnPrimary);
        collectionPlay.IsEnabled = activeCollection?.Items.Any(item => item.Track != null) == true;
    }

    private async Task PlayDisplayedCollectionAsync()
    {
        if (displayedCollection == null || page.Value != Page.LibraryCollection) return;
        if (playbackQueue.Context?.Key == displayedCollection.Key && current != null) { await ToggleAsync(); return; }
        if (activeCollection?.Items.FirstOrDefault(item => item.Track != null)?.Track is not { } first) return;
        SetQueue(first, 0); await PlayAsync(first);
    }

    private static string RussianPlural(long count, string one, string few, string many) => count % 100 is >= 11 and <= 14
        ? many : count % 10 == 1 ? one : count % 10 is >= 2 and <= 4 ? few : many;
}
