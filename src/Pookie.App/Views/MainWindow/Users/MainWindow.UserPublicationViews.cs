using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class UserPublicationView
    {
        internal LibraryItem? Item;
        internal long Revision;
        internal LikedTrackRow? Track;
        internal UserPublicationByline? TrackByline, PlaylistByline;
        internal FrameworkElement? Playlist;
        internal Image Cover = null!, Play = null!;
        internal TextBlock Author = null!, Title = null!, Count = null!;
        internal readonly List<CompactTrackRow> Preview = [];
        internal readonly List<UserPlaylistActionView> Actions = [];
    }
    private ItemsControl userPublicationList = null!;
    private ViewportItemsHost userPublicationHost = null!;
    private TextBlock userActivityEmpty = null!;
    private readonly Dictionary<StackPanel, UserPublicationView> userPublicationViews = [];

    private FrameworkElement UserPublicationListView()
    {
        userPublicationList = new ItemsControl().Items(Array.Empty<LibraryItem>(), item => item.Key)
            .VariableHeightPresenter().ItemHeight(TrackRowLayout.Stride).Background(Color.Transparent).BorderThickness(0).Padding(0)
            .ItemPadding(new Thickness(0, 0, 0, 36));
        userPublicationList.ItemTemplate = new DelegateTemplate<LibraryItem>(context =>
        {
            var host = new StackPanel().Vertical();
            userPublicationViews.Add(host, new()); context.Register("host", host); return host;
        }, (_, item, _, context) => BindUserPublication(context.Get<StackPanel>("host"), item),
            (_, _, _, context) => ClearUserPublication(context.Get<StackPanel>("host")));
        return userPublicationHost = new ViewportItemsHost(userPublicationList, () => userOverviewScroll.Bounds, ScrollShellWheel,
            delta => userOverviewScroll.SetScrollOffsets(0, userOverviewScroll.VerticalOffset + delta), trailingGap: 36).Initialize();
    }

    private UserPublicationByline PooledUserByline(TextBlock author)
    {
        var actor = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(actor, () => userProfileState?.User);
        return new(author, actor, new TextBlock().FontSize(11).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis), false);
    }

    private void BindUserPublication(StackPanel host, LibraryItem item)
    {
        ClearUserPublication(host);
        var view = userPublicationViews[host]; view.Item = item;
        var revision = ++view.Revision; var user = userProfileState!.User; var generation = navigationGeneration;
        bool Current() => view.Item != null && view.Revision == revision && IsUserProfileCurrent(user.Id, generation);
        var repost = item.ActivityType?.Contains("repost", StringComparison.OrdinalIgnoreCase) == true;
        var date = UserPublicationDate(item.ActivityAt ?? item.Track?.CreatedAt ?? item.Playlist?.CreatedAt);
        FrameworkElement body;
        if (item.Track is { } track)
        {
            view.Track ??= CreateUserTrackRow(publication: true, pooled: true,
                byline: author => view.TrackByline = PooledUserByline(author));
            view.TrackByline!.Bind(user.Username, date, repost);
            userPublicationRows.Add(view.Track);
            BindLibraryTrackRow(view.Track, track); body = view.Track.Root;
        }
        else
        {
            view.Playlist ??= CreatePooledUserPlaylist(view);
            BindPooledUserPlaylist(view, Current);
            view.PlaylistByline!.Bind(user.Username, date, repost); body = view.Playlist;
        }
        if (host.Children.Count > 0 && !ReferenceEquals(host.Children[0], body)) host.Remove(host.Children[0]);
        if (host.Children.Count == 0) host.Add(body);
        userPublications.Add((item, body));
    }

    private FrameworkElement CreatePooledUserPlaylist(UserPublicationView view)
    {
        void Open() { if (view.Item is { } item) Run(() => OpenLibraryItemAsync(item)); }
        view.Cover = new Image().Width(TrackRowLayout.CoverSize).Height(TrackRowLayout.CoverSize).StretchMode(Stretch.UniformToFill);
        view.Title = new TextBlock().FontSize(15).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        view.Title.Cursor = CursorType.Hand;
        view.Title.MouseDown += args => { if (args.Button == MouseButton.Left) { args.Handled = true; Open(); } };
        view.Author = new TextBlock().FontSize(12).SemiBold().Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(view.Author, () => view.Item?.Playlist?.User);
        view.Play = Icons.View("play-solid", 22, TrackButtons.OnPrimary).Center();
        var play = TrackButtons.Icon(view.Play, () => { if (view.Item is { } item) Run(() => PlayLibraryCollectionAsync(item)); }, TrackButtons.Filled, 40);
        view.PlaylistByline = PooledUserByline(view.Author);
        var heading = new Grid().Columns("40,*").Rows("40").Spacing(10).Children(play.Column(0),
            new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(view.PlaylistByline, view.Title));
        var artwork = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(new Border().CornerRadius(TrackRowLayout.CornerRadius).ClipToBounds().Child(ArtworkLayer(view.Cover))).OnClick(Open);
        var preview = new StackPanel().Vertical().Spacing(2);
        for (var i = 0; i < 3; i++)
        {
            var row = CreateCompactTrackRow(track => { if (view.Item is { } item) Run(() => PlayUserPlaylistTrackAsync(item, track)); }, dense: true);
            row.Root.Margin = new Thickness(-8, 0, -8, 0); row.Root.IsVisible = false;
            view.Preview.Add(row); preview.Add(row.Root);
        }
        view.Count = new TextBlock().FontSize(12).SemiBold();
        var more = TrackButtons.Action(view.Count, Open, TrackButtons.Text, 28).Padding(0).Left();
        var actions = UserPlaylistActions(view.Item!, view.Actions, currentItem: () => view.Item);
        var root = new Grid().Columns($"{TrackRowLayout.CoverSize},*").Rows("Auto").Spacing(TrackRowLayout.ColumnGap).Children(artwork.Column(0).Margin(0, 8, 0, 0).Top(),
            new StackPanel().Vertical().Spacing(10).Margin(TrackRowLayout.ContentInset, 8, TrackRowLayout.ContentInset, 0).Column(1)
                .Children(heading, preview, more, actions));
        VisualTree.Visit(root, element => { if (element is MaterialTrackButton button) button.RetainDecorationOnDetach = true; });
        return root;
    }

    private void BindPooledUserPlaylist(UserPublicationView view, Func<bool> current)
    {
        var item = view.Item!; var playlist = item.Playlist!;
        view.Title.Text = item.Title; view.Author.Text = playlist.User?.Username ?? item.Subtitle;
        view.Play.Source = Icons.Source(playbackQueue.Context?.Key == item.Key && isPlaying.Value ? "pause-solid" : "play-solid", TrackButtons.OnPrimary);
        userPlaylistPlayIcons.Add((item, view.Play)); userPublicationCovers.Add(view.Cover);
        foreach (var actions in view.Actions) { actions.Item = item; userPlaylistActionViews.Add(actions); }
        RefreshUserPlaylistActions(); SetCollectionArtwork(view.Cover, item);
        var count = UserPlaylistTrackCount(playlist);
        view.Count.Text = $"{(count == 1 ? "Всего" : "Все")} {count:N0} {RussianPlural(count, "трек", "трека", "треков")}" + (playlist.Duration > 0 ? " · " + FormatTime(playlist.Duration / 1000) : "");
        void Preview(SoundCloudTrack[] tracks)
        {
            var values = tracks.Where(track => !string.IsNullOrWhiteSpace(track.Title)).Take(3).ToArray();
            for (var i = 0; i < view.Preview.Count; i++)
            {
                var row = view.Preview[i]; BindCompactTrackRow(row, i < values.Length ? values[i] : null);
                row.Root.IsVisible = row.Track != null;
                if (row.Track != null && !userPreviewRows.Contains(row)) userPreviewRows.Add(row);
            }
        }
        Preview(collectionDetails.GetValueOrDefault(item.Key)?.Tracks.Tracks ?? playlist.Tracks ?? []);
        Run(async () =>
        {
            try
            {
                var source = await GetCollectionArtworkAsync(item);
                if (!current()) return;
                SetCollectionArtwork(view.Cover, item, source, finished: true);
                if (view.Preview.Any(row => row.Track != null) || playlist.TrackCount == 0) return;
                var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, lifetime.Token);
                if (!current()) return;
                collectionDetails[item.Key] = detail; Preview(detail.Tracks.Tracks);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException) { }
        });
    }

    private void ClearUserPublication(StackPanel host)
    {
        if (!userPublicationViews.TryGetValue(host, out var view) || view.Item == null) return;
        userPublications.RemoveAll(entry => ReferenceEquals(entry.Root, view.Track?.Root) || ReferenceEquals(entry.Root, view.Playlist));
        view.Item = null; ++view.Revision;
        if (view.Track is { } track) { ClearLibraryTrackRow(track); userPublicationRows.Remove(track); }
        foreach (var row in view.Preview) { BindCompactTrackRow(row, null); userPreviewRows.Remove(row); }
        if (view.Playlist != null)
        {
            ClearCardArtwork(view.Cover); userPublicationCovers.Remove(view.Cover);
            userPlaylistPlayIcons.RemoveAll(entry => ReferenceEquals(entry.Icon, view.Play));
        }
        foreach (var actions in view.Actions) userPlaylistActionViews.Remove(actions);
    }

    private void DisposeUserPublicationViews()
    {
        foreach (var (host, view) in userPublicationViews)
        {
            ClearUserPublication(host);
            if (view.Track is { } track) { track.ReleaseRenderingResources(); likedRows.Remove(track.Root); }
            foreach (var row in view.Preview) compactTrackRows.Remove(row);
            if (view.Playlist != null) VisualTree.Visit(view.Playlist, element =>
            { if (element is MaterialTrackButton button) button.ReleaseDecorationResources(); });
        }
        userPublicationViews.Clear();
    }
}
