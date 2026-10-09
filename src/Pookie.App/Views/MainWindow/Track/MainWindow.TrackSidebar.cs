using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBlock trackDetailSidebarLikes = null!, trackDetailSidebarReposts = null!;
    private StackPanel trackDetailFans = null!, trackDetailAlbums = null!;
    private TrackCollectionGrid trackDetailPlaylists = null!;
    private FrameworkElement trackDetailFansSection = null!, trackDetailPlaylistsSection = null!, trackDetailAlbumsSection = null!;
    private TrackSidebar? renderedTrackSidebar;
    private long renderedSidebarTrack, renderedSidebarGeneration;

    private FrameworkElement TrackDetailSidebarView()
    {
        trackDetailAvatar = new Image().Width(64).Height(64).StretchMode(Stretch.UniformToFill);
        trackDetailAuthorName = new TextBlock().FontSize(16).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        trackDetailAuthorStats = new TextBlock().FontSize(11).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        var profile = TrackButtons.Action(new Grid().Columns("64,*").Rows("Auto").Spacing(14).Children(
            new Border().CornerRadius(32).ClipToBounds().Child(trackDetailAvatar).Column(0),
            new StackPanel().Vertical().Spacing(6).CenterVertical().Column(1).Children(trackDetailAuthorName, trackDetailAuthorStats)),
            () => { if (trackDetailState?.Track.User is { Id: > 0 } user) Run(() => OpenLibraryItemAsync(new("user:" + user.Id, user.Username, "Исполнитель", user.AvatarUrl, User: user))); },
            TrackButtons.Text, 72).Padding(0);
        ((FrameworkElement)profile.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        trackDetailStats = new TextBlock(); trackDetailSidebarLikes = new TextBlock(); trackDetailSidebarReposts = new TextBlock();
        FrameworkElement Metric(TextBlock value, string label)
        {
            value.FontSize(17).Bold().TextTrimming(TextTrimming.CharacterEllipsis);
            return new Border().Background(Raised).CornerRadius(12).Padding(10, 14).Child(
                new StackPanel().Vertical().Spacing(5).Children(value, new TextBlock().Text(label).FontSize(10).Foreground(Muted)));
        }
        var stats = new Grid().Columns("1.4*,*,*").Rows("Auto").Spacing(8).Children(
            Metric(trackDetailStats, "Прослушиваний").Column(0), Metric(trackDetailSidebarLikes, "Лайки").Column(1),
            Metric(trackDetailSidebarReposts, "Репосты").Column(2));
        trackDetailFans = new StackPanel().Vertical().Spacing(8);
        trackDetailPlaylists = new TrackCollectionGrid(2, 0, 16);
        trackDetailAlbums = new StackPanel().Vertical().Spacing(4);
        trackDetailFansSection = SidebarSection("Фанаты", trackDetailFans);
        trackDetailPlaylistsSection = SidebarSection("В плейлистах", trackDetailPlaylists, TrackSection.Playlists);
        trackDetailAlbumsSection = SidebarSection("В альбомах", trackDetailAlbums, TrackSection.Albums);
        trackDetailRelatedStatus = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        var related = new StackPanel().Vertical().Spacing(4);
        for (var i = 0; i < 6; i++)
        {
            var row = CreateCompactTrackRow(); row.Root.Margin = new Thickness(-8, 0, -8, 0);
            trackDetailRelatedRows.Add(row); related.Add(row.Root);
        }
        var artistRow = new Grid().Columns("*,40").Rows("Auto").Spacing(12).Children(profile.Column(0),
            ArtistFollowButton(() => trackDetailState?.Track.User).Column(1).CenterVertical());
        return new StackPanel().Vertical().Spacing(28).Children(artistRow, stats, trackDetailFansSection,
            new StackPanel().Vertical().Spacing(14).Children(SidebarHeading("Похожие треки", TrackSection.Related), trackDetailRelatedStatus, related),
            trackDetailPlaylistsSection, trackDetailAlbumsSection);
    }

    private FrameworkElement SidebarHeading(string title, TrackSection section) =>
        SectionHeader(title, () => Run(() => OpenTrackSectionAsync(section)));

    private FrameworkElement SidebarSection(string title, FrameworkElement rows, TrackSection? section = null) => new StackPanel().Vertical().Spacing(14)
        .Children(section is { } target ? SidebarHeading(title, target) : new TextBlock().Text(title).FontSize(18).Bold(), rows);

    private async Task LoadTrackDetailSidebarAsync(long id, long generation, CancellationToken token)
    {
        try
        {
            var result = await api.GetTrackSidebarAsync(id, token);
            if (!IsTrackDetailCurrent(id, generation)) return;
            trackDetailState = trackDetailState! with { Sidebar = result, SidebarLoading = false };
            RenderTrackDetailSidebar();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        {
            if (!IsTrackDetailCurrent(id, generation)) return;
            trackDetailState = trackDetailState! with { SidebarLoading = false };
            RenderTrackDetailSidebar();
        }
    }

    private void RenderTrackDetailSidebar()
    {
        if (trackDetailState is not { } state) return;
        var data = state.Sidebar;
        trackDetailFansSection.IsVisible = data is { Fans.Length: > 0 };
        trackDetailPlaylistsSection.IsVisible = data is { Playlists.Length: > 0 };
        trackDetailAlbumsSection.IsVisible = data is { Albums.Length: > 0 };
        if (renderedSidebarTrack == state.Track.Id && renderedSidebarGeneration == navigationGeneration && ReferenceEquals(renderedTrackSidebar, data)) return;
        renderedSidebarTrack = state.Track.Id; renderedSidebarGeneration = navigationGeneration; renderedTrackSidebar = data;
        trackDetailFans.Clear(); trackDetailPlaylists.Clear(); trackDetailAlbums.Clear();
        if (data == null) return;
        foreach (var fan in data.Fans)
        {
            var avatar = new Image().Width(36).Height(36).StretchMode(Stretch.UniformToFill);
            avatar.Source = Icons.Source("user", Muted);
            var row = TrackButtons.Action(new Grid().Columns("36,*,Auto").Rows("Auto").Spacing(10).Children(
                new Border().CornerRadius(18).ClipToBounds().Child(avatar).Column(0),
                new TextBlock().Text(fan.User.Username).FontSize(13).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).Column(1).CenterVertical(),
                new TextBlock().Text(DetailCount(fan.Plays) + " воспроизведений").FontSize(11).Foreground(Muted).Column(2).CenterVertical()),
                () => Run(() => OpenLibraryItemAsync(new("user:" + fan.User.Id, fan.User.Username, "Исполнитель", fan.User.AvatarUrl, User: fan.User))),
                TrackButtons.Text, 44).Padding(0, 0, 16, 0);
            ((FrameworkElement)row.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
            trackDetailFans.Add(row); LoadTrackDetailAvatar(avatar, fan.User.AvatarUrl, state.Track.Id);
        }
        foreach (var item in data.Playlists.Take(4)) trackDetailPlaylists.Add(TrackCollectionCard(item, state.Track.Id));
        foreach (var item in data.Albums) trackDetailAlbums.Add(TrackSidebarCollection(item, state.Track.Id));
    }

    private FrameworkElement TrackCollectionCard(LibraryItem item, long trackId)
    {
        var card = CreateCollectionCardView(responsive: true);
        card.SetArtist(item.User != null);
        card.Title.Text = item.Title; card.Author.Text = item.Subtitle;
        card.Playback.IsVisible = false;
        var image = card.Cover;
        var button = card.Root.OnClick(() => Run(() => OpenLibraryItemAsync(item)));
        SetCollectionArtwork(image, item);
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(item);
            if (IsTrackDetailCurrent(trackId, generation)) SetCollectionArtwork(image, item, source, finished: true);
        });
        return button;
    }

    private FrameworkElement TrackSidebarCollection(LibraryItem item, long trackId)
    {
        var image = new Image().Width(52).Height(52).StretchMode(Stretch.UniformToFill);
        var cover = new Border().CornerRadius(6).ClipToBounds().Child(ArtworkLayer(image));
        SetCollectionArtwork(image, item);
        var row = TrackButtons.Action(new Grid().Columns("52,*").Rows("Auto").Spacing(12).Children(cover.Column(0),
            new StackPanel().Vertical().Spacing(5).CenterVertical().Column(1).Children(
                new TextBlock().Text(item.Title).FontSize(13).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                new TextBlock().Text(item.Subtitle).FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis))),
            () => Run(() => OpenLibraryItemAsync(item)), TrackButtons.Text, 64).Padding(0);
        ((FrameworkElement)row.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(item);
            if (IsTrackDetailCurrent(trackId, generation)) SetCollectionArtwork(image, item, source, finished: true);
        });
        return row;
    }
}
