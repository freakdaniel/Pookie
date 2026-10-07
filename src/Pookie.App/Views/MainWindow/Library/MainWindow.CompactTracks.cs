using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly List<CompactTrackRow> compactTrackRows = [];

    private sealed record CompactTrackRow(Grid Root, Image Cover, Image Play, Border Overlay, Button Like,
        Image Heart, TextBlock Title, TextBlock Author, TextBlock Duration, Border HoverFill)
    {
        public SoundCloudTrack? Track { get; set; }
        public bool Hovered { get; set; }
        public long? EntryId { get; set; }
    }
    private CompactTrackRow CreateCompactTrackRow(Action<SoundCloudTrack>? select = null, Color? secondary = null, bool queueRow = false)
    {
        var cover = new Image().Width(44).Height(44).StretchMode(Stretch.UniformToFill);
        var play = Icons.View("play-solid", 18).CenterHorizontal().CenterVertical();
        var overlay = new Border().Background(Color.FromArgb(170, 0, 0, 0)).Child(play);
        overlay.Opacity = 0;
        overlay.Transitions = [Transition.Create(UIElement.OpacityProperty, 150)];
        var frame = new Border().Width(44).Height(44).CornerRadius(5).ClipToBounds()
            .Child(ArtworkLayer(cover).Children(overlay));
        var title = new TextBlock().FontSize(14).SemiBold().Height(21).TextTrimming(TextTrimming.CharacterEllipsis);
        var author = new TextBlock().FontSize(12).Foreground(secondary ?? Muted).Height(19).TextTrimming(TextTrimming.CharacterEllipsis);
        var duration = new TextBlock().FontSize(12).Foreground(secondary ?? Muted).Width(48).Right().CenterVertical();
        var heart = Icons.View("heart", 17, Muted).CenterHorizontal().CenterVertical();
        var like = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Width(28).Height(32)
            .CenterVertical().Content(heart);
        var action = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(new Grid().Columns("44,*").Rows("*").Spacing(12).Children(frame.Column(0).CenterVertical(),
                new StackPanel().Vertical().CenterVertical().Column(1).Children(title, author)));
        var root = new Grid().Columns("*,28,48").Rows("*").Spacing(12).Padding(8, 6).Height(64);
        var hoverFill = new Border().CornerRadius(6).Background(Color.FromArgb(22, 255, 255, 255))
            .BorderThickness(0).Padding(0).Margin(-8, -6).Column(0).ColumnSpan(3);
        hoverFill.Opacity = 0;
        hoverFill.Transitions = [Transition.Create(UIElement.OpacityProperty, 150)];
        hoverFill.IsHitTestVisible = false;
        root.Children(hoverFill, action.Column(0), like.Column(1), duration.Column(2));
        var row = new CompactTrackRow(root, cover, play, overlay, like, heart, title, author, duration, hoverFill);
        compactTrackRows.Add(row);
        if (!queueRow) AttachTrackQueueMenu(root, () => row.Track);
        action.Click += () => { if (row.Track is { } item) (select ?? SelectLibraryTrack)(item); };
        like.Click += () => { if (row.Track is { } item) Run(() => ToggleTrackLikeAsync(item)); };
        root.MouseEnter += () => { row.Hovered = true; RefreshCompactTrackRow(row); };
        root.MouseLeave += () => { row.Hovered = false; RefreshCompactTrackRow(row); };
        return row;
    }

    private void BindCompactTrackRow(CompactTrackRow row, SoundCloudTrack? track)
    {
        if (track != null && Equals(row.Track, track)) { RefreshCompactTrackRow(row); return; }
        row.Track = track;
        if (track == null) { row.EntryId = null; row.Hovered = false; row.Overlay.Opacity = row.HoverFill.Opacity = 0; StopCardArtwork(row.Cover); return; }
        row.Title.Text = track.Title; row.Author.Text = track.Author; row.Duration.Text = FormatTime(track.DurationSeconds);
        SetCardArtwork(row.Cover, libraryCoverCache.GetValueOrDefault(track.Id), track.ArtworkUrl ?? track.User?.AvatarUrl);
        Run(async () =>
        {
            var source = await GetLibraryArtworkAsync(track);
            if (!disposed && row.Track?.Id == track.Id)
                SetCardArtwork(row.Cover, source, track.ArtworkUrl ?? track.User?.AvatarUrl, finished: true);
        });
        RefreshCompactTrackRow(row);
    }

    private void RefreshCompactTrackRow(CompactTrackRow row)
    {
        if (row.Track is not { } track) return;
        var selected = row.EntryId is { } entryId ? playbackQueue.Current?.EntryId == entryId : current?.Id == track.Id;
        row.HoverFill.Opacity = selected || row.Hovered ? 1 : 0;
        row.Play.Source = Icons.Source(selected && isPlaying.Value ? "pause-solid" : "play-solid");
        row.Overlay.Opacity = selected || row.Hovered ? 1 : 0;
        var liked = likedIds.Contains(track.Id);
        row.Heart.Source = Icons.Source(liked ? "heart-filled" : "heart", liked ? LikedHeart : row.Hovered ? Color.White : Muted);
        row.Like.IsEnabled = me != null && !likeBusy && !demo;
    }

}
