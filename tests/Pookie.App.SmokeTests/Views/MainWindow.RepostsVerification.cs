using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyRepostsUiAsync(LikesEditTransport transport, SoundCloudTrack[] fixtures)
    {
        var first = fixtures[3]; var other = fixtures[4];
        var viewer = (ScrollViewer)likedList.FindVisualChild<ScrollViewer>()!;
        viewer.SetScrollOffsets(0, 0);
        await WaitForLikedLayoutAsync(() => likedRows.Values.Any(row => row.Track?.Id == first.Id));
        Button Action(long id)
        {
            var row = likedRows.Values.First(row => row.Track?.Id == id);
            var buttons = new List<Button>();
            VisualTree.Visit(row.Root, element => {
                if (element is Button button && button.StyleName is TrackButtons.Tonal or TrackButtons.Reposted && button.Content is Image)
                    buttons.Add(button);
            });
            return buttons[0];
        }
        if (!repostedIds.Contains(first.Id) || Action(first.Id).StyleName != TrackButtons.Reposted)
            throw new InvalidOperationException("Existing repost did not appear selected.");
        var remove = ToggleTrackRepostAsync(first);
        if (Action(first.Id).IsEnabled || !Action(other.Id).IsEnabled || transport.LastReposted)
            throw new InvalidOperationException("Pending repost disabled other rows or sent the wrong state.");
        if (Action(first.Id).Opacity != 1) throw new InvalidOperationException("Pending repost flashed a faded disabled state.");
        await ToggleTrackRepostAsync(first);
        if (transport.RepostRequests != 1) throw new InvalidOperationException("Duplicate repost sent.");
        transport.Complete(first.Id); await remove;
        if (repostedIds.Contains(first.Id) || Action(first.Id).StyleName != TrackButtons.Tonal || !Action(first.Id).IsEnabled)
            throw new InvalidOperationException("Repost removal failed to update the row.");
        var add = ToggleTrackRepostAsync(first);
        if (!transport.LastReposted) throw new InvalidOperationException("Add repost sent DELETE.");
        transport.Complete(first.Id); await add;
        if (!repostedIds.Contains(first.Id) || Action(first.Id).StyleName != TrackButtons.Reposted)
            throw new InvalidOperationException("Repost addition did not update the row.");
        var failed = ToggleTrackRepostAsync(other); transport.Fail(other.Id);
        try { await failed; throw new InvalidOperationException("Fixture error swallowed."); } catch (HttpRequestException) { }
        if (repostedIds.Contains(other.Id) || !Action(other.Id).IsEnabled)
            throw new InvalidOperationException("Failed repost changed selection or stayed disabled.");
        if (CanRepostTrack(first with { User = me })) throw new InvalidOperationException("Own track can be reposted.");
        var late = ToggleTrackRepostAsync(other);
        api.Session = new("other", "fixture", "Other"); ResetReposts();
        transport.Complete(other.Id);
        try { await late; } catch (OperationCanceledException) { }
        if (repostedIds.Count != 0) throw new InvalidOperationException("Late repost changed another session.");
        Console.WriteLine("UI_REPOSTS_OK: existing state, add/remove, per-track pending, duplicate suppression, failure recovery, own tracks and stale session protection");
    }
}
