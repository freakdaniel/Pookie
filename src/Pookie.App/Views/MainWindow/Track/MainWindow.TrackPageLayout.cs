using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private FrameworkElement TrackDetailPageView()
    {
        trackDetailCover = new Image().StretchMode(Stretch.UniformToFill);
        trackDetailBackdrop = new Image().StretchMode(Stretch.UniformToFill);
        trackDetailBackdrop.IsHitTestVisible = false;
        // One title line plus the metadata line always occupies the play button's 64px header.
        trackDetailTitle = new TextBlock().FontSize(28).Bold().Height(36).TextWrapping(TextWrapping.NoWrap).TextTrimming(TextTrimming.CharacterEllipsis);
        trackDetailAuthor = new TextBlock().FontSize(14).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        trackDetailMeta = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        trackDetailPlayIcon = Icons.View("play-solid", 28, TrackButtons.OnSurface).Center();
        trackDetailPlay = TrackButtons.Icon(trackDetailPlayIcon,
            () => { if (trackDetailState is { } state) SelectLibraryTrack(state.Track); }, TrackButtons.Glass, 64);
        trackDetailWaveform = new TrackWaveform().Height(146);
        trackDetailWaveform.SeekRequested += fraction => { if (trackDetailState is { } state) Run(() => SeekLikedTrackAsync(state.Track, fraction)); };
        trackDetailPosition = new TextBlock().Text("0:00").FontSize(11).Bold().Right();
        trackDetailDuration = new TextBlock().FontSize(11).Right();
        trackDetailWaveformLayer = new Grid().Columns("*").Rows("*").Height(146).Children(trackDetailWaveform);
        trackDetailWaveformLayer.SizeChanged += _ => PositionTrackCommentMarkers();
        var waveform = new Grid().Columns("*,42").Rows("Auto").Spacing(8).Children(trackDetailWaveformLayer.Column(0),
            new StackPanel().Vertical().Spacing(12).Margin(0, 91, 0, 0).Column(1).Children(trackDetailPosition, trackDetailDuration));
        var titleBlock = new Grid().Columns("64,*").Rows("Auto").Spacing(18).Children(trackDetailPlay.Column(0).Top(),
            new StackPanel().Vertical().Spacing(7).Column(1).Children(trackDetailTitle,
                new TrackMetadataLine(trackDetailAuthor, trackDetailMeta)));
        trackDetailHeart = Icons.View("heart", 18, TrackButtons.OnSurface).CenterVertical();
        trackDetailLikeCount = new TextBlock().FontSize(14).Bold().CenterVertical();
        trackDetailLike = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(trackDetailHeart, trackDetailLikeCount),
            () => { if (trackDetailState is { } state) Run(() => ToggleTrackLikeAsync(state.Track)); }, TrackButtons.Glass, height: 44);
        var comment = TrackCommentComposerView();
        var copy = TrackButtons.Icon(Icons.View("copy", 20).Center(),
            () => { if (trackDetailState is { } state) CopyTrackLink(state.Track); }, TrackButtons.Glass, size: 44).ToolTip("Скопировать ссылку");
        var queue = TrackButtons.Icon(Icons.View("queue", 20).Center(),
            () => { if (trackDetailState is { } state) EnqueueTrack(state.Track, false); }, TrackButtons.Glass, size: 44).ToolTip("Добавить в очередь");
        trackDetailRepostIcon = Icons.View("repeat", 20, TrackButtons.OnSurface);
        trackDetailRepostCount = new TextBlock().FontSize(14).Bold().CenterVertical();
        trackDetailRepost = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(trackDetailRepostIcon.CenterVertical(), trackDetailRepostCount),
            () => { if (trackDetailState is { } state) Run(() => ToggleTrackRepostAsync(state.Track)); }, TrackButtons.Glass, height: 44).ToolTip("Сделать репост");
        AttachTrackQueueMenu(queue, () => trackDetailState?.Track);
        var playlist = AddToPlaylistButton(() => trackDetailState?.Track, TrackButtons.Glass, 44);
        var actions = trackCommentActions = new TrackCommentActionRow(TrackButtons.Connected(trackDetailLike, trackDetailRepost), comment,
            new StackPanel().Horizontal().Spacing(TrackButtons.ActionSpacing).Children(playlist, copy, queue));
        var coverFrame = new Border().Width(320).Height(320).CornerRadius(10).ClipToBounds().Column(1).Child(ArtworkLayer(trackDetailCover));
        var heroColumns = new TrackPageColumns(
            new Grid().Columns("*").Rows("Auto,*,44").Children(titleBlock.Row(0), waveform.Row(1).CenterVertical(), actions.Row(2)),
            coverFrame, true, trackDetailBackdrop, new Border().Background(ArtworkBackdrop.CardScrim), new ArtworkGlassSheen()).Initialize();
        var hero = new Border().CornerRadius(20).ClipToBounds().Background(Raised).Child(heroColumns);
        trackDetailGlass = AttachCardGlass(heroColumns);
        var heroShadow = new ShadowDecorator().BlurRadius(12).OffsetY(4).ShadowColor(Color.FromArgb(60, 0, 0, 0))
            .CornerRadius(20).Margin(-12, -8, -12, -16).Child(hero);
        trackDetailError = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        trackDetailDescription = new MultiLineTextBox { IsReadOnly = true, Wrap = true, SizeToDocument = true }
            .Background(Color.Transparent).BorderThickness(0).Padding(0).FontSize(14).FontWeight(FontWeight.Normal).Foreground(Muted);
        trackDescriptionLinks = new DescriptionLinks(trackDetailDescription, uri => OpenSettingsUrl(uri.AbsoluteUri))
        { LoadIcon = demo ? null : (uri, token) => (descriptionFavicons ??= new(imageDiskCache)).LoadAsync(uri, token) };
        trackDetailLicense = new TextBlock().FontSize(12).Foreground(Muted);
        trackDetailCommentsTitle = new TextBlock().FontSize(21).Bold();
        trackDetailCommentsStatus = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        trackDetailComments = new StackPanel().Vertical().Spacing(26);
        trackDetailMoreComments = TrackButtons.Action(new TextBlock().Text("Ещё комментарии").FontSize(14).SemiBold()
            .Foreground(TrackButtons.OnSurface), () => Run(MoreTrackCommentsAsync), TrackButtons.Text).Left();
        var content = new StackPanel().Vertical().Spacing(24).Column(0).Children(trackDetailDescription, trackDetailLicense,
            new Border().Height(1).Background(Raised),
            new Grid().Columns("*,Auto").Rows("Auto").Spacing(12).Children(trackDetailCommentsTitle.Column(0).CenterVertical(), TrackCommentSortView().Column(1)),
            trackDetailCommentsStatus, trackDetailComments, trackDetailMoreComments);
        var body = new TrackPageColumns(content, TrackDetailSidebarView()).Initialize().Margin(0, 8, 0, 0);
        trackDetailOverview = body;
        var sections = TrackDetailSectionsView();
        // Extend the viewport equally for the shadow; the matching inner padding
        // keeps the page aligned with the full content width on both sides.
        return trackDetailScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).Margin(-12, 0, -12, 0)
            .Content(new StackPanel().Vertical().Spacing(24).Padding(12, 8, 12, 28)
                .Children(heroShadow, trackDetailError, sections, body, TrackDetailSectionContentView()));
    }

    private Button trackDetailRepost = null!;
    private Image trackDetailRepostIcon = null!;
    private TextBlock trackDetailRepostCount = null!;
}
