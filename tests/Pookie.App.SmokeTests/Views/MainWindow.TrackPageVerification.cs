using System.Text.Json;
using Aprillz.MewUI;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyTrackPageUiAsync()
    {
        var oldSession = api.Session; var oldTransport = api.BrowserTransport;
        try
        {
            Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
            var fixtures = tracks.Take(2).Select((track, index) => track with
            {
                Title = index == 0 ? "Дипинс — Этажи (speed up)" : "Следующий трек",
                User = new() { Id = 7, Username = "wqombo 🎧", FollowersCount = 4134, TrackCount = 69 },
                Duration = 119000, Description = "Здесь начинается история трека.\nОписание автора и детали релиза.",
                Genre = "Electronic", CreatedAt = "2022-07-10T12:00:00Z", PlaybackCount = 1140000, LikesCount = 12190, CommentCount = 273,
                ArtworkUrl = "https://i1.sndcdn.com/track-page-fixture-t500x500.jpg",
                WaveformUrl = "https://wave.sndcdn.com/track-page-fixture.json",
                PermalinkUrl = "https://soundcloud.com/kar-tonw/dipins-etazhi-speed-up"
            }).ToArray();
            if (fixtures.Length != 2) throw new InvalidOperationException("Demo tracks unavailable.");
            await imageDiskCache.WriteAsync(fixtures[0].ArtworkUrl!, ArtworkFixtureBitmap(500), lifetime.Token);
            waveformRequests[fixtures[0].WaveformUrl!] = Task.FromResult<float[]?>(Enumerable.Range(0, 300)
                .Select(i => (float)(.22 + .55 * Math.Abs(Math.Sin(i * .41)) * (.45 + .55 * Math.Abs(Math.Sin(i * .037))))).ToArray());
            var browser = new TrackPageFixtureBrowser(fixtures);
            api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = browser;
            libraryLikes = new(fixtures, null); ShowLibraryTracks(); likesAsList.Value = true;
            await WaitForLikedLayoutAsync(() => likedRows.Values.Any(row => row.Track?.Id == fixtures[0].Id && row.Title.ActualWidth > 100));
            var originalPlayback = current?.Id; var originalGeneration = playGeneration;
            if (OperatingSystem.IsWindows())
            {
                var row = likedRows.Values.First(row => row.Track?.Id == fixtures[0].Id && row.Title.ActualWidth > 100);
                var point = new Point(row.Title.Bounds.X + 12, row.Title.Bounds.Y + row.Title.Bounds.Height / 2);
                SendLikedActionMouse(0x200, 0, point); SendLikedActionMouse(0x201, 1, point); SendLikedActionMouse(0x202, 0, point);
            }
            else await OpenTrackPageAsync(fixtures[0]);
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState is { CommentsLoading: false, RelatedLoading: false } &&
                !navigationHistory[navigationIndex].Pending && trackDetailWaveform.HasSamples && trackDetailCover.Source is ImageSource);
            if (current?.Id != originalPlayback || playGeneration != originalGeneration)
                throw new InvalidOperationException("Opening a title started playback.");
            if (trackDetailState!.Comments.Comments.Length != 1 || trackDetailRelatedRows.Count(row => row.Root.IsVisible) != 1 ||
                !trackDetailTitle.Text.Contains("Этажи") || !trackDetailDescription.Text.Contains("релиза"))
                throw new InvalidOperationException("Track metadata, comments or related tracks were not displayed.");
            await WaitForLikedLayoutAsync(() => trackDetailDescription.Bounds.Y > 350 && trackDetailDescription.Bounds.Y < 650 &&
                trackDetailCommentsTitle.ActualHeight > 0);
            await WaitForLoginFrameAsync(); CaptureUiPreview("track-page");
            browser.DelayComments = true;
            var cancelledComments = MoreTrackCommentsAsync();
            await MoveNavigationAsync(-1); await MoveNavigationAsync(1);
            browser.DelayedComments.SetResult(); await cancelledComments;
            if (trackDetailState is not { CommentsLoading: false, Comments.NextHref: not null })
                throw new InvalidOperationException("Returning to a page kept a cancelled comments request in its loading state.");
            browser.DelayComments = false;
            await MoreTrackCommentsAsync();
            if (trackDetailState.Comments.Comments.Length != 2 || trackDetailMoreComments.IsVisible)
                throw new InvalidOperationException("Comments pagination did not deduplicate entries or reach its end.");
            await MoveNavigationAsync(-1);
            if (page.Value != Page.LibraryTracks || !likesAsList.Value || tracks.Count != 2)
                throw new InvalidOperationException("Back lost the likes view.");
            await MoveNavigationAsync(1);
            if (page.Value != Page.Track || trackDetailState?.Comments.Comments.Length != 2)
                throw new InvalidOperationException("Forward lost loaded track comments.");
            browser.DelayTrack = fixtures[0].Id;
            var late = OpenTrackPageAsync(fixtures[0]);
            await OpenTrackPageAsync(fixtures[1]);
            browser.Delayed.SetResult(fixtures[0] with { Title = "Stale response" });
            await late;
            if (trackDetailState?.Track.Id != fixtures[1].Id || trackDetailTitle.Text != fixtures[1].Title)
                throw new InvalidOperationException("A late metadata response replaced the new track page.");
            SetQueue(fixtures[1]);
            if (playbackQueue.Current?.Track.Id != fixtures[1].Id || playbackQueue.Context?.Kind != Pookie.App.Playback.PlaybackContextKind.Track)
                throw new InvalidOperationException("Track page playback copied the likes queue.");
            await PlayAsync(fixtures[1]);
            await SeekLikedTrackAsync(fixtures[1], .25);
            if (Math.Abs(progress.Value - progress.Maximum * .25) > .1 || trackDetailPosition.Text != FormatTime(progress.Value))
                throw new InvalidOperationException("Track page seek is not connected to the shared player.");
            Console.WriteLine("TRACK_PAGE_UI_OK: native title click, shared player seek, metadata/cover/waveform/comments/related, pagination, back/forward and stale response protection");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("TRACK_PAGE_UI_FAILED: " + error); }
        finally { api.Session = oldSession; api.BrowserTransport = oldTransport; Window.Close(); }
    }

    private sealed class TrackPageFixtureBrowser(SoundCloudTrack[] fixtures) : ISoundCloudBrowserTransport
    {
        public long DelayTrack { get; set; }
        public bool DelayComments { get; set; }
        public TaskCompletionSource DelayedComments { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SoundCloudTrack> Delayed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var id = long.Parse(uri.AbsolutePath.Split('/')[2]);
            if (uri.AbsolutePath.EndsWith("/comments"))
            {
                if (DelayComments && uri.Query.Contains("cursor=")) await DelayedComments.Task;
                return JsonDocument.Parse(uri.Query.Contains("cursor=")
                    ? """{"collection":[{"id":1,"body":"duplicate"},{"id":2,"body":"Ещё один комментарий"}],"next_href":null}"""
                    : $$$"""{"collection":[{"id":1,"body":"Этот момент особенно хорош 🎧","created_at":"2026-10-09T12:00:00Z","timestamp":15000,"user":{"id":8,"username":"Слушатель"}}],"next_href":"https://api-v2.soundcloud.com/tracks/{{{id}}}/comments?cursor=next"}""");
            }
            if (uri.AbsolutePath.EndsWith("/related"))
            {
                var related = JsonSerializer.Serialize(fixtures.Single(track => track.Id != id), SoundCloudJson.Default.SoundCloudTrack);
                return JsonDocument.Parse("{\"collection\":[" + related + "],\"next_href\":null}");
            }
            var track = id == DelayTrack ? await Delayed.Task : fixtures.Single(track => track.Id == id);
            return JsonDocument.Parse(JsonSerializer.Serialize(track, SoundCloudJson.Default.SoundCloudTrack));
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
