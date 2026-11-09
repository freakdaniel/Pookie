using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly List<CompactTrackRow> compactTrackRows = [];

    private sealed record CompactTrackRow(Grid Root, Image Cover, TrackArtworkOverlay Overlay, Button Like,
        Image Heart, TextBlock Title, TextBlock Author, TextBlock Duration, InkBorder HoverFill)
    {
        public SoundCloudTrack? Track { get; set; }
        public bool Hovered { get; set; }
        public long? EntryId { get; set; }
        public bool CollectionRow { get; init; }
        public int SourceIndex { get; set; }
        public Button? More { get; set; }
        public MenuItem? RepostMenu { get; set; }
        public Button PlaybackButton => Overlay.PlaybackButton!;
        public InkBorder Summary { get; init; } = null!;
        public InkBorder Actions { get; init; } = null!;
        public CompactTrackTail Trailing { get; init; } = null!;
        public StackPanel? ExtraActions { get; set; }
        public TextBlock Plays { get; init; } = null!;
        public Button? Repost { get; set; }
        public Image? RepostIcon { get; set; }
        public bool Miniature { get; init; }
        public Button[] ActionButtons { get; set; } = [];
        public (string? Url, string Fallback, int Side)? ArtworkRequest { get; set; }
        public long ArtworkGeneration { get; set; }
    }
    private CompactTrackRow CreateCompactTrackRow(Action<SoundCloudTrack>? select = null, Color? secondary = null, bool queueRow = false, bool collectionRow = false, Action? open = null, bool dense = false, bool miniature = false)
    {
        var coverSize = miniature ? 20 : dense ? 32 : CompactCoverSize;
        var cover = new Image().Width(coverSize).Height(coverSize).StretchMode(Stretch.UniformToFill);
        CompactTrackRow row = null!;
        var overlay = CreateCoverPlayback(cover, () => { if (row.Track is { } track) (select ?? SelectLibraryTrack)(track); },
            miniature ? 18 : 30, miniature ? 10 : 18, Color.FromArgb(170, 0, 0, 0)).Width(coverSize).Height(coverSize);
        var frame = new Border().Width(coverSize).Height(coverSize).CornerRadius(5).ClipToBounds()
            .Child(ArtworkLayer(cover, fadeLoading: queueRow).Children(overlay));
        var title = new TextBlock().FontSize(dense ? 12 : 14).SemiBold().Left().Height(dense ? 18 : 21).TextTrimming(TextTrimming.CharacterEllipsis);
        var author = new TextBlock().FontSize(dense ? 11 : 12).Foreground(secondary ?? Muted).Height(dense ? 16 : 19).TextTrimming(TextTrimming.CharacterEllipsis).IsVisible(!miniature);
        var duration = new TextBlock { TextAlignment = TextAlignment.Right }.Width(CompactTrackTail.StatisticsWidth).FontSize(12).Foreground(secondary ?? Muted).Right();
        var plays = new TextBlock().FontSize(10).Foreground(secondary ?? Muted).Right().IsVisible(!miniature);
        var statistics = new StackPanel().Vertical().Spacing(2).Right().CenterVertical().Children(duration, plays);
        var heart = Icons.View("heart", 17, MutedIcon).CenterHorizontal().CenterVertical();
        var like = TrackButtons.Icon(heart, () => { }, TrackButtons.Text, 32).CenterVertical().IsVisible(!miniature);
        if (collectionRow && OperatingSystem.IsWindows() && like is MaterialTrackButton material) material.BakeGlyphOpacity = true;
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
        row = new CompactTrackRow(root, cover, overlay, like, heart, title, author, duration, hoverFill)
        { CollectionRow = collectionRow, Summary = summary, Actions = actions, Trailing = trailing,
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
        foreach (var button in new[] { action, row.PlaybackButton, like }) TrackCompactActionFocus(row, button);
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
        var sameTrack = track is { Id: > 0 } && row.Track?.Id == track.Id;
        var oldArtwork = row.Track?.ArtworkUrl ?? row.Track?.User?.AvatarUrl;
        if (!sameTrack && row.Root.IsFocusWithin) Window.FocusManager.ClearFocus();
        row.Track = track; row.Hovered = row.Root.IsMouseOver;
        if (!sameTrack)
        {
            row.Overlay.Reset();
            SetCompactActionReveal(row, row.Hovered && !(row.CollectionRow && playlistHoverSuspended) && track != null, animate: false);
        }
        if (track == null)
        {
            row.EntryId = null; row.Hovered = false; row.HoverFill.Strength = 0;
            ++row.ArtworkGeneration; row.ArtworkRequest = null;
            ClearCardArtwork(row.Cover);
            return;
        }
        row.Title.Text = track.Title; row.Author.Text = track.Author; row.Duration.Text = FormatTime(track.DurationSeconds);
        row.Plays.Text = track.PlaybackCount is { } count ? LikedTrackRow.ShortCount(count) : "—";
        row.Plays.IsVisible = !row.Miniature && track.Id > 0;
        row.Like.IsVisible = !row.Miniature && track.Id > 0;
        row.Actions.IsVisible = !row.Miniature && track.Id > 0;
        var artwork = track.ArtworkUrl ?? track.User?.AvatarUrl;
        if (!sameTrack || artwork != oldArtwork || row.Cover.Source == null && loadArtwork)
        {
            ++row.ArtworkGeneration; row.ArtworkRequest = null;
            if (loadArtwork) Run(() => LoadCompactTrackArtworkAsync(row, artwork));
            else SetCardArtwork(row.Cover, null, artwork);
        }
        RefreshCompactTrackRow(row);
    }

    private async Task<ImageSource?> LoadCompactTrackArtworkAsync(CompactTrackRow row, string? url,
        string fallback = Icons.MissingTrackArtwork)
    {
        if (disposed || row.Track == null) return null;
        var side = CompactArtworkSide(row.Cover.Width);
        var current = row.ArtworkRequest is { } previous && previous.Url == url ? row.Cover.Source as ImageSource : null;
        row.ArtworkRequest = (url, fallback, side);
        var generation = ++row.ArtworkGeneration;
        var cached = libraryCoverCache.GetValueOrDefault(row.Track.Id) ??
            (url == null ? current : collectionImages.GetValueOrDefault(url));
        var request = CompactArtworkAsync(url, side, cached);
        if (!request.IsCompletedSuccessfully && current == null)
            SetCardArtwork(row.Cover, null, url, fallback);
        var source = await request;
        if (disposed || generation != row.ArtworkGeneration) return null;
        SetCardArtwork(row.Cover, source ?? current, url, fallback, finished: true);
        return source ?? current;
    }

    private void RefreshCompactArtworkDpi()
    {
        foreach (var row in compactTrackRows)
            if (row.ArtworkRequest is { } request && request.Side != CompactArtworkSide(row.Cover.Width))
                Run(() => LoadCompactTrackArtworkAsync(row, request.Url, request.Fallback));
    }

    private void RefreshCompactTrackRow(CompactTrackRow row)
    {
        if (row.Track is not { } track) return;
        var scrolling = row.CollectionRow && playlistHoverSuspended;
        var hovered = row.Hovered && !scrolling;
        var showActions = hovered || row.Root.IsFocusWithin && Window.FocusManager.FocusedElement is Control focused && KeyboardFocusFeedback.IsVisible(focused);
        if (!showActions && row.Actions.IsFocusWithin) Window.FocusManager.ClearFocus();
        if (showActions && !row.Miniature && track.Id > 0) EnsureCompactTrackActions(row);
        if (row.RepostMenu != null) row.RepostMenu.Text = repostedIds.Contains(track.Id) ? "Убрать репост" : "Сделать репост";
        var selected = row.EntryId is { } entryId ? playbackQueue.Current?.EntryId == entryId :
            row.CollectionRow && playbackQueue.Context?.Key == displayedCollection?.Key
                ? current?.Id == track.Id && playbackQueue.Current?.SourceIndex == row.SourceIndex : current?.Id == track.Id;
        row.HoverFill.Strength = selected || hovered ? 1 : 0;
        row.Overlay.SetHovered(hovered, animate: !scrolling);
        row.Overlay.SetPlaying(selected, isPlaying.Value, animate: !scrolling);
        var liked = IsTrackLiked(track);
        TrackButtons.SetGlyph(row.Heart, liked ? "heart-filled" : "heart", liked ? LikedHeart : hovered ? Color.White : MutedIcon);
        TrackButtons.SetAvailability(row.Like, CanLikeTrack(track), IsLikePending(track));
        row.Like.Hint(liked ? "Убрать из понравившегося" : "Добавить в понравившееся");
        if (row.Repost is { } repost && row.RepostIcon is { } repostIcon)
        {
            var reposted = repostedIds.Contains(track.Id);
            TrackButtons.SetGlyph(repostIcon, "repeat", reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface);
            TrackButtons.SetAvailability(repost, CanRepostTrack(track), IsRepostPending(track));
            repost.Hint(reposted ? "Убрать репост" : "Сделать репост");
        }
        SetCompactActionReveal(row, showActions, animate: !scrolling);
    }

    private static void SetCompactActionReveal(CompactTrackRow row, bool show, bool animate = true)
    {
        show &= !row.Miniature && row.Track is { Id: > 0 };
        row.Trailing.SetExpanded(show, animate);
        foreach (var button in row.ActionButtons) button.IsTabStop = show;
    }

}
