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
                RepostsCount = 28, License = "cc-by-nc-sa",
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
            await WaitForLikedLayoutAsync(() => page.Value == Page.Track && trackDetailState is { CommentsLoading: false, RelatedLoading: false, SidebarLoading: false } &&
                !navigationHistory[navigationIndex].Pending && trackDetailWaveform.HasSamples && trackDetailCover.Source is ImageSource);
            if (current?.Id != originalPlayback || playGeneration != originalGeneration)
                throw new InvalidOperationException("Opening a title started playback.");
            if (trackDetailState!.Comments.Comments.Length != 1 || trackDetailRelatedRows.Count(row => row.Root.IsVisible) != 1 ||
                !trackDetailTitle.Text.Contains("Этажи") || !trackDetailDescription.Text.Contains("релиза"))
                throw new InvalidOperationException("Track metadata, comments or related tracks were not displayed.");
            if (trackDetailState.Comments.Comments[0].Replies.Length != 1 || trackDetailState.Sidebar?.Fans.Length != 1 ||
                trackDetailPlaylists.Children.Count != 4 || trackDetailAlbums.Children.Count != 1 || trackDetailSidebarReposts.Text != "28")
                throw new InvalidOperationException("Threaded replies or track sidebar information were not displayed.");
            await WaitForLikedLayoutAsync(() => trackDetailDescription.Bounds.Y > 350 && trackDetailDescription.Bounds.Y < 650 &&
                trackDetailCommentsTitle.ActualHeight > 0);
            await WaitForLoginFrameAsync(); CaptureUiPreview("track-page");
            if (trackDetailTitle.Bounds.Y >= trackDetailAuthor.Bounds.Y || trackDetailPlay.Bounds.X >= trackDetailTitle.Bounds.X ||
                trackDetailLike.Bounds.Bottom > trackDetailCover.Bounds.Bottom + 1 || trackDetailLike.Bounds.Y <= trackDetailWaveform.Bounds.Y)
                throw new InvalidOperationException("Hero title, author, playback, waveform and actions do not follow the reference layout.");
            await MoreTrackRepliesAsync(fixtures[0], trackDetailState.Comments.Comments[0]);
            if (trackDetailState.Comments.Comments[0].Replies.Length != 2 || trackDetailState.Comments.Comments[0].RepliesCursor != null)
                throw new InvalidOperationException("Replies did not append or deduplicate correctly.");
            Window.WindowSize = WindowSize.Resizable(1000, 900, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => trackDetailCover.ActualWidth is >= 270 and <= 282 && trackDetailLike.Bounds.Right <= trackDetailCover.Bounds.X);
            await WaitForLoginFrameAsync(); CaptureUiPreview("track-page-narrow");
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
            if (page.Value != Page.Track || trackDetailState?.Comments.Comments.Length != 2 || trackDetailState.Sidebar?.Playlists.Length != 4)
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
            var copyButtons = new List<Aprillz.MewUI.Controls.Button>();
            Aprillz.MewUI.VisualTree.Visit(trackDetailScroll, element => {
                if (element is Aprillz.MewUI.Controls.Button button && button.StyleName == TrackButtons.Glass && button.Width == 44) copyButtons.Add(button);
            });
            await VerifyTrackPlaylistButtonAsync(copyButtons[1], trackDetailState!.Track);
            await VerifyTrackClipboardAsync(copyButtons[2], trackDetailState!.Track);
            repostedIds.Add(fixtures[1].Id); RefreshRepostState();
            if (trackDetailRepost.StyleName != TrackButtons.Reposted)
                throw new InvalidOperationException("Hero repost state was not selected.");
            repostedIds.Remove(fixtures[1].Id); RefreshRepostState();
            await VerifyActionColorTransitionsAsync();
            await VerifyTrackSectionsUiAsync();
            ShowLibraryTracks(); likesAsList.Value = true;
            await WaitForLikedLayoutAsync(() => likedRows.Values.Any(row => row.Track?.Id == fixtures[0].Id && row.Root.ActualHeight > 100));
            var copyRow = likedRows.Values.First(row => row.Track?.Id == fixtures[0].Id);
            BindLibraryTrackRow(copyRow, fixtures[0] with { PermalinkUrl = null });
            var listActions = new List<Aprillz.MewUI.Controls.Button>();
            VisualTree.Visit(copyRow.Root, element => {
                if (element is Aprillz.MewUI.Controls.Button button && button.StyleName is TrackButtons.Tonal or TrackButtons.Reposted &&
                    button.Content is Aprillz.MewUI.Controls.Image) listActions.Add(button);
            });
            await VerifyTrackPlaylistButtonAsync(listActions[1], fixtures[0]);
            await VerifyTrackClipboardAsync(listActions[2], fixtures[0]);
            Console.WriteLine("TRACK_PAGE_UI_OK: reference hero, responsive cover/sidebar, threaded comments/replies, shared seek, sidebar fans/playlists/albums, pagination, back/forward and stale response protection");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("TRACK_PAGE_UI_FAILED: " + error); }
        finally { api.Session = oldSession; api.BrowserTransport = oldTransport; Window.Close(); }
    }

    private async Task VerifyTrackPlaylistButtonAsync(Aprillz.MewUI.Controls.Button button, SoundCloudTrack track)
    {
        RouteWaveformClick(new(button.Bounds.X + button.ActualWidth / 2, button.Bounds.Y + button.ActualHeight / 2));
        await WaitForLikedLayoutAsync(() => playlistPickerOpen.Value && playlistPickerTarget?.Id == track.Id);
        ClosePlaylistPicker();
        Console.WriteLine("TRACK_PLAYLIST_BUTTON_OK: real icon click opens the picker for the bound track");
    }

    private sealed class TrackPageFixtureBrowser(SoundCloudTrack[] fixtures) : ISoundCloudBrowserTransport
    {
        public long DelayTrack { get; set; }
        public bool DelayComments { get; set; }
        public TaskCompletionSource DelayedComments { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SoundCloudTrack> Delayed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Kind == "sidebar") return JsonDocument.Parse("""
                {"data":{"topFans":{"hasArtistOptedOut":false,"allTime":{"fans":[{"fan":{"urn":"soundcloud:users:8","username":"Слушатель"},"totalPlays":210}]}},
                "playlists":[{"urn":"soundcloud:playlists:10","title":"Любимые треки","user":{"urn":"soundcloud:users:8","username":"Слушатель"}}, {"urn":"soundcloud:playlists:12","title":"Вторая подборка"}, {"urn":"soundcloud:playlists:13","title":"Третья подборка"}],
                "albums":[{"urn":"soundcloud:playlists:11","title":"Альбом","user":{"urn":"soundcloud:users:7","username":"wqombo"}}]}}
                """);
            if (request.Kind == "replies") return JsonDocument.Parse("""
                {"data":{"trackCommentReplies":{"total":2,"comments":[{"urn":"soundcloud:comments:10","body":"duplicate"},
                {"urn":"soundcloud:comments:11","body":"И мне нравится"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}
                """);
            if (DelayComments && request.Cursor != null) await DelayedComments.Task;
            return JsonDocument.Parse(request.Cursor != null
                ? """{"data":{"trackComments":{"comments":[{"urn":"soundcloud:comments:1","body":"duplicate"},{"urn":"soundcloud:comments:2","body":"Ещё один комментарий"}],"pageInfo":{"endCursor":null}}}}"""
                : """
                {"data":{"trackComments":{"comments":[{"urn":"soundcloud:comments:1","body":"Этот момент особенно хорош 🎧","createdAt":"2026-09-09T12:00:00Z","trackTime":15000,
                "user":{"urn":"soundcloud:users:8","username":"Слушатель"},"replies":{"total":2,
                "comments":[{"urn":"soundcloud:comments:10","body":"Согласен, отличный трек","createdAt":"2026-09-15T12:00:00Z","trackTime":15000,"user":{"urn":"soundcloud:users:9","username":"Другой слушатель"}}],
                "pageInfo":{"hasNextPage":true,"endCursor":"reply-next"}}}],"pageInfo":{"endCursor":"next"}}}}
                """);
        }
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var id = long.Parse(uri.AbsolutePath.Split('/')[2]);
            if (uri.AbsolutePath.StartsWith("/playlists/")) return JsonDocument.Parse($$$"""{"id":{{{id}}},"kind":"playlist","title":"Подборка","artwork_url":"https://i1.sndcdn.com/track-page-fixture-t500x500.jpg","is_album":{{{(id == 11 ? "true" : "false")}}}}""");
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
            if (uri.AbsolutePath.EndsWith("/playlists_without_albums") && uri.Query.Contains("limit=4"))
                return JsonDocument.Parse(JsonSerializer.Serialize(new {
                    collection = Enumerable.Range(20, 4).Select(index => new {
                        id = index, kind = "playlist", title = "Подборка " + index,
                        artwork_url = "https://i1.sndcdn.com/track-page-fixture-t500x500.jpg", track_count = index,
                        user = new { id = 7, username = "Исполнитель" }
                    }), next_href = $"https://api-v2.soundcloud.com/tracks/{id}/playlists_without_albums?cursor=next"
                }));
            if (uri.AbsolutePath.EndsWith("/playlists_without_albums")) return JsonDocument.Parse(uri.Query.Contains("cursor=next")
                ? """{"collection":[{"id":13,"kind":"playlist","title":"Третья подборка"},{"id":14,"kind":"playlist","title":"Четвёртая подборка"}],"next_href":null}"""
                : $$$"""{"collection":[{"id":10,"kind":"playlist","title":"Любимые треки"},{"id":12,"kind":"playlist","title":"Вторая подборка"},{"id":13,"kind":"playlist","title":"Третья подборка"}],"next_href":"https://api-v2.soundcloud.com/tracks/{{{id}}}/playlists_without_albums?cursor=next"}""");
            if (uri.AbsolutePath.EndsWith("/reposters")) return JsonDocument.Parse("""{"collection":[{"id":8,"kind":"user","username":"Репостер","followers_count":5}],"next_href":null}""");
            if (uri.AbsolutePath.EndsWith("/albums")) return JsonDocument.Parse("""{"collection":[{"id":11,"kind":"playlist","is_album":true,"title":"Альбом"}],"next_href":null}""");
            var track = id == DelayTrack ? await Delayed.Task : fixtures.Single(track => track.Id == id);
            return JsonDocument.Parse(JsonSerializer.Serialize(track, SoundCloudJson.Default.SoundCloudTrack));
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
