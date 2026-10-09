using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyLikesEditsUiAsync()
    {
        try
        {
            var transport = new LikesEditTransport();
            api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Likes fixture" }; signedIn.Value = true;
            await ShowWorkspaceAsync();
            likedIdsReady = true;
            var fixtures = Enumerable.Range(1, 30).Select(id => new SoundCloudTrack
            {
                Id = 81000 + id, Title = "Likes edit " + id, Duration = 120000, LikesCount = 12,
                PermalinkUrl = "https://soundcloud.com/fixture/track-" + id,
                User = new() { Username = "Fixture artist" }
            }).ToArray();
            likedIds.UnionWith(fixtures.Select(track => track.Id));
            await LoadRepostedIdsAsync();
            libraryLikes = new(fixtures, "https://api-v2.soundcloud.com/fixture-next");
            ShowLibraryTracks(); likesAsList.Value = true;
            await WaitForLikedLayoutAsync(() => likedRows.Values.Any(row => row.Track?.Id == fixtures[1].Id && row.Root.ActualHeight > 100));
            var generation = navigationGeneration;
            var cursor = nextHref;
            var viewer = (ScrollViewer)likedList.FindVisualChild<ScrollViewer>()!;
            var scroll = viewer.VerticalOffset;
            var first = likedRows.Values.Single(row => row.Track?.Id == fixtures[0].Id);
            var second = likedRows.Values.Single(row => row.Track?.Id == fixtures[1].Id);
            Button Like(LikedTrackRow row) => FindLikeAction(row.Root);
            Like(first).Focus();
            var fade = new List<double>(); var shift = new List<double>(); var appearance = new List<double>();
            void Sample()
            {
                var outgoing = likedItemMotions.FirstOrDefault(pair => pair.Value == likedList && pair.Key.TrackId == fixtures[0].Id).Key;
                var following = likedItemMotions.FirstOrDefault(pair => pair.Value == likedList && pair.Key.TrackId == fixtures[1].Id).Key;
                if (outgoing != null) { fade.Add(outgoing.Opacity); appearance.Add(outgoing.Opacity); }
                if (following != null) shift.Add(following.PaintedPosition.Y);
            }
            Window.FrameRendered += Sample;
            try
            {
                var remove = ToggleTrackLikeAsync(fixtures[0]);
                if (Like(first).IsEnabled || !Like(second).IsEnabled || !CanLikeTrack(fixtures[1]))
                    throw new InvalidOperationException("A pending like disabled unrelated tracks.");
                if (Like(first).Opacity != 1) throw new InvalidOperationException("Pending like flashed a faded disabled state.");
                await ToggleTrackLikeAsync(fixtures[0]);
                if (transport.Requests != 1) throw new InvalidOperationException("Duplicate like was sent while pending.");
                transport.Complete(fixtures[0].Id);
                await remove;
                await WaitForLoginFrameAsync();
                await WaitForLikedLayoutAsync(() => !likedItemMotions.Keys.Any(motion => motion.Moving));
                if (fade.Count(value => value is > .02 and < .98) < 3 || shift.Select(y => Math.Round(y, 1)).Distinct().Count() < 4)
                    throw new InvalidOperationException($"Missing removal frames: fade={string.Join(',', fade)}, shift={string.Join(',', shift)}");
                if (tracks.Any(track => track.Id == fixtures[0].Id) || libraryLikes.Tracks.Length != 29 ||
                    navigationGeneration != generation || nextHref != cursor || Math.Abs(viewer.VerticalOffset - scroll) > 1)
                    throw new InvalidOperationException("Like removal reloaded navigation, lost loaded tracks/cursor or moved the viewport.");
                foreach (var row in likedRows.Values.Where(row => row.Track != null))
                    if (Like(row).IsFocused || Like(row).BorderThickness != 0 || !Like(row).IsEnabled)
                        throw new InvalidOperationException("Recycled row retained focus, outline or disabled state.");

                appearance.Clear();
                var add = ToggleTrackLikeAsync(fixtures[0]); transport.Complete(fixtures[0].Id); await add;
                await WaitForLoginFrameAsync();
                await WaitForLikedLayoutAsync(() => !likedItemMotions.Keys.Any(motion => motion.Moving));
                if (tracks[0].Id != fixtures[0].Id || appearance.Count(value => value is > .02 and < .98) < 3)
                    throw new InvalidOperationException("Re-added like did not fade in at the start of the collection.");
            }
            finally { Window.FrameRendered -= Sample; }

            var failed = ToggleTrackLikeAsync(fixtures[2]);
            transport.Fail(fixtures[2].Id);
            try { await failed; throw new InvalidOperationException("Fixture error was swallowed."); }
            catch (HttpRequestException) { }
            if (!CanLikeTrack(fixtures[2]) || !likedIds.Contains(fixtures[2].Id) || !tracks.Any(track => track.Id == fixtures[2].Id))
                throw new InvalidOperationException("Failed like lost the track or left its button disabled.");

            var pending = ToggleTrackLikeAsync(fixtures[1]);
            page.Value = Page.Home; ReplaceTracks(new([fixtures[20]], null));
            transport.Complete(fixtures[1].Id); await pending;
            if (tracks.Count != 1 || tracks[0].Id != fixtures[20].Id || libraryLikes!.Tracks.Any(track => track.Id == fixtures[1].Id))
                throw new InvalidOperationException("Late like reply overwrote another page or failed to update cached likes.");
            ShowLibraryTracks(); likesAsList.Value = false;
            await WaitForLikedLayoutAsync(() => likedTiles.Values.Any(tile => tile.Track?.Id == fixtures[0].Id && tile.Root.ActualWidth > 100));
            var gridFade = new List<double>();
            void SampleGrid()
            {
                var item = likedItemMotions.FirstOrDefault(pair => pair.Value == likedGrid && pair.Key.TrackId == fixtures[0].Id).Key;
                if (item != null) gridFade.Add(item.Opacity);
            }
            Window.FrameRendered += SampleGrid;
            try
            {
                var remove = ToggleTrackLikeAsync(fixtures[0]); transport.Complete(fixtures[0].Id); await remove;
                await WaitForLoginFrameAsync();
                await WaitForLikedLayoutAsync(() => !likedItemMotions.Keys.Any(motion => motion.Moving));
                if (gridFade.Count(value => value is > .02 and < .98) < 3) throw new InvalidOperationException("Grid removal had no fade frames.");
            }
            finally { Window.FrameRendered -= SampleGrid; }

            likesAsList.Value = true;
            viewer.SetScrollOffsets(0, TrackRowLayout.Stride * 6);
            await WaitForLikedLayoutAsync(() => likedRows.Values.Any(row => row.Track?.Id == fixtures[8].Id));
            var scrolled = viewer.VerticalOffset;
            var removeScrolled = ToggleTrackLikeAsync(fixtures[8]); transport.Complete(fixtures[8].Id); await removeScrolled;
            await WaitForLoginFrameAsync();
            await WaitForLikedLayoutAsync(() => !likedItemMotions.Keys.Any(motion => motion.Moving));
            if (Math.Abs(viewer.VerticalOffset - scrolled) > 1 || nextHref != cursor)
                throw new InvalidOperationException("Removing a visible row changed a scrolled viewport or pagination cursor.");
            CaptureUiPreview("likes-edits");
            await VerifyRepostsUiAsync(transport, fixtures);
            Console.WriteLine("UI_LIKES_EDITS_OK: isolated pending likes, duplicate suppression, removal/entry/movement frames, stable viewport/cursor/navigation, recycled focus, failure recovery, late replies and grid fade");
        }
        catch (Exception error) { VerificationFailure = error; Console.Error.WriteLine("UI_LIKES_EDITS_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private static Button FindLikeAction(FrameworkElement root)
    {
        Button? found = null;
        VisualTree.Visit(root, element =>
        {
            if (element is Button button && button.StyleName is TrackButtons.Selected or TrackButtons.Tonal && button.Content is StackPanel)
                found = button;
        });
        return found ?? throw new InvalidOperationException("Like action missing.");
    }

    private sealed class LikesEditTransport : ISoundCloudBrowserTransport
    {
        private readonly Dictionary<long, TaskCompletionSource> pending = [];
        public int Requests { get; private set; }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) =>
            uri.AbsolutePath == "/me/track_reposts/ids" ? Task.FromResult(JsonDocument.Parse("[81004]")) :
            throw new InvalidOperationException("Like edit unexpectedly fetched a page: " + uri.AbsolutePath);
        public int RepostRequests { get; private set; }
        public bool LastReposted { get; private set; }
        public Task SetRepostedAsync(long trackId, bool reposted, CancellationToken cancellationToken = default)
        {
            RepostRequests++; LastReposted = reposted;
            var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(trackId, response);
            return response.Task.WaitAsync(cancellationToken);
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default)
        {
            Requests++;
            var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(trackId, response);
            return response.Task.WaitAsync(cancellationToken);
        }
        public void Complete(long id) { pending.Remove(id, out var response); response!.SetResult(); }
        public void Fail(long id) { pending.Remove(id, out var response); response!.SetException(new HttpRequestException("Fixture network failure")); }
    }
}
