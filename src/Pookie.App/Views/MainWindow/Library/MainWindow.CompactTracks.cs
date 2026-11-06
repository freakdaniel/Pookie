using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly List<CompactTrackRow> compactTrackRows = [];

    private sealed record CompactTrackRow(Grid Root, Image Cover, Image Play, InkBorder Overlay, Button Like,
        Image Heart, TextBlock Title, TextBlock Author, TextBlock Duration, InkBorder HoverFill)
    {
        public SoundCloudTrack? Track { get; set; }
        public bool Hovered { get; set; }
        public long? EntryId { get; set; }
        public bool CollectionRow { get; init; }
        public int SourceIndex { get; set; }
        public Button? More { get; set; }
        public MenuItem? RepostMenu { get; set; }
        public Button PlaybackButton { get; set; } = null!;
        public InkBorder Summary { get; init; } = null!;
        public InkBorder Actions { get; init; } = null!;
        public CompactTrackTail Trailing { get; init; } = null!;
        public StackPanel? ExtraActions { get; set; }
        public TextBlock Plays { get; init; } = null!;
        public Button? Repost { get; set; }
        public Image? RepostIcon { get; set; }
        public bool Miniature { get; init; }
        public Button[] ActionButtons { get; set; } = [];
    }
    private CompactTrackRow CreateCompactTrackRow(Action<SoundCloudTrack>? select = null, Color? secondary = null, bool queueRow = false, bool collectionRow = false, Action? open = null, bool dense = false, bool miniature = false)
    {
        var coverSize = miniature ? 20 : dense ? 32 : 44;
        var cover = new Image().Width(coverSize).Height(coverSize).StretchMode(Stretch.UniformToFill);
        var play = Icons.View("play-solid", miniature ? 10 : 18, TrackButtons.OnPrimary).CenterHorizontal().CenterVertical();
        CompactTrackRow row = null!;
        var playback = TrackButtons.Icon(play, () => { if (row.Track is { } track) (select ?? SelectLibraryTrack)(track); }, TrackButtons.Filled, miniature ? 18 : 30).Center();
        var overlay = new InkBorder().Width(coverSize).Height(coverSize)
            .CornerRadius(0).Background(Color.FromArgb(170, 0, 0, 0)).Content(playback);
        overlay.Strength = 0;
        overlay.Transitions = [Transition.Create(InkBorder.StrengthProperty, 150)];
        var frame = new Border().Width(coverSize).Height(coverSize).CornerRadius(5).ClipToBounds()
            .Child(ArtworkLayer(cover).Children(overlay));
        var title = new TextBlock().FontSize(dense ? 12 : 14).SemiBold().Left().Height(dense ? 18 : 21).TextTrimming(TextTrimming.CharacterEllipsis);
        var author = new TextBlock().FontSize(dense ? 11 : 12).Foreground(secondary ?? Muted).Height(dense ? 16 : 19).TextTrimming(TextTrimming.CharacterEllipsis).IsVisible(!miniature);
        var duration = new TextBlock { TextAlignment = TextAlignment.Right }.Width(CompactTrackTail.StatisticsWidth).FontSize(12).Foreground(secondary ?? Muted).Right();
        var plays = new TextBlock().FontSize(10).Foreground(secondary ?? Muted).Right().IsVisible(!miniature);
        var statistics = new StackPanel().Vertical().Spacing(2).Right().CenterVertical().Children(duration, plays);
        var heart = Icons.View("heart", 17, Muted).CenterHorizontal().CenterVertical();
        var like = TrackButtons.Icon(heart, () => { }, TrackButtons.Text, 32).CenterVertical().IsVisible(!miniature);
        // These are opacity layers, not rounded surfaces: avoid a mask per row.
        var summary = new InkBorder().CornerRadius(0).Background(Color.Transparent).Content(statistics);
        var actions = new InkBorder().CornerRadius(0).Background(Color.Transparent);
        actions.Strength = 0; actions.IsHitTestVisible = false;
        var trailing = new CompactTrackTail(like, summary, actions, miniature);
        actions.IsVisible = !miniature; like.IsVisible = !miniature;
        var action = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(new Grid().Columns($"{coverSize},*").Rows("*").Spacing(12).Children(frame.Column(0).CenterVertical(),
                new StackPanel().Vertical().CenterVertical().Column(1).Children(title, author)));
        ((FrameworkElement)action.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        var root = new ViewportGrid().Columns("*,Auto").Rows("*")
            .Spacing(miniature ? 4 : 8).Padding(miniature ? 0 : 8, miniature ? 0 : 6).Height(miniature ? 24 : dense ? 48 : 64);
        var hoverFill = new InkBorder().CornerRadius(10).Background(Color.FromArgb(22, 255, 255, 255))
            .BorderThickness(0).Padding(0).Margin(miniature ? 0 : -8, miniature ? 0 : -6).Column(0).ColumnSpan(2);
        hoverFill.Strength = 0;
        hoverFill.Transitions = [Transition.Create(InkBorder.StrengthProperty, 150)];
        hoverFill.IsHitTestVisible = false;
        root.Children(hoverFill, action.Column(0), trailing.Column(1));
        row = new CompactTrackRow(root, cover, play, overlay, like, heart, title, author, duration, hoverFill)
        { CollectionRow = collectionRow, PlaybackButton = playback, Summary = summary, Actions = actions, Trailing = trailing,
            Plays = plays, Miniature = miniature };
        SetCompactActionReveal(row, false, animate: false);
        root.SizeChanged += e =>
        {
            var wide = !miniature && e.NewSize.Width >= CompactTrackTail.FullActionsThreshold;
            if (row.ExtraActions is { } extras) extras.IsVisible = wide;
            trailing.Width = miniature ? 48 : wide ? CompactTrackTail.FullWidth : CompactTrackTail.CompactWidth;
        };
        compactTrackRows.Add(row);
        if (!queueRow) AttachTrackQueueMenu(root, () => row.Track, extraActions: true);
        if (!queueRow) row.RepostMenu = root.ContextMenu!.Items.OfType<MenuItem>().Last();
        if (collectionRow) AttachPlaylistTrackEdits(root, () => row.Track, () => row.SourceIndex);
        foreach (var button in new[] { action, playback, like }) TrackCompactActionFocus(row, button);
        AttachUserLink(author, () => row.Track?.User);
        if (open == null) AttachTrackTitle(title, () => row.Track);
        else
        {
            title.Cursor = CursorType.Hand;
            title.MouseDown += args => { if (args.Button == MouseButton.Left) { args.Handled = true; open(); } };
        }
        action.Click += () =>
        {
            if (row.Track is not { } item) return;
            if (open != null) { open(); return; }
            if (queueRow) (select ?? SelectLibraryTrack)(item);
            else Run(() => OpenTrackPageAsync(item));
        };
        like.Click += () => { if (row.Track is { } item) Run(() => ToggleTrackLikeAsync(item)); };
        root.MouseEnter += () => { row.Hovered = true; RefreshCompactTrackRow(row); };
        root.MouseLeave += () => { row.Hovered = false; RefreshCompactTrackRow(row); };
        return row;
    }

    private void BindCompactTrackRow(CompactTrackRow row, SoundCloudTrack? track, bool loadArtwork = true)
    {
        if (track != null && Equals(row.Track, track)) { RefreshCompactTrackRow(row); return; }
        if (row.Root.IsFocusWithin) Window.FocusManager.ClearFocus();
        row.Track = track; row.Hovered = row.Root.IsMouseOver;
        SetCompactActionReveal(row, row.Hovered && track != null, animate: false);
        if (track == null) { row.EntryId = null; row.Hovered = false; row.Overlay.Strength = row.HoverFill.Strength = 0; StopCardArtwork(row.Cover); row.Cover.Source = null; return; }
        row.Title.Text = track.Title; row.Author.Text = track.Author; row.Duration.Text = FormatTime(track.DurationSeconds);
        row.Plays.Text = track.PlaybackCount is { } count ? LikedTrackRow.ShortCount(count) : "—";
        row.Plays.IsVisible = !row.Miniature && track.Id > 0;
        row.Like.IsVisible = !row.Miniature && track.Id > 0;
        row.Actions.IsVisible = !row.Miniature && track.Id > 0;
        SetCardArtwork(row.Cover, libraryCoverCache.GetValueOrDefault(track.Id), track.ArtworkUrl ?? track.User?.AvatarUrl);
        if (loadArtwork) Run(async () =>
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
        var showActions = row.Hovered || row.Root.IsFocusWithin && Window.FocusManager.FocusedElement is Control focused && KeyboardFocusFeedback.IsVisible(focused);
        if (!showActions && row.Actions.IsFocusWithin) Window.FocusManager.ClearFocus();
        if (showActions && !row.Miniature && track.Id > 0) EnsureCompactTrackActions(row);
        if (row.RepostMenu != null) row.RepostMenu.Text = repostedIds.Contains(track.Id) ? "Убрать репост" : "Сделать репост";
        var selected = row.EntryId is { } entryId ? playbackQueue.Current?.EntryId == entryId :
            row.CollectionRow && playbackQueue.Context?.Key == displayedCollection?.Key
                ? current?.Id == track.Id && playbackQueue.Current?.SourceIndex == row.SourceIndex : current?.Id == track.Id;
        row.HoverFill.Strength = selected || row.Hovered ? 1 : 0;
        row.Play.Source = Icons.Source(selected && isPlaying.Value ? "pause-solid" : "play-solid", TrackButtons.OnPrimary);
        row.Overlay.Strength = selected || row.Hovered ? 1 : 0;
        var liked = IsTrackLiked(track);
        TrackButtons.SetGlyph(row.Heart, liked ? "heart-filled" : "heart", liked ? LikedHeart : row.Hovered ? Color.White : Muted);
        TrackButtons.SetAvailability(row.Like, CanLikeTrack(track), IsLikePending(track));
        row.Like.Hint(liked ? "Убрать из понравившегося" : "Добавить в понравившееся");
        if (row.Repost is { } repost && row.RepostIcon is { } repostIcon)
        {
            var reposted = repostedIds.Contains(track.Id);
            TrackButtons.SetGlyph(repostIcon, "repeat", reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface);
            TrackButtons.SetAvailability(repost, CanRepostTrack(track), IsRepostPending(track));
            repost.Hint(reposted ? "Убрать репост" : "Сделать репост");
        }
        SetCompactActionReveal(row, showActions);
    }

    private static void SetCompactActionReveal(CompactTrackRow row, bool show, bool animate = true)
    {
        show &= !row.Miniature && row.Track is { Id: > 0 };
        row.Trailing.SetExpanded(show, animate);
        foreach (var button in row.ActionButtons) button.IsTabStop = show;
    }

}
