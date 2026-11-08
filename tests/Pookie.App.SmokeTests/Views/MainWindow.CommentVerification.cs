using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyCommentsUiAsync()
    {
        try
        {
            var browser = new CommentFixtureBrowser();
            api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = browser;
            me = new() { Id = 42, Username = "Listener", AvatarUrl = "https://i1.sndcdn.com/comment-fixture-t500x500.jpg" };
            signedIn.Value = true;
            await imageDiskCache.WriteAsync(me.AvatarUrl, ArtworkFixtureBitmap(500), lifetime.Token);
            await ShowWorkspaceAsync();
            var track = new SoundCloudTrack { Id = 81001, Title = "Comments fixture", Duration = 120000, CommentCount = 2, Commentable = true,
                User = new() { Id = 7, Username = "Artist" } };
            var own = new SoundCloudComment { Id = 1, Body = "Мой комментарий", User = me, Timestamp = 10000, CreatedAt = "2026-10-11T10:00:00Z",
                Replies = [new() { Id = 11, Body = "Чужой ответ", User = track.User, Timestamp = 10000 }], ReplyCount = 1 };
            var other = new SoundCloudComment { Id = 2, Body = "Другой комментарий", User = track.User, Timestamp = 5000, CreatedAt = "2026-10-10T10:00:00Z" };
            browser.Comments = [own, other];
            await NavigateRouteAsync(new(Page.Track, Track: track), _ =>
            {
                trackDetailState = new(track, new(browser.Comments, null), new([], null), false, false)
                    { MetadataLoading = false, SidebarLoading = false };
                RenderTrackDetail(); return Task.CompletedTask;
            });
            await WaitForLikedLayoutAsync(() => trackCommentInput.ActualWidth > 80 && trackCommentOwnAvatar.Source is ImageSource);
            VerifyCommentTextPixels();
            if (Environment.GetEnvironmentVariable("POOKIE_COMMENT_PROBE") == "1")
            { await ProbeCommentComposerAsync(); return; }
            // Reproduce a selected like and an unselected repost on nonuniform
            // artwork. Empty backgrounds hide a frozen off-card glass sample.
            trackDetailBackdrop.Source = TrackArtFixture(); trackDetailBackdrop.Opacity = 1;
            trackDetailState = trackDetailState! with { Track = track with { LikesCount = 645, RepostsCount = 6 } };
            likedIds.Add(track.Id); RefreshTrackDetailPlayback();
            RouteWaveformPointer(new Point(1, 1));
            await WaitForLikedLayoutAsync(() => trackDetailGlass!.IsCurrent && trackDetailLike.Background == TrackButtons.SelectedSurface);
            if (trackCommentSend.IsEnabled || !trackCommentInput.IsEnabled) throw new InvalidOperationException("Empty composer availability is incorrect.");
            if (trackCommentInput.Placeholder != "Твой комментарий") throw new InvalidOperationException("Unexpected comment placeholder.");
            var restingWidth = trackCommentComposer.ActualWidth;
            var expansionFrames = new List<double>();
            var fadeVerified = false;
            var fadeFrames = 0;
            var leadingBounds = ((FrameworkElement)trackCommentActions.Children[0]).Bounds;
            var trailingBounds = ((FrameworkElement)trackCommentActions.Children[2]).Bounds;
            var returningSamples = 0;
            var returnVerified = false;
            void SampleExpansion()
            {
                expansionFrames.Add(trackCommentComposer.ActualWidth);
                var leading = (FrameworkElement)trackCommentActions.Children[0];
                var trailing = (FrameworkElement)trackCommentActions.Children[2];
                if (leading.Bounds != leadingBounds || trailing.Bounds != trailingBounds)
                    throw new InvalidOperationException("Comment actions moved instead of fading in place.");
                if (leading.Opacity > 0 && Math.Abs(trackCommentComposer.ActualWidth - restingWidth) > 1)
                    throw new InvalidOperationException("The growing editor overlaps still-visible actions.");
                if (leading.Opacity is > 0 and < 1)
                {
                    fadeFrames++;
                    if (!trackCommentActions.Expanded) returningSamples++;
                    if (leading.Opacity is > .15 and < .85 && (!fadeVerified || !trackCommentActions.Expanded && !returnVerified))
                    {
                        VerifyCommentActionOpacityPixels(); fadeVerified = true;
                        if (!trackCommentActions.Expanded) returnVerified = true;
                        CaptureUiPreview(trackCommentActions.Expanded ? "comments-fade-out" : "comments-fade-in");
                    }
                }
            }
            Window.FrameRendered += SampleExpansion;
            try
            {
                RouteWaveformClick(new(trackCommentInput.Bounds.X + 12, trackCommentInput.Bounds.Y + 12));
                trackCommentInput.Text = "Новый комментарий 🎧";
                await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion > .999999 &&
                    Math.Abs(trackCommentComposer.ActualWidth - trackCommentActions.ActualWidth) < 1);
                if (trackDetailLike.Opacity != 1 || trackDetailLike.Parent is not FrameworkElement { Opacity: 0, IsHitTestVisible: false, IsVisible: false } ||
                    expansionFrames.Count(w => w > restingWidth + 1 && w < trackCommentActions.ActualWidth - 1) < 3 || !fadeVerified || fadeFrames < 3)
                    throw new InvalidOperationException($"Composer expansion snapped or left actions interactive: fade={fadeVerified}, frames={expansionFrames.Count}, widths={string.Join(',', expansionFrames.Select(w => Math.Round(w)))}.");
                CaptureUiPreview("comments-expanded");
                var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", throwOnError: true)!;
                var escape = new KeyEventArgs(Key.Escape, 0);
                typeof(Window).GetMethod("RaisePreviewKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(Window, [escape]);
                if (!escape.Handled) router.GetMethod("KeyDown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, [Window, escape]);
                await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion == 0 && Math.Abs(trackCommentComposer.ActualWidth - restingWidth) < 1);
                if (returningSamples < 3 || !returnVerified) throw new InvalidOperationException("Returning buttons were not checked across intermediate frames.");
                Console.WriteLine("COMMENT_ACTION_FADE_OK: fixed button bounds, intermediate opacity, no editor overlap and unchanged selected/repost glass on coloured artwork");
                if (trackCommentComposer.IsFocusWithin || trackCommentInput.Text != "Новый комментарий 🎧")
                    throw new InvalidOperationException("Escape lost the draft or retained editing focus.");
                trackCommentInput.Focus(); await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion is > 0 and < 1);
                var interrupted = trackCommentActions.Expansion;
                Window.FocusManager.ClearFocus();
                if (trackCommentActions.Expansion != interrupted) throw new InvalidOperationException("Interrupted expansion jumped.");
                trackCommentInput.Focus();
                await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion == 1);
                RouteWaveformClick(new(1, 1));
                await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion == 0);
                if (trackCommentInput.Text != "Новый комментарий 🎧") throw new InvalidOperationException("Outside click lost the draft.");
                CaptureUiPreview("comments-collapsed");
                trackCommentInput.Focus();
                await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion == 1);
                trackCommentSend.Focus();
                if (!trackCommentActions.Expanded) throw new InvalidOperationException("Send focus collapsed the editor.");
                Window.FocusManager.MoveFocusNext();
                if (trackCommentActions.Children.OfType<FrameworkElement>().Any(e => e != trackCommentComposer && e.IsFocusWithin))
                    throw new InvalidOperationException("Keyboard focus entered a hidden action.");
                trackCommentInput.Focus();
                await WaitForLikedLayoutAsync(() => trackCommentActions.Expansion == 1);
            }
            finally { Window.FrameRendered -= SampleExpansion; }
            CaptureUiPreview("comments-composer");
            RouteWaveformClick(new(trackCommentSend.Bounds.X + 16, trackCommentSend.Bounds.Y + 16));
            await WaitForLikedLayoutAsync(() => browser.Creates == 1);
            if (trackCommentSend.IsEnabled || trackCommentInput.IsEnabled) throw new InvalidOperationException("Pending comment accepts a duplicate submit.");
            await PostTrackCommentAsync();
            if (browser.Creates != 1) throw new InvalidOperationException("Duplicate comment request.");
            browser.CompleteCreate();
            await WaitForLikedLayoutAsync(() => trackCommentInput.Text.Length == 0 && trackCommentInput.IsEnabled && trackDetailState!.Track.CommentCount == 3);
            if (trackDetailState!.Comments.Comments[0].Id != 3) throw new InvalidOperationException("Confirmed comment was not inserted.");
            trackCommentInput.Text = "Сохранить черновик";
            var failed = PostTrackCommentAsync(); browser.Create!.SetException(new HttpRequestException("Fixture failure")); await failed;
            if (trackCommentInput.Text != "Сохранить черновик" || !trackCommentSend.IsEnabled) throw new InvalidOperationException("Failed submit lost its draft.");
            trackCommentInput.Text = "";
            browser.DelayOldest = true;
            var oldSort = ChangeTrackCommentSortAsync("OLDEST");
            await ChangeTrackCommentSortAsync("TIMESTAMP");
            browser.Oldest.SetResult(); await oldSort;
            if (trackDetailState.CommentSort != "TIMESTAMP" || trackDetailState.Comments.Comments[0].Id != 2 ||
                trackCommentSortLabel.Text != "По времени в треке") throw new InvalidOperationException("Late sort response replaced the current order.");
            await ChangeTrackCommentSortAsync("NEWEST");
            await WaitForLikedLayoutAsync(() => !trackCommentsLoadingView.Skeleton.IsVisible && trackDetailComments.Opacity == 1);
            var timeButtons = new List<Button>();
            LogicalTree.Visit(trackDetailComments, e => { if (e is Button { StyleName: TrackButtons.CommentTime } b) timeButtons.Add(b); });
            await WaitForLikedLayoutAsync(() => trackDetailComments.ActualWidth > 0 && shellPageScroll.IsReady);
            shellScroll.SetScrollOffsets(0, Math.Max(0, shellScroll.VerticalOffset + trackDetailComments.Bounds.Y - shellScroll.Bounds.Y - 160));
            // Scrolling is reconciled at the frame boundary. Linux must also
            // hit the final row position, rather than its pre-scroll bounds.
            await WaitForLoginFrameAsync();
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && !trackDetailComments.IsArrangeDirty &&
                timeButtons[0].ActualWidth > 0 && timeButtons[0].Bounds.Y > trackDetailScroll.Bounds.Y &&
                timeButtons[0].Bounds.Y < trackDetailScroll.Bounds.Bottom - 40);
            if (OperatingSystem.IsWindows())
            {
                ProbeShowWindow(Window.Handle, 5);
                if (!ProbeSetWindowPos(Window.Handle, -1, 0, 0, 0, 0, 0x53)) throw new InvalidOperationException("Could not expose native comment check.");
                Window.Activate();
                await WaitForLoginFrameAsync();
                await WaitForLikedLayoutAsync(() => Window.IsActive && shellPageScroll.IsReady && !trackDetailComments.IsArrangeDirty);
            }
            // Scrolling can recycle the rows collected before it. Hover the
            // currently attached timestamp, never a detached previous binding.
            timeButtons.Clear();
            LogicalTree.Visit(trackDetailComments, e => { if (e is Button { StyleName: TrackButtons.CommentTime } b) timeButtons.Add(b); });
            var time = timeButtons.First(button => button.FindVisualRoot() == Window && button.ActualWidth > 0 &&
                button.Bounds.Y > trackDetailScroll.Bounds.Y && button.Bounds.Bottom < trackDetailScroll.Bounds.Bottom);
            var hoverPoint = new Point(time.Bounds.X + time.ActualWidth / 2, time.Bounds.Y + 12);
            MoveActionPointer(hoverPoint);
            try { await WaitForLikedLayoutAsync(() => time.IsMouseOver && time.Background == TrackButtons.HoverColor(TrackButtons.CommentTimeSurface, TrackButtons.OnCommentTime)); }
            catch (InvalidOperationException error)
            {
                var hit = ((UIElement)Window.Content!).HitTest(new(time.Bounds.X + time.ActualWidth / 2, time.Bounds.Y + 12));
                LikedActionPoint nativePointer = default;
                if (OperatingSystem.IsWindows()) GetLikedActionCursor(out nativePointer);
                CaptureUiPreview("comments-hover-failure");
                throw new InvalidOperationException($"Timestamp hover failed: sent={hoverPoint}, screen={Window.ClientToScreen(hoverPoint)}, native={nativePointer.X},{nativePointer.Y}, bounds={time.Bounds} rooted={time.FindVisualRoot() == Window} enabled={time.IsEffectivelyEnabled} over={time.IsMouseOver} active={Window.IsActive} viewport={shellScroll.Bounds} offset={shellScroll.VerticalOffset} colour={time.Background}, hit={hit?.GetType().Name}:{(hit as TextBlock)?.Text}, belongs={hit == time || hit != null && time.IsAncestorOf(hit)}, commentsHit={trackDetailComments.IsHitTestVisible}.", error);
            }
            MoveActionPointer(new(1, 1));
            await WaitForLikedLayoutAsync(() => !time.IsMouseOver && time.Background == TrackButtons.CommentTimeSurface);
            var commentMenus = new List<Button>();
            LogicalTree.Visit(trackDetailComments, e =>
            {
                if (e is Button { ContextMenu: not null } button && button.FindVisualRoot() == Window &&
                    button.Bounds.Y > trackDetailScroll.Bounds.Y && button.Bounds.Bottom < trackDetailScroll.Bounds.Bottom)
                    commentMenus.Add(button);
            });
            var commentMore = commentMenus.First();
            RouteWaveformClick(new Point(commentMore.Bounds.X + commentMore.ActualWidth / 2, commentMore.Bounds.Y + commentMore.ActualHeight / 2));
            var commentMenu = PopupAppearance.MenuFor(commentMore);
            await WaitForLikedLayoutAsync(() => commentMenu.IsOpen && commentMenu.Reveal == 1);
            if (commentMenu.Parent != Window.Content || commentMenu.Buttons.Count < 2)
                throw new InvalidOperationException("Track comment menu did not use the chat overlay or lost its commands.");
            VerifyContextMenuIcons(commentMenu);
            CaptureUiPreview("comments-menu-open");
            await VerifyMoreClosesMenuAsync(commentMore, commentMenu);
            RouteWaveformClick(commentMore.Bounds.Center);
            await WaitForLikedLayoutAsync(() => commentMenu.IsOpen && commentMenu.Reveal == 1);
            RouteSettingsKey(Key.Escape);
            await WaitForLikedLayoutAsync(() => !commentMenu.IsVisible && commentMenu.Parent == null);
            Console.WriteLine("COMMENTS_MENU_OVERLAY_OK: icon rows, actual comment More button toggles in-window menu, Escape fades it out");
            await DeleteTrackCommentAsync(track, other);
            if (browser.Deletes != 0) throw new InvalidOperationException("Another user's comment was deleted.");
            var deleting = DeleteTrackCommentAsync(track, own);
            await DeleteTrackCommentAsync(track, own);
            if (browser.Deletes != 1 || !trackDetailState.Comments.Comments.Any(c => c.Id == 1)) throw new InvalidOperationException("Deletion is premature or duplicated.");
            browser.Delete!.SetException(new HttpRequestException("Fixture failure")); await deleting;
            if (!trackDetailState.Comments.Comments.Any(c => c.Id == 1)) throw new InvalidOperationException("Failed deletion removed the comment.");
            deleting = DeleteTrackCommentAsync(track, own); browser.Delete!.SetResult(JsonDocument.Parse("""{"data":{"deleteTrackComment":true}}""")); await deleting;
            if (trackDetailState.Comments.Comments.Any(c => c.Id == 1) || !trackDetailState.Comments.Comments.Any(c => c.Id == 11))
                throw new InvalidOperationException("Deleting own parent removed another user's reply.");
            trackDetailState = trackDetailState with { Track = track with { Commentable = false } }; RenderTrackDetailHeader();
            if (trackCommentInput.IsEnabled || trackCommentSend.IsEnabled) throw new InvalidOperationException("Disabled track comments accept input.");
            trackDetailState = trackDetailState with { Track = track }; RenderTrackDetailHeader();
            trackCommentInput.Text = "После перехода";
            var navigatingPost = PostTrackCommentAsync();
            await NavigateRouteAsync(new(Page.Home), _ => Task.CompletedTask);
            if (browser.WriteToken.IsCancellationRequested) throw new InvalidOperationException("Navigation cancelled the shared browser write.");
            browser.CompleteCreate(); await navigatingPost;
            await MoveNavigationAsync(-1);
            if (page.Value != Page.Track || trackCommentInput.Text.Length != 0 || !trackCommentInput.IsEnabled ||
                !trackDetailState!.Comments.Comments.Any(c => c.Id == 3)) throw new InvalidOperationException("History retained a sent draft or lost the confirmed comment.");
            trackCommentInput.Text = "Поздний комментарий";
            var late = PostTrackCommentAsync(); api.Session = new("other", "fixture", "Other"); browser.CompleteCreate(); await late;
            if (trackCommentInput.Text != "Поздний комментарий") throw new InvalidOperationException("Late response cleared another session's draft.");
            Console.WriteLine("COMMENTS_UI_OK: animated composer, Escape/outside cancellation, retained draft, continuous reversal, send focus, avatar, real send click, duplicate suppression, confirmed insertion, retained failed draft, server sort race, timestamp hover restoration, own-only delete, failure recovery, reply preservation, navigation/history and stale session");
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("COMMENTS_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private void VerifyCommentActionOpacityPixels()
    {
        var scale = Window.DpiScale;
        var width = (int)Math.Ceiling(trackCommentActions.ActualWidth * scale);
        var height = (int)Math.Ceiling(44 * scale);
        byte[] Pixels(FrameworkElement element)
        {
            using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(width, height, scale, premultiplied: true));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface); context.Clear(Color.Transparent);
            context.Translate(-trackCommentActions.Bounds.X, -trackCommentActions.Bounds.Y);
            element.Render(context); context.EndFrame();
            return ((Aprillz.MewUI.Rendering.ICpuPixelSurface)surface).CopyPixels();
        }
        var actual = Pixels(trackCommentActions);
        var painted = 0;
        foreach (var index in new[] { 0, 2 })
        {
            var group = (FrameworkElement)trackCommentActions.Children[index];
            var reference = Pixels(group);
            var left = Math.Max(0, (int)((group.Bounds.X - trackCommentActions.Bounds.X + 6) * scale));
            var right = Math.Min(width, (int)((group.Bounds.Right - trackCommentActions.Bounds.X - 6) * scale));
            for (var x = left; x < right; x += 7)
            foreach (var y in new[] { 9, 22, 35 })
            {
                var pixel = ((int)(y * scale) * width + x) * 4;
                if (reference[pixel + 3] < 8) continue;
                painted++;
                for (var channel = 0; channel < 4; channel++)
                    if (Math.Abs(actual[pixel + channel] - reference[pixel + channel]) > 6)
                        throw new InvalidOperationException($"In-place fade changed action colour/coverage: group={index}, x={x}, y={y}, channel={channel}, actual={actual[pixel + channel]}, reference={reference[pixel + channel]}.");
            }
        }
        if (painted == 0) throw new InvalidOperationException("Action fade pixel check sampled no visible ink.");
        var editor = Pixels(trackCommentComposer);
        var middle = (int)((trackCommentComposer.Bounds.X + trackCommentComposer.ActualWidth / 2 - trackCommentActions.Bounds.X) * scale);
        var editorPixel = ((int)(22 * scale) * width + middle) * 4;
        for (var channel = 0; channel < 4; channel++)
            if (Math.Abs(actual[editorPixel + channel] - editor[editorPixel + channel]) > 1)
                throw new InvalidOperationException("Fading actions changed the comment editor.");
    }

    private sealed class CommentFixtureBrowser : ISoundCloudBrowserTransport
    {
        public SoundCloudComment[] Comments = [];
        public SoundCloudTrack? PlaybackTrack;
        public readonly List<TrackReadRequest> Reads = [];
        public int Creates, Deletes;
        public bool DelayOldest;
        public TaskCompletionSource Oldest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<JsonDocument>? Create, Delete;
        public CancellationToken WriteToken;
        private CommentWriteRequest? lastCreate;
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (PlaybackTrack is { } track && uri.AbsolutePath == $"/tracks/{track.Id}")
                return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(track with
                {
                    Media = new() { Transcodings = [new() { Url = "https://api-v2.soundcloud.com/fixture-stream", Format = new() { Protocol = "progressive" } }] }
                }, SoundCloudJson.Default.SoundCloudTrack)));
            if (uri.AbsolutePath == "/fixture-stream")
                return Task.FromResult(JsonDocument.Parse("""{"url":"https://cf-media.sndcdn.com/fixture"}"""));
            return Task.FromResult(JsonDocument.Parse(uri.AbsolutePath.EndsWith("/ids") ? "[]" : """{"collection":[],"next_href":null}"""));
        }
        public async Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default)
        {
            Reads.Add(request);
            if (request.Sort == "OLDEST" && DelayOldest) await Oldest.Task;
            var ordered = request.Sort == "TIMESTAMP" ? Comments.OrderBy(c => c.Timestamp) : request.Sort == "OLDEST"
                ? Comments.OrderBy(c => c.Id) : Comments.OrderByDescending(c => c.Id);
            object Row(SoundCloudComment c) => new { urn = "soundcloud:comments:" + c.Id, body = c.Body, createdAt = c.CreatedAt, trackTime = c.Timestamp,
                user = new { urn = "soundcloud:users:" + c.User?.Id, username = c.User?.Username },
                replies = new { total = c.ReplyCount, comments = c.Replies.Select(Row).ToArray(), pageInfo = new { hasNextPage = false } } };
            return JsonDocument.Parse(JsonSerializer.Serialize(new { data = new { trackComments = new { comments = ordered.Select(Row).ToArray(), pageInfo = new { endCursor = (string?)null } } } }));
        }
        public Task<JsonDocument> WriteCommentAsync(CommentWriteRequest request, CancellationToken cancellationToken = default)
        {
            WriteToken = cancellationToken;
            if (request.Kind == "create") { Creates++; lastCreate = request; Create = new(TaskCreationOptions.RunContinuationsAsynchronously); return Create.Task; }
            Deletes++; Delete = new(TaskCreationOptions.RunContinuationsAsynchronously); return Delete.Task;
        }
        public void CompleteCreate() => Create!.SetResult(JsonDocument.Parse(JsonSerializer.Serialize(new { data = new { createTrackComment = new { comment = new {
            urn = "soundcloud:comments:3", body = lastCreate!.Body, createdAt = "2026-10-11T12:00:00Z", trackTime = lastCreate.Timestamp,
            user = new { urn = "soundcloud:users:42", username = "Listener" } } } } })));
    }
}
