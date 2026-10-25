using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ScrollViewer userCommentsScroll = null!;
    private StackPanel userCommentsBody = null!, userSidebarComments = null!;
    private FrameworkElement userCommentsSection = null!;
    private UserCommentPage? renderedUserComments;
    private long renderedCommentsUser, renderedUserCommentsGeneration;
    private string renderedUserCommentsStatus = "";

    private void RenderUserComments(UserProfileState state)
    {
        var pending = state.Loading.Contains(UserSection.Comments);
        var error = state.Errors.GetValueOrDefault(UserSection.Comments) ?? "";
        userCommentsSection.IsVisible = state.Comments is { Comments.Length: > 0 } || pending || error.Length > 0;
        SetUserLoading(userCommentsLoading, pending && state.Comments == null, LoadingRowStyle.Compact, 246);
        var status = error;
        if (renderedCommentsUser == state.User.Id && renderedUserCommentsGeneration == navigationGeneration &&
            ReferenceEquals(renderedUserComments, state.Comments) && renderedUserCommentsStatus == status) return;
        renderedCommentsUser = state.User.Id; renderedUserCommentsGeneration = navigationGeneration;
        renderedUserComments = state.Comments; renderedUserCommentsStatus = status;
        userSidebarComments.Clear(); userCommentsBody.Clear();
        foreach (var entry in state.Comments?.Comments.Take(3) ?? []) userSidebarComments.Add(UserCommentView(entry, preview: true));
        foreach (var entry in state.Comments?.Comments ?? []) userCommentsBody.Add(UserCommentView(entry, preview: false));
        if (error.Length > 0)
            userSidebarComments.Add(TrackButtons.Action(new TextBlock().Text("Повторить загрузку").FontSize(12),
                () => Run(() => LoadUserSectionAsync(UserSection.Comments, navigationGeneration, loading!.Token, state.Comments?.NextHref)), TrackButtons.Text, 30).Padding(0).Left());
    }

    private void ClearUserComments()
    {
        userSidebarComments.Clear(); userCommentsBody.Clear(); renderedUserComments = null;
        renderedCommentsUser = renderedUserCommentsGeneration = 0; renderedUserCommentsStatus = "";
    }

    private FrameworkElement UserCommentView(UserComment entry, bool preview)
    {
        var title = string.IsNullOrWhiteSpace(entry.Track?.Title) ? "На недоступном треке" : "На треке «" + entry.Track.Title + "»";
        var track = new TextBlock().Text(title).FontSize(preview ? 12 : 14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        if (entry.Track?.Id is > 0) AttachTrackTitle(track, () => entry.Track);
        var date = new TextBlock().Text(UserPublicationDate(entry.Comment.CreatedAt)).FontSize(10).Foreground(Muted).CenterVertical();
        var body = new CommentTextBlock().Text(entry.Comment.Body).FontSize(preview ? 13 : 14).FontWeight(FontWeight.Normal).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        if (preview) body.MaxHeight(72).TextTrimming(TextTrimming.CharacterEllipsis);
        return new StackPanel().Vertical().Spacing(6).Children(
            new Grid().Columns("*,Auto").Rows("Auto").Spacing(10).Children(track.Column(0), date.Column(1)), body);
    }
}
