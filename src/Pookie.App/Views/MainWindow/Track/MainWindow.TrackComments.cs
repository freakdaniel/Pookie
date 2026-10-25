using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Grid trackDetailWaveformLayer = null!;
    private CommentMarkerLayer trackDetailMarkerLayer = null!;
    private sealed record TrackCommentMarker(Button Button, Image Avatar, double Fraction, SoundCloudComment Comment)
    { internal long AvatarGeneration = -1; }
    private readonly List<TrackCommentMarker> trackCommentMarkers = [];
    private long trackCommentMarkerTrack;
    private WaveCommentPreview trackWaveCommentPreview = null!;
    private DispatcherTimer trackCommentPreviewTimer = null!;
    private TrackCommentMarker? trackHoveredComment, trackFocusedComment, trackAutoComment;
    private double? previousTrackCommentPosition;
    private readonly HashSet<(long Track, long Comment, long Generation)> pendingTrackReplies = [];

    private FrameworkElement TrackCommentView(SoundCloudComment comment, SoundCloudTrack track, bool reply = false)
    {
        var size = reply ? 28 : 44;
        var avatar = new Image { VectorCacheMode = ImageVectorCacheMode.Direct }.Width(size).Height(size).StretchMode(Stretch.UniformToFill);
        avatar.Source = Icons.Source("user", Muted);
        var name = new CommentTextBlock().Text(comment.User?.Username ?? "Пользователь SoundCloud").FontSize(14).Bold().CenterVertical()
            .MaxWidth(200).TextTrimming(TextTrimming.CharacterEllipsis);
        AttachUserLink(name, () => comment.User); AttachUserLink(avatar, () => comment.User);
        var top = new StackPanel().Horizontal().Spacing(6).Children(name);
        if (ValidCommentTime(comment, track) is { } timestamp)
        {
            top.Add(new CommentTextBlock().Text("на").FontSize(12).Foreground(Muted).CenterVertical());
            var time = TrackButtons.Action(new CommentTextBlock().Text(FormatTime(timestamp / 1000)).FontSize(12).Bold()
                .Foreground(TrackButtons.OnCommentTime).IsHitTestVisible(false), () => Run(() => SeekLikedTrackAsync(track, timestamp / track.Duration)),
                TrackButtons.CommentTime, 24).Padding(6, 0);
            top.Add(time);
        }
        var age = DetailAge(comment.CreatedAt);
        if (age.Length > 0) top.Add(new CommentTextBlock().Text("· " + age).FontSize(12).Foreground(Muted).CenterVertical());
        var more = TrackButtons.Icon(Icons.View("dots-three", 20).Center(), () => { }, TrackButtons.Text, 28);
        var menu = new ContextMenu();
        var copy = new Command("pookie.comment.copy", "Скопировать текст");
        more.Commands.Register(copy, () => CopyText(comment.Body));
        menu.Item(copy);
        var open = new Command("pookie.comment.open", "Открыть в SoundCloud");
        more.Commands.Register(open, () => OpenSoundCloudPage(track.PermalinkUrl)); menu.Item(open);
        if (me is { } viewer && comment.User?.Id == viewer.Id)
        {
            var delete = new Command("pookie.comment.delete", "Удалить");
            more.Commands.Register(delete, () => Run(() => ConfirmDeleteTrackCommentAsync(track, comment)),
                () => CanDeleteTrackComment(track.Id, comment));
            menu.Item(delete);
        }
        more.OnClick(() => menu.Show(more)); more.ContextMenu = menu;
        var actions = new StackPanel().Horizontal().Spacing(12).Children(
            TrackButtons.Action(new CommentTextBlock().Text("Ответить").FontSize(13).SemiBold(),
                () => OpenSoundCloudPage(track.PermalinkUrl), TrackButtons.Text, 28).Padding(0), more);
        var text = new StackPanel().Vertical().Spacing(4).Column(1).Children(top,
            new CommentTextBlock().Text(comment.Body).FontSize(14).FontWeight(FontWeight.Normal).TextWrapping(TextWrapping.Wrap), actions);
        if (!reply)
        {
            var replies = new StackPanel().Vertical().Spacing(14).Margin(0, 10, 0, 0);
            foreach (var item in comment.Replies) replies.Add(TrackCommentView(item, track, reply: true));
            if (comment.RepliesCursor != null)
            {
                var moreReplies = TrackButtons.Action(new TextBlock().Text("Ещё ответы").FontSize(13).SemiBold(), () => { }, TrackButtons.Text, 28).Left();
                moreReplies.IsEnabled = !pendingTrackReplies.Contains((track.Id, comment.Id, navigationGeneration));
                moreReplies.OnClick(() => Run(async () =>
                {
                    moreReplies.IsEnabled = false;
                    try { await MoreTrackRepliesAsync(track, comment); }
                    finally { moreReplies.IsEnabled = true; }
                }));
                replies.Add(moreReplies);
            }
            if (replies.Children.Count > 0) text.Add(replies);
        }
        var root = new Grid().Columns($"{size},*").Rows("Auto").Spacing(reply ? 8 : 12).Children(
            new Border().CornerRadius(size / 2d).ClipToBounds().Child(avatar).Column(0).Top(), text);
        LoadTrackDetailAvatar(avatar, comment.User?.AvatarUrl, track.Id);
        return root;
    }

    private async Task MoreTrackRepliesAsync(SoundCloudTrack track, SoundCloudComment comment)
    {
        var generation = navigationGeneration;
        if (comment.RepliesCursor == null || !pendingTrackReplies.Add((track.Id, comment.Id, generation))) return;
        var token = loading!.Token;
        try
        {
            var result = await api.GetTrackRepliesAsync(track.Id, comment.Id, comment.RepliesCursor, token);
            if (!IsTrackDetailCurrent(track.Id, generation)) return;
            var comments = trackDetailState!.Comments.Comments.Select(item => item.Id != comment.Id ? item : item with {
                Replies = item.Replies.Concat(result.Comments).DistinctBy(r => r.Id).ToArray(), ReplyCount = result.Total,
                RepliesCursor = result.Cursor == comment.RepliesCursor ? null : result.Cursor }).ToArray();
            trackDetailState = trackDetailState with { Comments = trackDetailState.Comments with { Comments = comments } };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || lifetime.IsCancellationRequested) { return; }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        { if (IsTrackDetailCurrent(track.Id, generation)) status.Value = FriendlyError(error); }
        finally
        {
            pendingTrackReplies.Remove((track.Id, comment.Id, generation));
            if (IsTrackDetailCurrent(track.Id, generation)) RenderTrackDetailComments();
        }
    }

    private static double? ValidCommentTime(SoundCloudComment comment, SoundCloudTrack track) =>
        comment.Timestamp is { } time && double.IsFinite(time) && time >= 0 && time < track.Duration ? time : null;

    private void LoadTrackDetailAvatar(Image image, string? url, long trackId, bool marker = false)
    {
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await SharedArtworkAsync(url);
            if (!IsTrackDetailCurrent(trackId, generation)) return;
            if (source != null) image.Source = source;
            if (marker) { image.Opacity = 1; trackDetailMarkerLayer.Interact(); }
            trackDetailMarkerLayer.RefreshSnapshot();
        });
    }

    private void RenderTrackCommentMarkers()
    {
        if (trackDetailState is not { } state) return;
        if (trackCommentMarkerTrack != state.Track.Id || !preferences.ShowCommentMarkers)
        {
            ResetTrackCommentPreview(); trackDetailMarkerLayer.ResetReveal();
            trackDetailMarkerLayer.Clear(); trackCommentMarkers.Clear(); trackCommentMarkerTrack = state.Track.Id;
        }
        if (!preferences.ShowCommentMarkers) return;
        var timed = state.Comments.Comments.Where(c => ValidCommentTime(c, state.Track) != null).Take(50).ToArray();
        foreach (var old in trackCommentMarkers.Where(m => !timed.Any(c => c.Id == m.Comment.Id && c.Timestamp == m.Comment.Timestamp &&
            c.Body == m.Comment.Body && c.User == m.Comment.User && c.Timestamp / state.Track.Duration == m.Fraction)).ToArray())
        { trackDetailMarkerLayer.Remove(old.Button); old.Avatar.Source = null; trackCommentMarkers.Remove(old); }
        foreach (var comment in timed)
        {
            if (trackCommentMarkers.FirstOrDefault(m => m.Comment.Id == comment.Id) is { } existing)
            { LoadTrackCommentMarkerAvatar(existing, state.Track.Id); continue; }
            var fraction = comment.Timestamp!.Value / state.Track.Duration;
            var image = new Image { VectorCacheMode = ImageVectorCacheMode.Direct, Opacity = 0 }.Width(24).Height(24).StretchMode(Stretch.UniformToFill);
            image.Transitions = [Transition.Create(UIElement.OpacityProperty, 220, Easing.CubicBezier(.2, 0, 0, 1))];
            image.Source = Icons.Source("user", Muted);
            var marker = TrackButtons.Icon(new Border().Background(Color.FromRgb(51, 53, 59)).CornerRadius(12).ClipToBounds().Child(image),
                () => Run(() => SeekLikedTrackAsync(state.Track, fraction)), TrackButtons.Text, 24).Left().Top();
            var slot = new TrackCommentMarker(marker, image, fraction, comment);
            // The cached strip must include complete avatars even when it is
            // captured while their screen positions are partly above the window.
            VisualTree.Visit(marker, element => { if (element is UIElement visual) visual.SkipViewportCull = true; });
            marker.MouseEnter += () => { trackDetailMarkerLayer.Interact(); trackHoveredComment = slot; RefreshTrackCommentPreview(); };
            marker.MouseLeave += () => { trackDetailMarkerLayer.Interact(); if (trackHoveredComment == slot) trackHoveredComment = null; RefreshTrackCommentPreview(); };
            marker.GotFocus += () => { trackFocusedComment = slot; RefreshTrackCommentPreview(); };
            marker.LostFocus += () => { if (trackFocusedComment == slot) trackFocusedComment = null; RefreshTrackCommentPreview(); };
            trackCommentMarkers.Add(slot); trackDetailMarkerLayer.Add(marker);
            LoadTrackCommentMarkerAvatar(slot, state.Track.Id);
        }
        if (trackCommentMarkers.Count > 0) trackDetailMarkerLayer.RevealMarkers(); else trackDetailMarkerLayer.ResetReveal();
        RefreshTrackCommentPreview();
        PositionTrackCommentMarkers();
    }

    private void LoadTrackCommentMarkerAvatar(TrackCommentMarker marker, long trackId)
    {
        if (marker.Avatar.Opacity > .999 || marker.AvatarGeneration == navigationGeneration) return;
        marker.AvatarGeneration = navigationGeneration;
        LoadTrackDetailAvatar(marker.Avatar, marker.Comment.User?.AvatarUrl, trackId, marker: true);
    }

    private void PositionTrackCommentMarkers()
    {
        var width = trackDetailWaveformLayer.ActualWidth;
        foreach (var marker in trackCommentMarkers)
            marker.Button.Margin = new Thickness(Math.Clamp(marker.Fraction * width - 12, 0, Math.Max(0, width - 24)), 103, 0, 0);
        trackDetailMarkerLayer.RefreshSnapshot();
    }

    private void RefreshTrackCommentPreview()
    {
        if (trackHoveredComment != null && !trackCommentMarkers.Contains(trackHoveredComment)) trackHoveredComment = null;
        if (trackFocusedComment != null && !trackCommentMarkers.Contains(trackFocusedComment)) trackFocusedComment = null;
        if (trackAutoComment != null && !trackCommentMarkers.Contains(trackAutoComment)) trackAutoComment = null;
        var marker = trackHoveredComment ?? trackFocusedComment ?? trackAutoComment;
        trackWaveCommentPreview.Show(marker?.Comment, marker?.Fraction ?? 0);
    }

    private void AdvanceTrackCommentPreview(double seconds, bool playing)
    {
        if (!preferences.ShowCommentMarkers || current?.Id != trackDetailState?.Track.Id ||
            trackDetailWaveform.Bounds.Bottom <= trackDetailScroll.Bounds.Y || trackDetailWaveform.Bounds.Y >= trackDetailScroll.Bounds.Bottom)
        {
            previousTrackCommentPosition = null; trackAutoComment = null; trackCommentPreviewTimer.Stop(); RefreshTrackCommentPreview(); return;
        }
        if (!playing) { previousTrackCommentPosition = null; return; }
        // Ordinary ticks cross a small interval. A seek/resume only considers
        // the destination, never bursts through every comment it skipped.
        var continuous = previousTrackCommentPosition is { } previous && seconds >= previous && seconds - previous <= 1.5;
        var start = continuous ? previousTrackCommentPosition!.Value : Math.Max(-.001, seconds - .45);
        previousTrackCommentPosition = seconds;
        if (!continuous) { trackAutoComment = null; trackCommentPreviewTimer.Stop(); }
        TrackCommentMarker? crossed = null;
        foreach (var marker in trackCommentMarkers)
        {
            var at = marker.Comment.Timestamp!.Value / 1000;
            if (at > start && at <= seconds && (crossed == null || at > crossed.Comment.Timestamp!.Value / 1000)) crossed = marker;
        }
        // Keep one readable automatic card for its display interval. Comments
        // crossed meanwhile are skipped, not queued; hover remains unrestricted.
        if (crossed == null || continuous && trackAutoComment != null)
        { if (!continuous) RefreshTrackCommentPreview(); return; }
        trackAutoComment = crossed; trackCommentPreviewTimer.Stop(); trackCommentPreviewTimer.Start(); RefreshTrackCommentPreview();
    }

    private void ResetTrackCommentPreview()
    {
        trackCommentPreviewTimer?.Stop(); previousTrackCommentPosition = null;
        trackHoveredComment = trackFocusedComment = trackAutoComment = null;
        trackWaveCommentPreview?.Reset();
    }

    private void OpenSoundCloudPage(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host is not ("soundcloud.com" or "www.soundcloud.com") || uri.UserInfo != "") return;
        OpenExternalUrl(uri.AbsoluteUri, () => status.Value = "Не удалось открыть SoundCloud");
    }

    private static string DetailAge(string? value)
    {
        if (!DateTimeOffset.TryParse(value, out var date)) return "";
        var elapsed = DateTimeOffset.UtcNow - date;
        if (elapsed.TotalMinutes < 1) return "только что";
        if (elapsed.TotalHours < 1) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} мин. назад";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours} ч. назад";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays} дн. назад";
        if (elapsed.TotalDays < 365) return $"{(int)(elapsed.TotalDays / 30)} мес. назад";
        return $"{(int)(elapsed.TotalDays / 365)} г. назад";
    }
}
