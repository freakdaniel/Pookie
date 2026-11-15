using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class FeedPublicationView(FeedEntry entry)
    {
        internal FeedEntry Entry = entry;
        internal bool Bound;
        internal long Revision;
        internal LibraryItem? CurrentItem => Bound ? Entry.Item : null;
        internal SoundCloudUser? Actor => Bound ? Entry.Actor ?? Entry.Item.Track?.User ?? Entry.Item.Playlist?.User : null;
        internal Image Avatar = null!, Repost = null!;
        internal TextBlock Name = null!, Publication = null!, Date = null!;
        internal LikedTrackRow? Track;
        internal FrameworkElement? PlaylistRoot;
        internal TextBlock PlaylistAuthor = null!, PlaylistTitle = null!, PlaylistCount = null!;
        internal readonly List<CompactTrackRow> Preview = [];
        internal readonly List<Image> Covers = [];
        internal readonly List<UserPlaylistActionView> Actions = [];
        internal Image? Play;
    }
    // The presenter recycles hosts. Keep their controls and render buffers too;
    // only the active dictionary participates in playback updates and async loads.
    private readonly Dictionary<StackPanel, FeedPublicationView> feedPublicationPool = [];
    private readonly Dictionary<StackPanel, FeedPublicationView> feedViews = [];
    private readonly List<UserPlaylistActionView> feedPlaylistActions = [];
    private readonly List<CompactTrackRow> feedSidebarRows = [];
    private FeedEntry[]? renderedFeedSidebar;

    private FeedPublicationView CreateFeedPublication(StackPanel host, FeedEntry entry)
    {
        var view = new FeedPublicationView(entry);
        view.Avatar = new Image().Width(28).Height(28).StretchMode(Stretch.UniformToFill);
        var avatar = new Border().Width(28).Height(28).CornerRadius(14).ClipToBounds().Child(view.Avatar);
        AttachUserLink(avatar, () => view.Actor);
        view.Name = new TextBlock().FontSize(12).SemiBold().MaxWidth(280).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(view.Name, () => view.Actor);
        view.Repost = Icons.View("repeat", 13, MutedIcon).CenterVertical();
        view.Publication = new TextBlock().FontSize(12).Foreground(Muted).CenterVertical();
        view.Date = new TextBlock().FontSize(11).Foreground(Muted).CenterVertical();
        var byline = new StackPanel().Horizontal().Spacing(7).CenterVertical().Children(view.Name, view.Repost, view.Publication);
        host.Add(new Grid().Columns("28,*,Auto").Rows("28").Spacing(10).Children(avatar.Column(0), byline.Column(1), view.Date.Column(2)));
        feedPublicationPool.Add(host, view);
        return view;
    }

    private void BindFeedPublication(StackPanel host, FeedEntry entry)
    {
        ClearFeedPublication(host);
        var view = feedPublicationPool.GetValueOrDefault(host) ?? CreateFeedPublication(host, entry);
        view.Entry = entry; view.Bound = true; var revision = ++view.Revision;
        feedViews[host] = view;
        bool Current() => !disposed && view.Bound && view.Revision == revision;
        var actor = view.Actor;
        view.Avatar.Source = Icons.Source("user-circle", MutedIcon);
        if (actor?.AvatarUrl != null) Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(UserItem(actor));
            if (Current() && source != null) view.Avatar.Source = source;
        });
        view.Name.Text = actor?.Username ?? entry.Item.Subtitle;
        view.Repost.IsVisible = entry.IsRepost;
        view.Publication.Text = entry.IsRepost ? "сделал репост" : "опубликовал";
        view.Date.Text = FeedDate(entry.CreatedAt);
        FrameworkElement? body = null;
        if (entry.Item.Track is { } track)
        {
            if (view.Track == null)
            {
                view.Track = CreateTrackRow(value => Run(() => PlayFeedTrackAsync(value)),
                    (value, fraction) => Run(() => SeekFeedTrackAsync(value, fraction)), TrackRowAppearance.Publication, pooled: true);
            }
            BindLibraryTrackRow(view.Track, track); body = view.Track.Root;
        }
        else if (entry.Item.Playlist != null)
        {
            view.PlaylistRoot ??= CreateFeedPlaylistPublication(view);
            BindFeedPlaylistPublication(view, Current);
            body = view.PlaylistRoot;
        }
        if (host.Children.Count > 1 && !ReferenceEquals(host.Children[1], body)) host.Remove(host.Children[1]);
        if (body != null && host.Children.Count == 1) host.Add(body);
    }

    private void ClearFeedPublication(StackPanel host)
    {
        if (!feedViews.Remove(host, out var view)) return;
        view.Bound = false; ++view.Revision;
        view.Avatar.Source = null;
        if (view.Track?.Track != null) ClearLibraryTrackRow(view.Track);
        foreach (var row in view.Preview) BindCompactTrackRow(row, null);
        foreach (var cover in view.Covers) ClearCardArtwork(cover);
        foreach (var actions in view.Actions) feedPlaylistActions.Remove(actions);
    }

    private FrameworkElement FeedAvatar(SoundCloudUser? user, int size, Func<bool> currentView)
    {
        var image = new Image().Width(size).Height(size).StretchMode(Stretch.UniformToFill).Source(Icons.Source("user-circle", MutedIcon));
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
                    if (IsCurrentTrack(value)) { await ToggleAsync(); return; }
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
            if (view.Entry.Item.Playlist != null && view.Play != null) view.Play.Source = Icons.Source(playbackQueue.Context?.Key == view.Entry.Item.Key && isPlaying.Value ? "pause-solid" : "play-solid", TrackButtons.OnPrimary);
    }

    private void DisposeFeed()
    {
        feedSkeleton.SetActive(false);
        foreach (var (host, view) in feedPublicationPool)
        {
            ClearFeedPublication(host);
            if (view.Track is { } row) { row.ReleaseRenderingResources(); likedRows.Remove(row.Root); }
            foreach (var preview in view.Preview) compactTrackRows.Remove(preview);
            if (view.PlaylistRoot != null) VisualTree.Visit(view.PlaylistRoot, element =>
            {
                if (element is MaterialTrackButton button) button.ReleaseDecorationResources();
            });
            host.Clear();
        }
        feedPublicationPool.Clear();
        foreach (var row in feedSidebarRows) { BindCompactTrackRow(row, null); compactTrackRows.Remove(row); }
        feedSidebarRows.Clear();
    }
}
