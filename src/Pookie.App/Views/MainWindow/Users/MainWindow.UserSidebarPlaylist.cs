using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record SidebarPlaylistRow(LibraryItem Item, Grid Root, Image Cover, TrackArtworkOverlay Overlay,
        InkBorder HoverFill, TextBlock Title, TextBlock Author);
    private readonly List<SidebarPlaylistRow> userSidebarPlaylists = [];

    private FrameworkElement UserSidebarPlaylist(LibraryItem item, long profileId)
    {
        var playlist = item.Playlist!;
        var cover = new Image().Width(CompactCoverSize).Height(CompactCoverSize).StretchMode(Stretch.UniformToFill);
        var overlay = CreateCoverPlayback(cover, () => Run(() => PlayLibraryCollectionAsync(item)),
            30, 18, Color.FromArgb(170, 0, 0, 0)).Width(CompactCoverSize).Height(CompactCoverSize);
        var frame = new Border().Width(CompactCoverSize).Height(CompactCoverSize).CornerRadius(5).ClipToBounds()
            .Child(ArtworkLayer(cover).Children(overlay));
        var title = new TextBlock().Text(item.Title).FontSize(14).SemiBold().Left().Height(21).TextTrimming(TextTrimming.CharacterEllipsis);
        var author = new TextBlock().Text(playlist.User?.Username ?? item.Subtitle).FontSize(12).Foreground(Muted)
            .Left().Height(19).TextTrimming(TextTrimming.CharacterEllipsis);
        var count = playlist.TrackCount;
        var statistics = new StackPanel().Vertical().Spacing(2).Right().CenterVertical().Children(
            new StackPanel().Horizontal().Spacing(4).Right().Children(Icons.View("list-bullets", 12, Muted),
                new TextBlock().Text(item.IsAlbum ? "Альбом" : "Плейлист").FontSize(10).Foreground(Muted)),
            new TextBlock().Text(count + " " + RussianPlural(count, "трек", "трека", "треков")).FontSize(11).Foreground(Muted).Right());
        var action = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(new Grid().Columns($"{CompactCoverSize},*").Rows("*").Spacing(12).Children(frame.CenterVertical(),
                new StackPanel().Vertical().CenterVertical().Column(1).Children(title, author)))
            .OnClick(() => Run(() => OpenLibraryItemAsync(item)));
        ((FrameworkElement)action.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        var root = new ViewportGrid().Columns("*,Auto").Rows("*").Spacing(8).Padding(8, 6).Height(64).Margin(-8, 0, -8, 0);
        var hover = new InkBorder().CornerRadius(10).Background(Color.FromArgb(22, 255, 255, 255))
            .BorderThickness(0).Padding(0).Margin(-8, -6).ColumnSpan(2);
        hover.Strength = 0; hover.IsHitTestVisible = false;
        hover.Transitions = [Transition.Create(InkBorder.StrengthProperty, 150)];
        root.Children(hover, action, statistics.Column(1));
        var row = new SidebarPlaylistRow(item, root, cover, overlay, hover, title, author);
        userSidebarPlaylists.Add(row);
        AttachUserLink(author, () => playlist.User);
        title.Cursor = CursorType.Hand;
        AttachTextLinkHover(title, normal => Color.FromArgb((byte)Math.Round(normal.A * .72), normal.R, normal.G, normal.B));
        title.MouseDown += args =>
        {
            if (args.Button != MouseButton.Left) return;
            args.Handled = true; Run(() => OpenLibraryItemAsync(item));
        };
        root.MouseEnter += () => RefreshUserSidebarPlaylist(row);
        root.MouseLeave += () => RefreshUserSidebarPlaylist(row);
        foreach (var button in new[] { action, overlay.PlaybackButton! })
        {
            KeyboardFocusFeedback.Attach(button);
            button.GotFocus += () => RefreshUserSidebarPlaylist(row);
            button.LostFocus += () => RefreshUserSidebarPlaylist(row);
        }
        RefreshUserSidebarPlaylist(row);
        SetCardArtwork(cover, null, item.ArtworkUrl, "music-notes");
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await CompactArtworkAsync(item.ArtworkUrl, CompactArtworkSide(CompactCoverSize));
            if (IsUserProfileCurrent(profileId, generation) && userSidebarPlaylists.Contains(row))
                SetCardArtwork(cover, source, item.ArtworkUrl, "music-notes", finished: true);
        });
        return root;
    }

    private void RefreshUserSidebarPlaylist(SidebarPlaylistRow row)
    {
        var selected = current != null && playbackQueue.Context?.Key == row.Item.Key;
        var hovered = row.Root.IsMouseOver || row.Root.IsFocusWithin &&
            Window.FocusManager.FocusedElement is Control focused && KeyboardFocusFeedback.IsVisible(focused);
        row.HoverFill.Strength = selected || hovered ? 1 : 0;
        row.Overlay.SetHovered(hovered);
        row.Overlay.SetPlaying(selected, isPlaying.Value);
    }

    private void ClearUserSidebarPlaylists()
    {
        foreach (var row in userSidebarPlaylists) { row.Overlay.Reset(); ClearCardArtwork(row.Cover); }
        userSidebarPlaylists.Clear();
    }
}
