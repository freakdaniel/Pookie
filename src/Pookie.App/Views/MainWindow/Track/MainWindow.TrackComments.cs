using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Grid trackDetailWaveformLayer = null!;
    private readonly List<(Button Button, double Fraction)> trackCommentMarkers = [];
    private readonly HashSet<(long Track, long Comment, long Generation)> pendingTrackReplies = [];

    private FrameworkElement TrackCommentView(SoundCloudComment comment, SoundCloudTrack track, bool reply = false)
    {
        var size = reply ? 28 : 44;
        var avatar = new Image().Width(size).Height(size).StretchMode(Stretch.UniformToFill);
        avatar.Source = Icons.Source("user", Muted);
        var name = new TextBlock().Text(comment.User?.Username ?? "Пользователь SoundCloud").FontSize(14).Bold().CenterVertical()
            .MaxWidth(200).TextTrimming(TextTrimming.CharacterEllipsis);
        var top = new StackPanel().Horizontal().Spacing(6).Children(name);
        if (ValidCommentTime(comment, track) is { } timestamp)
        {
            top.Add(new TextBlock().Text("на").FontSize(12).Foreground(Muted).CenterVertical());
            var time = TrackButtons.Action(new TextBlock().Text(FormatTime(timestamp / 1000)).FontSize(12).Bold()
                .Foreground(Color.FromRgb(110, 166, 255)), () => Run(() => SeekLikedTrackAsync(track, timestamp / track.Duration)),
                TrackButtons.Text, 24).Padding(6, 0).Background(Color.FromArgb(28, 110, 166, 255));
            top.Add(time);
        }
        var age = DetailAge(comment.CreatedAt);
        if (age.Length > 0) top.Add(new TextBlock().Text("· " + age).FontSize(12).Foreground(Muted).CenterVertical());
        var more = TrackButtons.Icon(Icons.View("dots-three", 20).Center(), () => { }, TrackButtons.Text, 28);
        var menu = new ContextMenu();
        var copy = new Command("pookie.comment.copy", "Скопировать текст");
        more.Commands.Register(copy, () => CopyText(comment.Body));
        menu.Item(copy);
        var open = new Command("pookie.comment.open", "Открыть в SoundCloud");
        more.Commands.Register(open, () => OpenSoundCloudPage(track.PermalinkUrl)); menu.Item(open);
        more.OnClick(() => menu.Show(more)); more.ContextMenu = menu;
        var actions = new StackPanel().Horizontal().Spacing(12).Children(
            TrackButtons.Action(new TextBlock().Text("Ответить").FontSize(13).SemiBold(),
                () => OpenSoundCloudPage(track.PermalinkUrl), TrackButtons.Text, 28).Padding(0), more);
        var text = new StackPanel().Vertical().Spacing(4).Column(1).Children(top,
            new TextBlock().Text(comment.Body).FontSize(14).FontWeight(FontWeight.Normal).TextWrapping(TextWrapping.Wrap), actions);
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
        catch (OperationCanceledException) when (token.IsCancellationRequested || lifetime.IsCancellationRequested) { }
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

    private void LoadTrackDetailAvatar(Image image, string? url, long trackId)
    {
        var generation = navigationGeneration;
        Run(async () =>
        {
            var source = await SharedArtworkAsync(url);
            if (source != null && IsTrackDetailCurrent(trackId, generation)) image.Source = source;
        });
    }

    private void RenderTrackCommentMarkers()
    {
        foreach (var marker in trackCommentMarkers) trackDetailWaveformLayer.Remove(marker.Button);
        trackCommentMarkers.Clear();
        if (trackDetailState is not { } state) return;
        foreach (var comment in state.Comments.Comments.Where(c => ValidCommentTime(c, state.Track) != null).Take(50))
        {
            var fraction = comment.Timestamp!.Value / state.Track.Duration;
            var image = new Image().Width(24).Height(24).StretchMode(Stretch.UniformToFill);
            image.Source = Icons.Source("user", Muted);
            var marker = TrackButtons.Icon(new Border().CornerRadius(12).ClipToBounds().Child(image),
                () => Run(() => SeekLikedTrackAsync(state.Track, fraction)), TrackButtons.Text, 24).Left().Top()
                .ToolTip($"{comment.User?.Username}: {comment.Body}");
            trackCommentMarkers.Add((marker, fraction)); trackDetailWaveformLayer.Add(marker);
            LoadTrackDetailAvatar(image, comment.User?.AvatarUrl, state.Track.Id);
        }
        PositionTrackCommentMarkers();
    }

    private void PositionTrackCommentMarkers()
    {
        var width = trackDetailWaveformLayer.ActualWidth;
        foreach (var (button, fraction) in trackCommentMarkers)
            button.Margin = new Thickness(Math.Clamp(fraction * width - 12, 0, Math.Max(0, width - 24)), 103, 0, 0);
    }

    private void OpenSoundCloudPage(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host is not ("soundcloud.com" or "www.soundcloud.com") || uri.UserInfo != "") return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { status.Value = "Не удалось открыть SoundCloud."; }
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
