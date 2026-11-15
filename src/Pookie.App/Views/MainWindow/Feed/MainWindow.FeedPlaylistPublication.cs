using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private FrameworkElement CreateFeedPlaylistPublication(FeedPublicationView view)
    {
        void Open() { if (view.CurrentItem is { } item) Run(() => OpenLibraryItemAsync(item)); }
        var cover = new Image().Width(TrackRowLayout.CoverSize).Height(TrackRowLayout.CoverSize).StretchMode(Stretch.UniformToFill);
        view.Covers.Add(cover);
        var artwork = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).OnClick(Open)
            .Content(new Border().CornerRadius(TrackRowLayout.CornerRadius).ClipToBounds().Child(ArtworkLayer(cover)));
        view.PlaylistAuthor = new TextBlock().FontSize(11.5).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(view.PlaylistAuthor, () => view.CurrentItem?.Playlist?.User);
        view.PlaylistTitle = new TextBlock().FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        view.PlaylistTitle.Cursor = CursorType.Hand;
        view.PlaylistTitle.MouseDown += args => { if (args.Button == MouseButton.Left) { args.Handled = true; Open(); } };
        view.Play = Icons.View("play-solid", 19, TrackButtons.OnPrimary).Center();
        var play = TrackButtons.Icon(view.Play, () => { if (view.CurrentItem is { } item) Run(() => PlayLibraryCollectionAsync(item)); }, TrackButtons.Filled, 36);
        var preview = new StackPanel().Vertical().Spacing(0);
        for (var i = 0; i < 3; i++)
        {
            var row = CreateCompactTrackRow(track =>
            {
                if (view.CurrentItem is { } item) Run(() => PlayUserPlaylistTrackAsync(item, track));
            }, dense: true, miniature: true);
            row.Root.IsVisible = false;
            view.Preview.Add(row); preview.Add(row.Root);
        }
        var actions = UserPlaylistActions(view.Entry.Item, view.Actions, 36, () => view.CurrentItem);
        view.PlaylistCount = new TextBlock().FontSize(11).Foreground(Muted);
        var more = TrackButtons.Action(view.PlaylistCount, Open, TrackButtons.Text, 28).Padding(0).Left();
        var root = new Grid().Columns($"{TrackRowLayout.CoverSize},*").Rows("*").Height(TrackRowLayout.Height).Spacing(TrackRowLayout.ColumnGap).Children(artwork.Column(0),
            new Grid().Columns("*").Rows("36,72,36").Spacing(8).Padding(TrackRowLayout.ContentInset, 12).Column(1).Children(
                new Grid().Columns("36,*,Auto").Rows("36").Spacing(10).Row(0).Children(play.Column(0),
                    new StackPanel().Vertical().Spacing(3).Column(1).CenterVertical().Children(view.PlaylistAuthor, view.PlaylistTitle), more.Column(2).CenterVertical()),
                preview.Row(1), actions.Row(2)));
        VisualTree.Visit(root, element =>
        {
            if (element is MaterialTrackButton button) button.RetainDecorationOnDetach = true;
        });
        return root;
    }

    private void BindFeedPlaylistPublication(FeedPublicationView view, Func<bool> currentView)
    {
        var item = view.Entry.Item; var playlist = item.Playlist!;
        var cover = view.Covers[0]; SetCollectionArtwork(cover, item);
        Run(async () => { var source = await GetCollectionArtworkAsync(item); if (currentView()) SetCollectionArtwork(cover, item, source, finished: true); });
        view.PlaylistAuthor.Text = playlist.User?.Username ?? item.Subtitle;
        view.PlaylistTitle.Text = item.Title;
        var count = UserPlaylistTrackCount(playlist);
        view.PlaylistCount.Text = $"{count} {RussianPlural(count, "трек", "трека", "треков")}";
        void Preview(SoundCloudTrack[] tracks)
        {
            var values = tracks.Where(track => !string.IsNullOrWhiteSpace(track.Title)).Take(3).ToArray();
            for (var i = 0; i < view.Preview.Count; i++)
            {
                var row = view.Preview[i];
                BindCompactTrackRow(row, i < values.Length ? values[i] : null);
                row.Root.IsVisible = row.Track != null;
            }
        }
        Preview(playlist.Tracks ?? []);
        if (view.Preview.All(row => row.Track == null)) Run(async () =>
        {
            var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, lifetime.Token);
            if (!currentView()) return;
            collectionDetails[item.Key] = detail; Preview(detail.Tracks.Tracks);
        });
        foreach (var actions in view.Actions) { actions.Item = item; feedPlaylistActions.Add(actions); }
        RefreshUserPlaylistActions();
        RefreshFeedPlayback();
    }
}
