using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private long renderedUserPublicationGeneration;
    private readonly List<Image> userPublicationCovers = [];
    private readonly List<(LibraryItem Item, Image Icon)> userPlaylistPlayIcons = [];
    private readonly Dictionary<FrameworkElement, Action> userPublicationLoads = [];
    private void LoadVisibleUserPublications()
    {
        if (page.Value != Page.UserProfile || userSection.Value != UserSection.Overview || userOverviewScroll.ActualHeight <= 0) return;
        var viewport = userOverviewScroll.Bounds;
        foreach (var (root, load) in userPublicationLoads.ToArray())
            if (root.ActualHeight > 0 && root.Bounds.Bottom >= viewport.Y - 180 && root.Bounds.Y <= viewport.Bottom + 180)
            { userPublicationLoads.Remove(root); load(); }
    }
    private LikedTrackRow CreateUserTrackRow(bool publication = false, LibraryItem? item = null)
    {
        var row = new LikedTrackRow(track => Run(() => PlayUserTrackAsync(track)), track => Run(() => OpenTrackPageAsync(track)),
            track => Run(() => ToggleTrackLikeAsync(track)), CopyTrackLink, track => Run(() => ToggleTrackRepostAsync(track)),
            track => Run(() => OpenPlaylistPickerAsync(track)), (track, fraction) => Run(() => SeekUserTrackAsync(track, fraction)),
            ArtworkLayer, glass: publication, publication: publication, byline: item == null ? null : author => CreateUserByline(author, item));
        likedRows.Add(row.Root, row);
        if (publication) row.AddQueueAction(track => EnqueueTrack(track, false));
        AttachTrackQueueMenu(row.Root, () => row.Track);
        AttachTrackTitle(row.Title, () => row.Track);
        AttachUserLink(row.Author, () => row.Track?.User);
        return row;
    }

    private FrameworkElement UserPublication(LibraryItem item, long id)
    {
        var loads = new List<Func<Task>>();
        FrameworkElement content;
        if (item.Track is { } track)
        {
            var row = CreateUserTrackRow(publication: true, item: item); userPublicationRows.Add(row);
            row.Bind(track); SetCardArtwork(row.Cover, libraryCoverCache.GetValueOrDefault(track.Id), track.ArtworkUrl ?? track.User?.AvatarUrl);
            likedArtworkTracks[row.Cover] = track.Id; RefreshLikedRow(row);
            loads.Add(() => Task.WhenAll(LoadLikedArtworkAsync(row.Cover, track), LoadLikedWaveformAsync(row, track), LoadLikedTrackArtAsync(row, track)));
            content = row.Root;
        }
        else content = UserPlaylistPublication(item, id, loads);
        userPublications.Add((item, content));
        userPublicationLoads[content] = () => Run(() => Task.WhenAll(loads.ToArray().Select(load => load())));
        return content;
    }

    private FrameworkElement UserPlaylistPublication(LibraryItem item, long id, List<Func<Task>> loads)
    {
        var playlist = item.Playlist!;
        var cover = new Image().Width(TrackRowLayout.CoverSize).Height(TrackRowLayout.CoverSize).StretchMode(Stretch.UniformToFill);
        userPublicationCovers.Add(cover); SetCollectionArtwork(cover, item);
        var title = new TextBlock().Text(item.Title).FontSize(15).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        title.Cursor = CursorType.Hand;
        title.MouseDown += args => { if (args.Button == MouseButton.Left) { args.Handled = true; Run(() => OpenLibraryItemAsync(item)); } };
        var author = new TextBlock().Text(playlist.User?.Username ?? item.Subtitle).FontSize(12).SemiBold().Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(author, () => playlist.User);
        var playIcon = Icons.View(playbackQueue.Context?.Key == item.Key && isPlaying.Value ? "pause-solid" : "play-solid", 22, TrackButtons.OnPrimary).Center();
        userPlaylistPlayIcons.Add((item, playIcon));
        var play = TrackButtons.Icon(playIcon, () => Run(() => PlayLibraryCollectionAsync(item)), TrackButtons.Filled, 40);
        var heading = new Grid().Columns("40,*").Rows("40").Spacing(10).Children(play.Column(0),
            new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(CreateUserByline(author, item), title));
        var artwork = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(new Border().CornerRadius(TrackRowLayout.CornerRadius).ClipToBounds().Child(ArtworkLayer(cover)))
            .OnClick(() => Run(() => OpenLibraryItemAsync(item)));
        var preview = new StackPanel().Vertical().Spacing(2);
        void BindPreview(SoundCloudTrack[] tracks)
        {
            foreach (var track in tracks.Where(track => !string.IsNullOrWhiteSpace(track.Title)).Take(3))
            {
                var row = CreateCompactTrackRow(value => Run(() => PlayUserPlaylistTrackAsync(item, value)), dense: true);
                row.Root.Margin = new Thickness(-8, 0, -8, 0);
                BindCompactTrackRow(row, track, loadArtwork: false); userPreviewRows.Add(row); preview.Add(row.Root);
                loads.Add(async () => { var source = await GetLibraryArtworkAsync(track); if (!disposed && row.Track?.Id == track.Id) SetCardArtwork(row.Cover, source, track.ArtworkUrl ?? track.User?.AvatarUrl, finished: true); });
            }
        }
        BindPreview(playlist.Tracks ?? []);
        var count = UserPlaylistTrackCount(playlist);
        var more = TrackButtons.Action(new TextBlock().Text($"{(count == 1 ? "Всего" : "Все")} {count:N0} {RussianPlural(count, "трек", "трека", "треков")}" + (playlist.Duration > 0 ? " · " + FormatTime(playlist.Duration / 1000) : "")).FontSize(12).SemiBold(),
            () => Run(() => OpenLibraryItemAsync(item)), TrackButtons.Text, 28).Padding(0).Left();
        var root = new Grid().Columns($"{TrackRowLayout.CoverSize},*").Rows("Auto").Spacing(TrackRowLayout.ColumnGap).Children(artwork.Column(0).Margin(0, 8, 0, 0).Top(),
            new StackPanel().Vertical().Spacing(10).Margin(TrackRowLayout.ContentInset, 8, TrackRowLayout.ContentInset, 0).Column(1)
                .Children(heading, preview, more, UserPlaylistActions(item)));
        var generation = navigationGeneration;
        loads.Add(async () =>
        {
            var source = await GetCollectionArtworkAsync(item);
            if (!IsUserProfileCurrent(id, generation) || !userPublicationCovers.Contains(cover)) return;
            SetCollectionArtwork(cover, item, source, finished: true);
            if (preview.Children.Count > 0 || playlist.TrackCount == 0) return;
            try
            {
                var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, loading!.Token);
                if (!IsUserProfileCurrent(id, generation) || !userPublicationCovers.Contains(cover)) return;
                collectionDetails[item.Key] = detail;
                var previousLoads = loads.Count; BindPreview(detail.Tracks.Tracks);
                await Task.WhenAll(loads.Skip(previousLoads).Select(load => load()));
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException) { }
        });
        return root;
    }

    private FrameworkElement CreateUserByline(TextBlock author, LibraryItem item)
    {
        var user = userProfileState!.User;
        var repost = item.ActivityType?.Contains("repost", StringComparison.OrdinalIgnoreCase) == true;
        var actor = new TextBlock().Text(user.Username).FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(actor, () => user);
        var date = new TextBlock().Text(UserPublicationDate(item.ActivityAt ?? item.Track?.CreatedAt ?? item.Playlist?.CreatedAt))
            .FontSize(11).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        return new UserPublicationByline(author, actor, date, repost);
    }

    private static int UserPlaylistTrackCount(SoundCloudPlaylist playlist) => Math.Max(playlist.TrackCount, playlist.Tracks?.Length ?? 0);
    private static bool UserItemVisible(LibraryItem item) => item.Playlist == null || UserPlaylistTrackCount(item.Playlist) > 0;

    private async Task PlayUserPlaylistTrackAsync(LibraryItem item, SoundCloudTrack track)
    {
        if (playbackQueue.Context?.Key == item.Key && current?.Id == track.Id) { await ToggleAsync(); return; }
        var session = api.Session; var sourceVersion = playbackQueue.SourceVersion; var generation = playGeneration;
        var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, lifetime.Token);
        if (disposed || api.Session != session || playbackQueue.SourceVersion != sourceVersion || playGeneration != generation) return;
        collectionDetails[item.Key] = detail;
        StartQueueContext(new(item.Key, item.Title, "Сейчас играет плейлист", detail.Tracks.NextHref,
            Kind: item.IsAlbum ? Playback.PlaybackContextKind.Album : Playback.PlaybackContextKind.Playlist), detail.Tracks.Tracks, track);
        await PlayAsync(track);
    }

    private static string UserPublicationDate(string? value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return "";
        var days = Math.Max(0, (DateTimeOffset.UtcNow - date).TotalDays);
        return days < 1 ? "Сегодня" : days < 30 ? $"{(int)days} дн. назад" : date.ToLocalTime().ToString("d MMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
    }
}
