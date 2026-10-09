using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private FrameworkElement TrackDetailPageView()
    {
        trackDetailCover = new Image().StretchMode(Stretch.UniformToFill);
        trackDetailBackdrop = new Image().StretchMode(Stretch.UniformToFill);
        trackDetailBackdrop.IsHitTestVisible = false;
        trackDetailTitle = new TextBlock().FontSize(28).Bold().TextWrapping(TextWrapping.Wrap);
        trackDetailAuthor = new TextBlock().FontSize(16).Foreground(Muted);
        trackDetailMeta = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        trackDetailPlayIcon = Icons.View("play-solid", 24, Color.FromRgb(25, 25, 25)).Center();
        var play = new Button().Width(62).Height(62).Background(Color.White).BorderThickness(0).CornerRadius(31).Padding(0)
            .Content(trackDetailPlayIcon).OnClick(() => { if (trackDetailState is { } state) SelectLibraryTrack(state.Track); });
        trackDetailWaveform = new TrackWaveform().Height(100);
        trackDetailWaveform.SeekRequested += fraction => { if (trackDetailState is { } state) Run(() => SeekLikedTrackAsync(state.Track, fraction)); };
        trackDetailPosition = new TextBlock().Text("0:00").FontSize(11).Foreground(Muted);
        trackDetailDuration = new TextBlock().FontSize(11).Foreground(Muted).Right();
        var titleBlock = new Grid().Columns("62,*").Rows("Auto").Spacing(18).Children(play.Column(0).Top(),
            new StackPanel().Vertical().Spacing(9).Column(1).Children(trackDetailAuthor, trackDetailTitle, trackDetailMeta));
        var hero = new Border().Height(356).CornerRadius(14).ClipToBounds().Background(Raised).Child(
            new Grid().Columns("*").Rows("*").Children(trackDetailBackdrop,
                new Border().Background(Color.FromArgb(110, 18, 18, 18)),
                new Grid().Columns("*,300").Rows("Auto").Spacing(30).Padding(28).Children(
                    new DockPanel().LastChildFill().Column(0).Children(titleBlock.DockTop(),
                        new StackPanel().Vertical().Spacing(3).DockBottom().Children(trackDetailWaveform,
                            new Grid().Columns("*,Auto").Rows("Auto").Children(trackDetailPosition.Column(0), trackDetailDuration.Column(1))),
                        new Border().Height(24)),
                    new Border().Width(300).Height(300).CornerRadius(8).ClipToBounds().Column(1).Child(ArtworkLayer(trackDetailCover)))));
        trackDetailHeart = Icons.View("heart", 17).CenterVertical();
        trackDetailLikeCount = new TextBlock().FontSize(13).Bold().CenterVertical();
        trackDetailLike = DetailAction(new StackPanel().Horizontal().Spacing(7).Children(trackDetailHeart, trackDetailLikeCount),
            () => { if (trackDetailState is { } state) Run(() => ToggleTrackLikeAsync(state.Track)); });
        var copy = DetailAction(new StackPanel().Horizontal().Spacing(7).Children(Icons.View("copy", 17).CenterVertical(),
            new TextBlock().Text("Ссылка").FontSize(12).CenterVertical()), () => { if (trackDetailState is { } state) CopyTrackLink(state.Track); });
        var queue = DetailAction(new StackPanel().Horizontal().Spacing(7).Children(Icons.View("queue", 17).CenterVertical(),
            new TextBlock().Text("В очередь").FontSize(12).CenterVertical()), () => { if (trackDetailState is { } state) EnqueueTrack(state.Track, false); });
        trackDetailStats = new TextBlock().FontSize(11).Foreground(Muted).Right().CenterVertical().TextTrimming(TextTrimming.CharacterEllipsis);
        var actions = new Grid().Columns("Auto,*").Rows("Auto").Spacing(18).Children(
            new StackPanel().Horizontal().Spacing(8).Column(0).Children(trackDetailLike, copy, queue), trackDetailStats.Column(1));
        trackDetailError = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        trackDetailDescription = new TextBlock().FontSize(14).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        trackDetailCommentsTitle = new TextBlock().FontSize(21).Bold();
        trackDetailCommentsStatus = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        trackDetailComments = new StackPanel().Vertical().Spacing(22);
        trackDetailMoreComments = DetailAction(new TextBlock().Text("Загрузить ещё / повторить").FontSize(12), () => Run(MoreTrackCommentsAsync));
        trackDetailMoreComments.Left();
        var content = new StackPanel().Vertical().Spacing(24).Column(0).Children(trackDetailDescription,
            new Border().Height(1).Background(Raised), trackDetailCommentsTitle, trackDetailCommentsStatus, trackDetailComments, trackDetailMoreComments);
        trackDetailAvatar = new Image().Width(56).Height(56).StretchMode(Stretch.UniformToFill);
        trackDetailAuthorName = new TextBlock().FontSize(16).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        trackDetailAuthorStats = new TextBlock().FontSize(11).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        trackDetailRelatedStatus = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        var related = new StackPanel().Vertical().Spacing(4);
        for (var i = 0; i < 6; i++)
        {
            var row = CreateCompactTrackRow(); trackDetailRelatedRows.Add(row); related.Add(row.Root);
        }
        var sidebar = new StackPanel().Vertical().Spacing(22).Column(1).Children(
            new Grid().Columns("56,*").Rows("Auto").Spacing(14).Children(
                new Border().CornerRadius(28).ClipToBounds().Child(trackDetailAvatar).Column(0),
                new StackPanel().Vertical().Spacing(6).CenterVertical().Column(1).Children(trackDetailAuthorName, trackDetailAuthorStats)),
            new TextBlock().Text("Похожие треки").FontSize(18).Bold(), trackDetailRelatedStatus, related);
        return trackDetailScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(new StackPanel().Vertical().Spacing(22).Padding(0, 12, 16, 28).Children(hero, actions, trackDetailError,
                new Grid().Columns("*,320").Rows("Auto").Spacing(32).Margin(0, 8, 0, 0).Children(content, sidebar)));
    }

    private Button DetailAction(FrameworkElement content, Action action) => new Button().Background(Raised).BorderThickness(0)
        .CornerRadius(7).Padding(12, 9).Content(content).OnClick(action);

    private FrameworkElement TrackCommentView(SoundCloudComment comment, SoundCloudTrack track)
    {
        var avatar = new Image().Width(36).Height(36).StretchMode(Stretch.UniformToFill);
        avatar.Source = Icons.Source("user", Muted);
        var name = new TextBlock().Text(comment.User?.Username ?? "Пользователь SoundCloud").FontSize(12).SemiBold().CenterVertical()
            .MaxWidth(220).TextTrimming(TextTrimming.CharacterEllipsis);
        var top = new StackPanel().Horizontal().Spacing(8).Children(name);
        if (comment.Timestamp is { } timestamp && double.IsFinite(timestamp) && timestamp >= 0 && timestamp < track.Duration)
            top.Add(new Button().Background(Raised).BorderThickness(0).CornerRadius(4).Padding(5, 2)
                .Content(new TextBlock().Text(FormatTime(timestamp / 1000)).FontSize(10).Foreground(Muted))
                .OnClick(() => Run(() => SeekLikedTrackAsync(track, timestamp / track.Duration))));
        var root = new Grid().Columns("36,*").Rows("Auto").Spacing(12).Children(
            new Border().CornerRadius(18).ClipToBounds().Child(avatar).Column(0).Top(),
            new StackPanel().Vertical().Spacing(5).Column(1).Children(top,
                new TextBlock().Text(comment.Body).FontSize(13).TextWrapping(TextWrapping.Wrap),
                new TextBlock().Text(DetailDate(comment.CreatedAt)).FontSize(10).Foreground(Muted)));
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await SharedArtworkAsync(comment.User?.AvatarUrl);
            if (source != null && IsTrackDetailCurrent(track.Id, generation)) avatar.Source = source;
        });
        return root;
    }
}
