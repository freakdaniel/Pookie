using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class FeedPublicationView(FeedEntry entry)
    {
        internal FeedEntry Entry = entry;
        internal LikedTrackRow? Track;
        internal readonly List<CompactTrackRow> Preview = [];
        internal readonly List<Image> Covers = [];
        internal readonly List<UserPlaylistActionView> Actions = [];
        internal Image? Play;
    }
    private readonly Dictionary<StackPanel, FeedPublicationView> feedViews = [];
    private readonly List<UserPlaylistActionView> feedPlaylistActions = [];
    private readonly List<CompactTrackRow> feedSidebarRows = [];
    private FeedEntry[]? renderedFeedSidebar;

    private void BindFeedPublication(StackPanel host, FeedEntry entry)
    {
        ClearFeedPublication(host);
        var view = new FeedPublicationView(entry); feedViews[host] = view;
        bool Current() => !disposed && feedViews.GetValueOrDefault(host) == view;
        var actor = entry.Actor ?? entry.Item.Track?.User ?? entry.Item.Playlist?.User;
        var avatar = FeedAvatar(actor, 28, Current);
        var name = new TextBlock().Text(actor?.Username ?? entry.Item.Subtitle).FontSize(12).SemiBold().MaxWidth(280).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(name, () => actor);
        var byline = new StackPanel().Horizontal().Spacing(7).CenterVertical().Children(name);
        if (entry.IsRepost) byline.Add(Icons.View("repeat", 13, Muted).CenterVertical());
        byline.Add(new TextBlock().Text(entry.IsRepost ? "сделал репост" : "опубликовал").FontSize(12).Foreground(Muted).CenterVertical());
        host.Add(new Grid().Columns("28,*,Auto").Rows("28").Spacing(10).Children(avatar.Column(0), byline.Column(1),
            new TextBlock().Text(FeedDate(entry.CreatedAt)).FontSize(11).Foreground(Muted).Column(2).CenterVertical()));
        if (entry.Item.Track is { } track)
        {
            var row = new LikedTrackRow(value => Run(() => PlayFeedTrackAsync(value)), value => Run(() => OpenTrackPageAsync(value)),
                value => Run(() => ToggleTrackLikeAsync(value)), CopyTrackLink, value => Run(() => ToggleTrackRepostAsync(value)),
                value => Run(() => OpenPlaylistPickerAsync(value)), (value, fraction) => Run(() => SeekFeedTrackAsync(value, fraction)),
                ArtworkLayer, glass: true, publication: true);
            view.Track = row; likedRows.Add(row.Root, row);
            row.AddQueueAction(value => EnqueueTrack(value, false));
            AttachTrackQueueMenu(row.Root, () => row.Track); AttachTrackTitle(row.Title, () => row.Track); AttachUserLink(row.Author, () => row.Track?.User);
            BindLibraryTrackRow(row, track); host.Add(row.Root);
        }
        else if (entry.Item.Playlist != null) host.Add(FeedPlaylistPublication(view, Current));
    }

    private FrameworkElement FeedPlaylistPublication(FeedPublicationView view, Func<bool> currentView)
    {
        var item = view.Entry.Item; var playlist = item.Playlist!;
        var cover = new Image().Width(TrackRowLayout.CoverSize).Height(TrackRowLayout.CoverSize).StretchMode(Stretch.UniformToFill);
        view.Covers.Add(cover); SetCollectionArtwork(cover, item);
        Run(async () => { var source = await GetCollectionArtworkAsync(item); if (currentView()) SetCollectionArtwork(cover, item, source, finished: true); });
        var artwork = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).OnClick(() => Run(() => OpenLibraryItemAsync(item)))
            .Content(new Border().CornerRadius(TrackRowLayout.CornerRadius).ClipToBounds().Child(ArtworkLayer(cover)));
        var author = new TextBlock().Text(playlist.User?.Username ?? item.Subtitle).FontSize(11.5).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(author, () => playlist.User);
        var title = new TextBlock().Text(item.Title).FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        title.Cursor = CursorType.Hand; title.MouseDown += args => { if (args.Button == MouseButton.Left) { args.Handled = true; Run(() => OpenLibraryItemAsync(item)); } };
        view.Play = Icons.View("play-solid", 19, TrackButtons.OnPrimary).Center();
        var play = TrackButtons.Icon(view.Play, () => Run(() => PlayLibraryCollectionAsync(item)), TrackButtons.Filled, 36);
        var preview = new StackPanel().Vertical().Spacing(0);
        void Preview(SoundCloudTrack[] tracks)
        {
            foreach (var track in tracks.Where(track => !string.IsNullOrWhiteSpace(track.Title)).Take(3))
            {
                var row = CreateCompactTrackRow(value => Run(() => PlayUserPlaylistTrackAsync(item, value)), dense: true, miniature: true);
                BindCompactTrackRow(row, track); view.Preview.Add(row); preview.Add(row.Root);
            }
        }
        Preview(playlist.Tracks ?? []);
        if (view.Preview.Count == 0) Run(async () =>
        {
            var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, lifetime.Token);
            if (!currentView()) return;
            collectionDetails[item.Key] = detail; Preview(detail.Tracks.Tracks);
        });
        var actions = UserPlaylistActions(item, view.Actions, 36);
        feedPlaylistActions.AddRange(view.Actions);
        RefreshUserPlaylistActions();
        var more = TrackButtons.Action(new TextBlock().Text($"{UserPlaylistTrackCount(playlist)} {RussianPlural(UserPlaylistTrackCount(playlist), "трек", "трека", "треков")}").FontSize(11).Foreground(Muted),
            () => Run(() => OpenLibraryItemAsync(item)), TrackButtons.Text, 28).Padding(0).Left();
        return new Grid().Columns($"{TrackRowLayout.CoverSize},*").Rows("*").Height(TrackRowLayout.Height).Spacing(TrackRowLayout.ColumnGap).Children(artwork.Column(0),
            new Grid().Columns("*").Rows("36,72,36").Spacing(8).Padding(TrackRowLayout.ContentInset, 12).Column(1).Children(
                new Grid().Columns("36,*,Auto").Rows("36").Spacing(10).Row(0).Children(play.Column(0),
                    new StackPanel().Vertical().Spacing(3).Column(1).CenterVertical().Children(author, title), more.Column(2).CenterVertical()),
                preview.Row(1), actions.Row(2)));
    }

    private void ClearFeedPublication(StackPanel host)
    {
        if (feedViews.Remove(host, out var view))
        {
            if (view.Track is { } trackRow) { ClearLibraryTrackRow(trackRow); likedRows.Remove(trackRow.Root); trackRow.TrackArtGlass.Dispose(); }
            foreach (var row in view.Preview) { BindCompactTrackRow(row, null); compactTrackRows.Remove(row); }
            foreach (var cover in view.Covers) StopCardArtwork(cover);
            foreach (var actions in view.Actions) feedPlaylistActions.Remove(actions);
        }
        host.Clear();
    }

    private FrameworkElement FeedAvatar(SoundCloudUser? user, int size, Func<bool> currentView)
    {
        var image = new Image().Width(size).Height(size).StretchMode(Stretch.UniformToFill).Source(Icons.Source("user-circle", Muted));
        if (user?.AvatarUrl != null) Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(UserItem(user));
            if (currentView() && source != null) image.Source = source;
        });
        var avatar = new Border().Width(size).Height(size).CornerRadius(size / 2d).ClipToBounds().Child(image);
        AttachUserLink(avatar, () => user); return avatar;
    }

    private void RenderFeedSidebar()
    {
        if (ReferenceEquals(renderedFeedSidebar, feedState?.Entries)) return;
        renderedFeedSidebar = feedState?.Entries;
        foreach (var row in feedSidebarRows) { BindCompactTrackRow(row, null); compactTrackRows.Remove(row); }
        feedSidebarRows.Clear(); feedSidebar.Clear();
        var entries = feedState?.Entries ?? [];
        var authors = entries.Where(entry => !entry.IsRepost).Select(entry => entry.Item.Track?.User ?? entry.Item.Playlist?.User)
            .OfType<SoundCloudUser>().DistinctBy(user => user.Id).Take(4).ToArray();
        if (authors.Length > 0)
        {
            var releases = new StackPanel().Vertical().Spacing(12).Children(
                new TextBlock().Text("Недавно выпустили").FontSize(17).SemiBold());
            foreach (var author in authors)
            {
                var person = new Grid().Columns("40,*").Rows("Auto").Spacing(12).Children(
                    FeedAvatar(author, 40, () => page.Value == Page.Feed && ReferenceEquals(renderedFeedSidebar, entries)).Column(0),
                    new TextBlock().Text(author.Username).FontSize(13).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).CenterVertical().Column(1));
                releases.Add(TrackButtons.Action(person, () => Run(() => OpenUserProfileAsync(author)), TrackButtons.Text, 48).Padding(0));
                person.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            feedSidebar.Add(releases);
        }
        var history = localRecent.Concat(HomeItems("recent")).Select(item => item.Track).OfType<SoundCloudTrack>().DistinctBy(track => track.Id).Take(3).ToArray();
        if (history.Length > 0)
        {
            var recent = new StackPanel().Vertical().Spacing(8).Children(
                SectionHeader("Недавно слушал", () => Run(() => ShowLibrarySectionAsync(Page.LibraryHistory)), 17));
            foreach (var track in history)
            {
                var row = CreateCompactTrackRow(value => Run(async () =>
                {
                    if (current?.Id == value.Id && playbackQueue.Context?.Key == "recent") { await ToggleAsync(); return; }
                    StartQueueContext(new("recent", "Недавно прослушанное", "Сейчас играет из истории", Kind: Playback.PlaybackContextKind.Recent), history, value);
                    await PlayAsync(value);
                }), dense: true);
                row.Root.Margin = new Thickness(-8, 0, -8, 0);
                BindCompactTrackRow(row, track); feedSidebarRows.Add(row); recent.Add(row.Root);
            }
            feedSidebar.Add(recent);
        }
    }

    private static string FeedDate(string? value)
    {
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date)) return "";
        var age = DateTimeOffset.UtcNow - date;
        return age.TotalMinutes < 1 ? "Только что" : age.TotalHours < 1 ? $"{Math.Max(1, (int)age.TotalMinutes)} мин. назад" :
            age.TotalDays < 1 ? $"{(int)age.TotalHours} ч. назад" : UserPublicationDate(value);
    }

    private void RefreshFeedPlayback()
    {
        foreach (var view in feedViews.Values)
            if (view.Play != null) view.Play.Source = Icons.Source(playbackQueue.Context?.Key == view.Entry.Item.Key && isPlaying.Value ? "pause-solid" : "play-solid", TrackButtons.OnPrimary);
    }

    private void DisposeFeed()
    {
        feedSkeleton.SetActive(false);
        foreach (var host in feedViews.Keys.ToArray()) ClearFeedPublication(host);
        foreach (var row in feedSidebarRows) { BindCompactTrackRow(row, null); compactTrackRows.Remove(row); }
        feedSidebarRows.Clear();
    }
}
