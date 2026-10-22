using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBox trackCommentInput = null!;
    private Image trackCommentOwnAvatar = null!;
    private Button trackCommentSend = null!;
    private TextBlock trackCommentSortLabel = null!;
    private TextBlock trackCommentDraftTime = null!;
    private ExpandedGlassSurface trackCommentComposer = null!;
    private TrackCommentActionRow trackCommentActions = null!;
    private long trackCommentComposerTrackId;
    private long trackCommentsRevision, trackCommentCountRevision, trackCommentAvatarUser;
    private bool renderingCommentDraft;
    private readonly HashSet<(WebSession Session, long Track)> pendingCommentPosts = [];
    private readonly HashSet<(WebSession Session, long Comment)> pendingCommentDeletes = [];

    private FrameworkElement TrackCommentComposerView()
    {
        trackCommentOwnAvatar = new Image().Width(28).Height(28).StretchMode(Stretch.UniformToFill).Source(Icons.Source("user", Muted));
        trackCommentInput = new TextBox().Background(Color.Transparent).BorderThickness(0).Padding(0).FontSize(13)
            .Placeholder("Твой комментарий").CenterVertical().OnTextChanged(text =>
            {
                if (renderingCommentDraft || trackDetailState is not { } state) return;
                trackDetailState = state with { CommentDraft = text };
                RefreshCommentSendAvailability();
            }).OnKeyDown(e =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Run(PostTrackCommentAsync); }
            });
        trackCommentInput.MaxLength = CommentWriteRequest.MaxBodyLength;
        trackCommentSend = TrackButtons.Icon(Icons.View("paper-plane-tilt", 20).Center(),
            () => Run(PostTrackCommentAsync), TrackButtons.Text, 32).CenterVertical();
        trackCommentDraftTime = new TextBlock().FontSize(11).Foreground(Muted).CenterVertical().IsVisible(false);
        trackCommentComposer = new ExpandedGlassSurface().CornerRadius(22).Height(44).Padding(8, 4).Background(TrackButtons.GlassSurface)
            .Content(new Grid().Columns("28,*,Auto,32").Rows("*").Spacing(8).Children(
                new Border().CornerRadius(14).ClipToBounds().Child(trackCommentOwnAvatar).CenterVertical().Column(0),
                trackCommentInput.Column(1), trackCommentDraftTime.Column(2), trackCommentSend.Column(3)));
        trackCommentComposer.Transitions = [Transition.Create(Control.BackgroundProperty, 150)];
        void Surface() => trackCommentComposer.Background = trackCommentComposer.IsMouseOver || trackCommentComposer.IsFocusWithin
            ? TrackButtons.HoverColor(TrackButtons.GlassSurface, TrackButtons.OnSurface) : TrackButtons.GlassSurface;
        trackCommentComposer.MouseEnter += Surface; trackCommentComposer.MouseLeave += Surface;
        trackCommentInput.GotFocus += () =>
        {
            if (trackDetailState is { CommentTimestamp: null } state)
                trackDetailState = state with { CommentTimestamp = CurrentCommentTimestamp(state.Track) };
            trackCommentActions.SetExpanded(true);
            Surface();
        };
        void LeaveComposer()
        {
            if (!trackCommentComposer.IsFocusWithin) trackCommentActions.SetExpanded(false);
            Surface();
        }
        trackCommentInput.LostFocus += LeaveComposer;
        trackCommentSend.GotFocus += () => { trackCommentActions.SetExpanded(true); Surface(); };
        trackCommentSend.LostFocus += LeaveComposer;
        trackCommentComposer.MouseDown += args =>
        {
            if (args.Button == MouseButton.Left && trackCommentInput.IsEnabled && !trackCommentSend.IsMouseOver)
                trackCommentInput.Focus();
        };
        return trackCommentComposer;
    }

    private bool CancelTrackCommentInput()
    {
        if (page.Value != Page.Track || !trackCommentActions.Expanded) return false;
        if (trackCommentComposer.IsFocusWithin) Window.FocusManager.ClearFocus();
        trackCommentActions.SetExpanded(false);
        return true;
    }

    private FrameworkElement TrackCommentSortView()
    {
        trackCommentSortLabel = new TextBlock().FontSize(13).Foreground(Muted).CenterVertical();
        var button = TrackButtons.Action(trackCommentSortLabel, () => { }, TrackButtons.Text, 32).Padding(12, 0);
        var menu = new ContextMenu();
        foreach (var sort in new[] { "NEWEST", "OLDEST", "TIMESTAMP" })
        {
            var command = new Command("pookie.comments.sort." + sort, CommentSortLabel(sort));
            button.Commands.Register(command, () => Run(() => ChangeTrackCommentSortAsync(sort)), () => trackDetailState != null);
            menu.Item(command);
        }
        button.ContextMenu = menu; button.OnClick(() => menu.Show(button));
        return button;
    }

    private static string CommentSortLabel(string sort) => sort switch
    { "OLDEST" => "Сначала старые", "TIMESTAMP" => "По времени в треке", _ => "Сначала новые" };

    private async Task ChangeTrackCommentSortAsync(string sort)
    {
        if (sort is not ("NEWEST" or "OLDEST" or "TIMESTAMP") || trackDetailState is not { } state || state.CommentSort == sort) return;
        ++trackCommentsRevision;
        trackDetailState = state with { CommentSort = sort, Comments = new([], null), CommentsLoading = true, CommentsError = "" };
        RenderTrackDetailComments();
        await LoadTrackDetailCommentsAsync(state.Track.Id, navigationGeneration, loading!.Token);
    }

    private double CurrentCommentTimestamp(SoundCloudTrack track) => current?.Id == track.Id
        ? Math.Clamp(progress.Value * 1000, 0, Math.Max(0, track.Duration - 1)) : 0;

    private bool CanPostTrackComment => !demo && me != null && api.Session is { } session && trackDetailState is { } state &&
        state.Track.Commentable != false && !pendingCommentPosts.Contains((session, state.Track.Id));

    private void RefreshCommentSendAvailability()
    {
        trackCommentSend.IsEnabled = CanPostTrackComment && !string.IsNullOrWhiteSpace(trackCommentInput.Text);
        trackCommentInput.IsEnabled = CanPostTrackComment;
        trackCommentDraftTime.IsVisible = trackCommentInput.Text.Length > 0;
        trackCommentDraftTime.Text = FormatTime((trackDetailState?.CommentTimestamp ?? 0) / 1000);
    }

    private void RenderTrackCommentComposer()
    {
        if (trackDetailState is not { } state) return;
        if (trackCommentComposerTrackId != state.Track.Id)
        {
            trackCommentComposerTrackId = state.Track.Id;
            if (trackCommentComposer.IsFocusWithin) Window.FocusManager.ClearFocus();
            trackCommentActions.SetExpanded(false, animate: false);
        }
        renderingCommentDraft = true;
        try { trackCommentInput.Text = state.CommentDraft; }
        finally { renderingCommentDraft = false; }
        trackCommentInput.Placeholder = state.Track.Commentable == false ? "Комментарии отключены" : me == null || api.Session == null
            ? "Войди, чтобы оставить комментарий" : "Твой комментарий";
        RefreshCommentSendAvailability();
        var userId = me?.Id ?? 0;
        if (trackCommentAvatarUser == userId) return;
        trackCommentAvatarUser = userId; trackCommentOwnAvatar.Source = Icons.Source("user", Muted);
        if (me?.AvatarUrl is not { } url) return;
        var session = api.Session;
        Run(async () =>
        {
            var image = await SharedArtworkAsync(url);
            if (!disposed && me?.Id == userId && api.Session == session && image != null) trackCommentOwnAvatar.Source = image;
        });
    }

    private async Task PostTrackCommentAsync()
    {
        if (!CanPostTrackComment || trackDetailState is not { } state || api.Session is not { } session) return;
        var body = state.CommentDraft.Trim();
        if (body.Length == 0 || !pendingCommentPosts.Add((session, state.Track.Id))) return;
        var generation = navigationGeneration;
        // A navigation read can be cancelled independently. Cancelling a write
        // terminates the shared browser worker, including current playback.
        var token = lifetime.Token;
        RefreshCommentSendAvailability();
        try
        {
            var comment = await api.CreateTrackCommentAsync(state.Track.Id, body, state.CommentTimestamp ?? CurrentCommentTimestamp(state.Track), token);
            if (disposed || api.Session != session) return;
            if (CommitTrackCommentState(state.Track.Id, latest => AddPostedComment(latest, comment, state.CommentDraft)))
                status.Value = "Комментарий отправлен";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || api.Session != session) { }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException or NotSupportedException or ArgumentException)
        { if (api.Session == session && IsTrackDetailCurrent(state.Track.Id, generation)) status.Value = FriendlyError(error); }
        finally
        {
            pendingCommentPosts.Remove((session, state.Track.Id));
            if (api.Session == session && page.Value == Page.Track && trackDetailState?.Track.Id == state.Track.Id) RenderTrackCommentComposer();
        }
    }

    private static TrackDetailState AddPostedComment(TrackDetailState state, SoundCloudComment comment, string submittedDraft)
    {
        var known = state.Comments.Comments.Any(c => c.Id == comment.Id || c.Replies.Any(r => r.Id == comment.Id));
        IEnumerable<SoundCloudComment> comments = known ? state.Comments.Comments : state.Comments.Comments.Prepend(comment);
        comments = state.CommentSort switch
        {
            "OLDEST" => comments.OrderBy(c => c.CreatedAt, StringComparer.Ordinal).ThenBy(c => c.Id),
            "TIMESTAMP" => comments.OrderBy(c => c.Timestamp ?? double.MaxValue).ThenBy(c => c.CreatedAt, StringComparer.Ordinal),
            _ => comments.OrderByDescending(c => c.CreatedAt, StringComparer.Ordinal).ThenByDescending(c => c.Id)
        };
        return state with { CommentDraft = state.CommentDraft == submittedDraft ? "" : state.CommentDraft,
            CommentTimestamp = state.CommentDraft == submittedDraft ? null : state.CommentTimestamp, CommentsLoading = false, CommentsError = "",
            Comments = state.Comments with { Comments = comments.ToArray() },
            Track = state.Track with { CommentCount = (state.Track.CommentCount ?? state.Comments.Comments.Length) + (known ? 0 : 1) } };
    }

    private bool CommitTrackCommentState(long trackId, Func<TrackDetailState, TrackDetailState> update)
    {
        foreach (var entry in navigationHistory)
            if (entry.Snapshot is { Detail: { } detail } snapshot && detail.Track.Id == trackId)
                entry.Snapshot = snapshot with { Detail = update(detail) };
        if (page.Value != Page.Track || trackDetailState?.Track.Id != trackId) return false;
        ++trackCommentsRevision; ++trackCommentCountRevision;
        trackDetailState = update(trackDetailState);
        RenderTrackDetailHeader(); RenderTrackDetailComments();
        return true;
    }

    private bool CanDeleteTrackComment(long trackId, SoundCloudComment comment) => api.Session is { } session && !demo &&
        trackDetailState?.Track.Id == trackId && me is { } viewer && comment.User?.Id == viewer.Id &&
        !pendingCommentDeletes.Contains((session, comment.Id));

    private async Task ConfirmDeleteTrackCommentAsync(SoundCloudTrack track, SoundCloudComment comment)
    {
        if (!CanDeleteTrackComment(track.Id, comment)) return;
        var generation = navigationGeneration; var session = api.Session;
        if (await MessageBox.AskYesNoAsync("Удалить этот комментарий?", detail: comment.Body, owner: Window) &&
            api.Session == session && IsTrackDetailCurrent(track.Id, generation)) await DeleteTrackCommentAsync(track, comment);
    }

    private async Task DeleteTrackCommentAsync(SoundCloudTrack track, SoundCloudComment comment)
    {
        if (!CanDeleteTrackComment(track.Id, comment) || api.Session is not { } session || me is not { } viewer ||
            !pendingCommentDeletes.Add((session, comment.Id))) return;
        var generation = navigationGeneration; var token = lifetime.Token;
        try
        {
            await api.DeleteTrackCommentAsync(track.Id, comment, viewer.Id, token);
            if (disposed || api.Session != session) return;
            if (CommitTrackCommentState(track.Id, state =>
            {
                var comments = state.Comments.Comments.SelectMany(c => c.Id != comment.Id
                ? new[] { c with { Replies = c.Replies.Where(r => r.Id != comment.Id).ToArray(),
                    ReplyCount = Math.Max(0, c.ReplyCount - (c.Replies.Any(r => r.Id == comment.Id) ? 1 : 0)) } }
                : c.Replies.Length == 0 ? [] : new[] { c.Replies[0] with { Replies = c.Replies.Skip(1).ToArray(),
                    ReplyCount = Math.Max(0, c.ReplyCount - 1), RepliesCursor = c.RepliesCursor } }).ToArray();
                return state with { Comments = state.Comments with { Comments = comments }, CommentsLoading = false,
                    Track = state.Track with { CommentCount = Math.Max(0, (state.Track.CommentCount ?? state.Comments.Comments.Length) - 1) } };
            })) status.Value = "Комментарий удалён";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || api.Session != session) { }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        { if (api.Session == session && IsTrackDetailCurrent(track.Id, generation)) status.Value = FriendlyError(error); }
        finally { pendingCommentDeletes.Remove((session, comment.Id)); }
    }
}
