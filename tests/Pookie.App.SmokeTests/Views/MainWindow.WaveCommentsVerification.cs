using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlayerLinksAndWaveCommentsAsync(SoundCloudTrack track)
    {
        timer.Stop();
        var savedTrack = current; var savedTitle = title.Value; var savedArtist = artist.Value;
        var savedVisible = playerVisible.Value; var savedState = trackDetailState!; var transport = api.BrowserTransport;
        var savedOffset = shellScroll.VerticalOffset;
        try
        {
            Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
            shellScroll.SetScrollOffsets(0, 0);
            current = track; title.Value = track.Title; artist.Value = track.Author; playerVisible.Value = true;
            await WaitForLikedLayoutAsync(() => playerTrackInfo.ActualHeight > 0 && playerArtworkButton.ActualHeight == 48 && playerChrome.Height == PlayerBarHeight);
            if (Math.Abs(playerTrackInfo.Bounds.Y + playerTrackInfo.ActualHeight / 2 - playerArtworkButton.Bounds.Y - 24) > .5 ||
                playerTrackTitle.Cursor != CursorType.Hand || playerTrackArtist.Cursor != CursorType.Hand)
                throw new InvalidOperationException("Player metadata is not centred on the cover or lost link cursors.");
            var generation = navigationGeneration; var state = trackDetailState;
            RouteWaveformClick(SettingsCenter(playerTrackTitle)); await WaitForLoginFrameAsync();
            if (generation != navigationGeneration || !ReferenceEquals(state, trackDetailState))
                throw new InvalidOperationException("Clicking the current track reloaded its page/comments.");
            foreach (var label in new[] { playerTrackTitle, playerTrackArtist })
            {
                MoveActionPointer(new(1, 1)); await WaitForLoginFrameAsync();
                var resting = label.Foreground;
                var colours = new HashSet<Color>(); void Sample() => colours.Add(label.Foreground);
                Window.FrameRendered += Sample;
                MoveActionPointer(SettingsCenter(label));
                await WaitForLikedLayoutAsync(() => label.IsMouseOver && colours.Count > 3);
                MoveActionPointer(new(1, 1));
                await WaitForLikedLayoutAsync(() => !label.IsMouseOver && label.Foreground == resting);
                Window.FrameRendered -= Sample;
            }
            CaptureUiPreview("player-track-links");
            trackDetailState = savedState with { Comments = new([], null), CommentsLoading = true };
            RenderTrackDetailComments();
            await WaitForLikedLayoutAsync(() => trackCommentsLoadingView.Skeleton.Running && trackCommentsLoadingView.Skeleton.ActualHeight > 200);
            if (trackDetailCommentsStatus.IsVisible || trackDetailComments.Opacity != 0)
                throw new InvalidOperationException("Loading comments retained a status label or unmasked rows.");
            CaptureUiPreview("track-comments-loading");
            var avatarFrames = new List<double>(); void Avatars() => avatarFrames.Add(trackDetailMarkerLayer.Reveal);
            Window.FrameRendered += Avatars;
            trackDetailState = savedState; RenderTrackDetailComments();
            await WaitForLikedLayoutAsync(() => trackDetailMarkerLayer.Reveal > .999 && !trackCommentsLoadingView.Skeleton.IsVisible);
            Window.FrameRendered -= Avatars;
            if (!avatarFrames.Any(v => v is > .01 and < .99)) throw new InvalidOperationException("Comment markers appeared without an entrance fade.");
            var first = trackCommentMarkers.Single();
            var longer = first.Comment with { Id = 88001, Timestamp = 30000, Body = "Длинный комментарий, который занимает несколько строк и меняет размер карточки 🎧" };
            var nearby = first.Comment with { Id = 88002, Timestamp = 18000, Body = "Следующий комментарий рядом по времени" };
            trackDetailState = savedState with { Comments = new([first.Comment, longer, nearby], null) }; RenderTrackDetailComments();
            await WaitForLoginFrameAsync();
            if (!ReferenceEquals(first.Button, trackCommentMarkers[0].Button))
                throw new InvalidOperationException("Appending comments rebuilt existing waveform avatars.");
            var second = trackCommentMarkers.Single(m => m.Comment.Id == longer.Id);
            var reveals = new List<double>(); var widths = new List<double>();
            void PreviewFrames() { reveals.Add(trackWaveCommentPreview.Reveal); widths.Add(trackWaveCommentPreview.CardBounds.Width); }
            Window.FrameRendered += PreviewFrames;
            MoveActionPointer(SettingsCenter(first.Button));
            await WaitForLikedLayoutAsync(() => trackWaveCommentPreview.CommentId == first.Comment.Id && !trackWaveCommentPreview.Animating);
            var shortWidth = trackWaveCommentPreview.CardBounds.Width;
            CaptureUiPreview("wave-comment-hover");
            MoveActionPointer(SettingsCenter(second.Button));
            await WaitForLikedLayoutAsync(() => trackWaveCommentPreview.CommentId == second.Comment.Id && !trackWaveCommentPreview.Animating);
            var longWidth = trackWaveCommentPreview.CardBounds.Width;
            if (longWidth <= shortWidth || !widths.Any(w => w > shortWidth + .5 && w < longWidth - .5) ||
                !reveals.Any(v => v is > .01 and < .99) || !trackDetailWaveformLayer.Bounds.Contains(trackWaveCommentPreview.CardBounds))
                throw new InvalidOperationException("Wave comment preview snapped its appearance/size or escaped the waveform.");
            CaptureUiPreview("wave-comment-resized");
            MoveActionPointer(new(1, 1)); Window.FocusManager.ClearFocus();
            await WaitForLikedLayoutAsync(() => !trackWaveCommentPreview.IsVisible);
            AdvanceTrackCommentPreview(14.9, true); AdvanceTrackCommentPreview(15.1, true);
            await WaitForLikedLayoutAsync(() => trackWaveCommentPreview.CommentId == first.Comment.Id && !trackWaveCommentPreview.Animating);
            AdvanceTrackCommentPreview(16.1, true); AdvanceTrackCommentPreview(17.1, true); AdvanceTrackCommentPreview(18.3, true);
            if (trackWaveCommentPreview.CommentId != first.Comment.Id || trackAutoComment?.Comment.Id != first.Comment.Id)
                throw new InvalidOperationException("Dense automatic comments interrupted the readable card instead of being skipped.");
            MoveActionPointer(SettingsCenter(first.Button));
            AdvanceTrackCommentPreview(30.05, true);
            if (trackWaveCommentPreview.CommentId != first.Comment.Id)
                throw new InvalidOperationException("A timed comment replaced the comment under the pointer.");
            MoveActionPointer(new(1, 1));
            await WaitForLikedLayoutAsync(() => trackWaveCommentPreview.CommentId == longer.Id && !trackWaveCommentPreview.Animating);
            CaptureUiPreview("wave-comment-playback");
            ResetTrackCommentPreview();
            AdvanceTrackCommentPreview(75, true);
            if (trackWaveCommentPreview.CommentId != null || trackWaveCommentPreview.Animating)
                throw new InvalidOperationException("Seeking beyond comments replayed historical comments.");
            Window.FrameRendered -= PreviewFrames;
            api.BrowserTransport = new PlayerArtistFixture(track.User!);
            RouteWaveformClick(SettingsCenter(playerTrackArtist));
            await WaitForLikedLayoutAsync(() => page.Value == Page.UserProfile && userProfileState?.User.Id == track.User!.Id && !navigationHistory[navigationIndex].Pending);
            if (trackWaveCommentPreview.IsVisible || trackWaveCommentPreview.Animating)
                throw new InvalidOperationException("Wave comment animation survived navigation to the artist.");
            api.BrowserTransport = transport;
            await MoveNavigationAsync(-1);
            Console.WriteLine("PLAYER_WAVE_COMMENTS_OK: centred metadata, animated links and artist navigation, idempotent title clicks, skeletons/avatar fades, animated preview sizing, timed cues with dense-comment skipping and hover priority");
        }
        finally
        {
            ResetTrackCommentPreview(); api.BrowserTransport = transport;
            current = savedTrack; title.Value = savedTitle; artist.Value = savedArtist; playerVisible.Value = savedVisible;
            trackDetailState = savedState; RenderTrackDetailComments();
            shellScroll.SetScrollOffsets(0, savedOffset); MoveActionPointer(new(1, 1)); timer.Start();
        }
    }

    private sealed class PlayerArtistFixture(SoundCloudUser user) : ISoundCloudBrowserTransport
    {
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(
            uri.AbsolutePath == "/users/" + user.Id ? JsonSerializer.SerializeToDocument(user, SoundCloudJson.Default.SoundCloudUser) :
            JsonDocument.Parse(uri.AbsolutePath.EndsWith("web-profiles") || uri.AbsolutePath.EndsWith("/ids") ? "[]" : "{\"collection\":[],\"next_href\":null}"));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
