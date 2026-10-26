using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyUserProfilesAsync()
    {
        try
        {
            timer.Stop(); Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
            VerifySmoothBackgroundGradients();
            var fixture = new UserProfileFixture();
            api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = fixture;
            me = new() { Id = 42, Username = "Мой профиль" }; signedIn.Value = true; followingReady = false;
            userDescriptionLinks.LoadIcon = null;
            userBioLinks.LoadIcon = null;
            descriptionFavicons = new(imageDiskCache, new UserFaviconFixtureHandler(ArtworkFixtureBitmap(32)));
            var customArt = TrackArtFixture();
            trackArtRequests[fixture.Tracks[0].TrackArtUrl!] = Task.FromResult<ImageSource?>(customArt);
            foreach (var track in fixture.Tracks)
            {
                libraryCoverCache[track.Id] = ImageSource.FromBgraPixels(1, 1, [160, 120, 75, 255]);
                waveformRequests[track.WaveformUrl!] = Task.FromResult<float[]?>(Enumerable.Range(0, 240).Select(i => (float)(.2 + .7 * Math.Abs(Math.Sin(i * .27)))).ToArray());
            }
            await imageDiskCache.WriteAsync(fixture.User.AvatarUrl!, ArtworkFixtureBitmap(128), lifetime.Token);
            await imageDiskCache.WriteAsync(fixture.User.BannerUrl!, ArtworkFixtureBitmap(500), lifetime.Token);
            await ShowWorkspaceAsync();
            var original = current?.Id;
            fixture.ActivityHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.SidebarHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var bannerHold = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            userHeroImages[fixture.User.BannerUrl!] = bannerHold.Task;
            var opening = OpenLibraryItemAsync(UserItem(fixture.User with { Username = "Неполное имя", FollowersCount = null }));
            await WaitForLikedLayoutAsync(() => userOverviewLoading.Loading && userOverviewLoading.Skeleton.Running &&
                userOverviewLoading.Root.ActualWidth > 300 && userCommentsLoading.Loading && userHeader.Backdrop.Target != PlayerPalette.Neutral && !userHeader.Backdrop.Running);
            if (userOverviewLoading.Skeleton.Bounds.Right >= userCounters.Bounds.X ||
                userOverviewLoading.Skeleton.Bounds.Y <= userTabs.Bounds.Bottom || userBanner.Source != null ||
                userCommentsLoading.Skeleton.Bounds.X != userCounters.Bounds.X || userSectionLoading.Root.IsVisible)
                throw new InvalidOperationException("Profile loading crossed columns/headings or hid its avatar palette behind a pending banner");
            var headerHeight = userHero.ActualHeight;
            CaptureUiPreview("user-profile-loading");
            fixture.ActivityHold.SetResult();
            await WaitForLikedLayoutAsync(() => userPublicationRows.Count == 2 && !userOverviewLoading.Loading && userOverviewBody.Opacity > 0 && userOverviewBody.Opacity < 1);
            if (!userCommentsLoading.Loading || !userLikesLoading.Loading)
                throw new InvalidOperationException("Activity response prematurely completed unrelated sidebar loaders");
            CaptureUiPreview("user-profile-activity-reveal");
            fixture.SidebarHold.SetResult(); await opening;
            await WaitForLikedLayoutAsync(() => !userOverviewLoading.Skeleton.IsVisible && !userCommentsLoading.Skeleton.IsVisible);
            var sidebarRow = userSidebarRows.Single(); RenderUserProfile();
            if (!ReferenceEquals(sidebarRow, userSidebarRows.Single())) throw new InvalidOperationException("An unrelated profile render rebuilt ready sidebar rows");
            CaptureUiPreview("user-profile-avatar-gradient");
            bannerHold.SetResult(PrepareImage(ArtworkFixtureBitmap(500)));
            await WaitForLikedLayoutAsync(() => userHeader.Revealing && userHeader.BannerOpacity > 0 && userHeader.BannerOpacity < 1);
            if (Math.Abs(userHero.ActualHeight - headerHeight) > .5) throw new InvalidOperationException("A banner response changed the hero geometry");
            CaptureUiPreview("user-profile-banner-reveal");
            await WaitForLikedLayoutAsync(() => !userHeader.Revealing && userHeader.BannerOpacity > .999);
            fixture.ActivityHold = fixture.SidebarHold = null;
            await WaitForLikedLayoutAsync(() => page.Value == Page.UserProfile && userProfileState is { MetadataLoading: false } &&
                userHero.ActualHeight > 200 && userArtworkReady && !navigationHistory[navigationIndex].Pending);
            if (userName.Text != fixture.User.Username || userPublicationRows.Count != 2 || userPublications.Count != 3 || userSidebarRows.Count != 1 || userSidebarFollowers.Children.Count != 1 || !userBioLinks.Links.Any() || !userVerified.IsVisible || current?.Id != original || userFollowerCount.Text != "321")
                throw new InvalidOperationException("Profile metadata, overview rows, counts, verification or navigation without playback failed.");
            if (userPublications.Any(entry => entry.Root is not Grid) || userPublications.Any(entry => entry.Item.Playlist is { TrackCount: 0 }))
                throw new InvalidOperationException("Activity kept a card background or an empty playlist");
            await VerifyUserHeaderGlassAsync();
            await VerifyUserProfileBadgesAsync();
            await VerifyUserLinkFaviconsAsync();
            await WaitForLikedLayoutAsync(() => userPublicationRows.Any(row => row.Track?.Id == fixture.Tracks[0].Id && ReferenceEquals(row.TrackArt.Source, customArt)));
            await WaitForLikedLayoutAsync(() => userSidebarBiography.ActualWidth > 100 && userBioHeading.IsVisible && !userBioMore.IsVisible && userSidebarComments.Children.Count == 3);
            userProfileState = userProfileState! with { User = userProfileState.User with { Description = string.Join("\n", Enumerable.Repeat("Длинное описание профиля с музыкой и историями", 32)) } };
            RenderUserProfile();
            await WaitForLikedLayoutAsync(() => userBioHeading.IsVisible && userBioMore.IsVisible && userSidebarBiography.ExtentHeight > 180);
            CaptureUiPreview("user-profile-long-bio");
            userProfileState = userProfileState with { User = fixture.User }; RenderUserProfile();
            await WaitForLikedLayoutAsync(() => userBioHeading.IsVisible && !userBioMore.IsVisible);
            await WaitForLoginFrameAsync(); CaptureUiPreview("user-profile-overview");
            foreach (var width in new[] { 1000, 1600, 1160 })
            {
                Window.WindowSize = WindowSize.Resizable(width, 900, minWidth: 1000, minHeight: 680);
                await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width - width) < 1 && Math.Abs(contentFrame.ActualWidth - Math.Min(1440, width)) < 1 && !userHero.IsArrangeDirty);
                if (userHero.ActualHeight > 350 || userName.ActualWidth < 50 || userName.Bounds.Right > userHero.Bounds.Right - 20 || userTabs.Bounds.Right > contentFrame.Bounds.Right + 1)
                    throw new InvalidOperationException("Profile header or tabs overflowed the responsive page.");
                var row = userPublicationRows[0];
                var like = FindLikeAction(row.Root);
                if (Math.Abs(row.Cover.ActualWidth - TrackRowLayout.CoverSize) > 1 || Math.Abs(row.Cover.ActualHeight - TrackRowLayout.CoverSize) > 1 ||
                    Math.Abs(row.Waveform.Bounds.X - row.Cover.Bounds.Right - TrackRowLayout.ColumnGap - TrackRowLayout.ContentInset) > 1 || Math.Abs(like.Bounds.X - row.Waveform.Bounds.X) > 1 ||
                    like.Bounds.Y < row.Waveform.Bounds.Bottom || like.Bounds.Bottom > row.Root.Bounds.Bottom + 1)
                    throw new InvalidOperationException("Profile track diverged from the library cover/waveform/footer grid");
                VisualTree.Visit(row.Root, element =>
                {
                    if (element is Button button && button.Bounds.Right > row.Root.Bounds.Right + 1)
                        throw new InvalidOperationException("A profile track action overflowed its content column");
                });
                CaptureUiPreview("user-profile-" + width);
            }
            var heroY = userHero.Bounds.Y;
            shellScroll.SetScrollOffsets(0, 400);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && userHero.Bounds.Y < heroY - 200);
            CaptureUiPreview("user-profile-scrolled");
            var firstPublication = userPublications[0].Root;
            shellScroll.SetScrollOffsets(0, SmoothScroll.Maximum(shellScroll));
            await WaitForLikedLayoutAsync(() => userProfileState!.Sections[UserSection.Overview].Items.Length == 5 && userPublications.Count == 4);
            if (!ReferenceEquals(firstPublication, userPublications[0].Root) || userPublications.Select(entry => entry.Item.Key).Distinct().Count() != 4)
                throw new InvalidOperationException("Activity pagination replaced existing publications or duplicated an event");
            await PlayUserTrackAsync(fixture.Tracks[0]);
            if (playbackQueue.Context?.Key != "user:7:activity" || playbackQueue.Current?.Track.Id != fixture.Tracks[0].Id)
                throw new InvalidOperationException("Activity playback lost its source context");
            var playlistActions = userPlaylistActionViews.Single();
            shellScroll.SetScrollOffsets(0, 450);
            await WaitForLikedLayoutAsync(() => playlistActions.Root.ActualWidth > 0 &&
                Math.Abs(userPublicationCovers[0].ActualWidth - TrackRowLayout.CoverSize) <= 1 / Window.DpiScale);
            if (Math.Abs(playlistActions.Root.Bounds.X - userPublicationCovers[0].Bounds.Right - TrackRowLayout.ColumnGap - TrackRowLayout.ContentInset) > 1 ||
                Math.Abs(playlistActions.Like.Bounds.Y - playlistActions.Queue.Bounds.Y) > 1 || playlistActions.Repost.Bounds.Right > playlistActions.Queue.Bounds.X)
                throw new InvalidOperationException("Playlist artwork/actions do not share the library grid and a single footer row");
            await LoadUserPlaylistActionsAsync();
            if (!playlistActions.Like.IsEnabled || !playlistActions.Repost.IsEnabled) throw new InvalidOperationException("Profile playlist actions did not load their server state");
            var likePlaylist = TogglePlaylistActionAsync(playlistActions.Item.Playlist!, false);
            await TogglePlaylistActionAsync(playlistActions.Item.Playlist!, false); await likePlaylist;
            if (fixture.PlaylistLikeWrites != 1 || !likedPlaylistIds.Contains(101)) throw new InvalidOperationException("Playlist like lost confirmation or duplicate suppression");
            await TogglePlaylistActionAsync(playlistActions.Item.Playlist!, true);
            if (fixture.PlaylistRepostWrites != 1 || !repostedPlaylistIds.Contains(101)) throw new InvalidOperationException("Profile playlist repost did not update its shared state");
            CaptureUiPreview("user-profile-playlist-actions");
            var playlistItem = userPublications.Single(entry => entry.Item.Playlist != null).Item;
            await PlayUserPlaylistTrackAsync(playlistItem, fixture.Tracks[1]);
            if (playbackQueue.Context?.Key != playlistItem.Key || playbackQueue.Current?.Track.Id != fixture.Tracks[1].Id)
                throw new InvalidOperationException("Playlist preview playback lost the collection context");
            var manualBefore = playbackQueue.Snapshot.ManualUpcoming.Count;
            await EnqueueUserPlaylistAsync(playlistItem);
            if (!playbackQueue.Snapshot.ManualUpcoming.Skip(manualBefore).Select(entry => entry.Track.Id).SequenceEqual(fixture.Tracks.Take(19).Select(track => track.Id)))
                throw new InvalidOperationException("Playlist queue action enqueued only its preview instead of the full collection");
            await OpenUserSectionAsync(UserSection.Comments);
            await WaitForLikedLayoutAsync(() => userCommentsBody.Children.Count == 30 && shellPageScroll.IsReady);
            shellScroll.SetScrollOffsets(0, SmoothScroll.Maximum(shellScroll));
            await WaitForLikedLayoutAsync(() => userProfileState!.Comments?.Comments.Length == 35 && userCommentsBody.Children.Count == 35);
            if (userProfileState!.Comments!.NextHref != null) throw new InvalidOperationException("User comments pagination did not deduplicate or reach its end");
            CaptureUiPreview("user-profile-comments");
            await OpenUserSectionAsync(UserSection.About);
            var clipboard = new MusicComposerClipboard(""); userDescription.ClipboardService = clipboard;
            userDescription.SelectAll(); userDescription.Copy();
            if (!clipboard.TryGetText(out var bio) || bio != fixture.User.Description || userDescriptionLinks.Links.Count != 2 || userAboutLinks.Children.Count != 1)
                throw new InvalidOperationException("Profile biography lost selectable source text or link formatting.");
            CaptureUiPreview("user-profile-about");
            await OpenUserSectionAsync(UserSection.Tracks);
            await WaitForLikedLayoutAsync(() => userTrackList.ItemsSource.Count == 30 && SmoothScroll.Maximum(shellScroll) > 1000 && shellPageScroll.IsReady);
            shellScroll.SetScrollOffsets(0, SmoothScroll.Maximum(shellScroll));
            await WaitForLikedLayoutAsync(() => userProfileState!.Sections[UserSection.Tracks].Items.Length == 45);
            if (userProfileState!.Sections[UserSection.Tracks].NextHref != null) throw new InvalidOperationException("Profile pagination failed to reach the end.");
            await PlayUserTrackAsync(fixture.Tracks[31]);
            if (playbackQueue.Context?.Key != "user:7:tracks" || playbackQueue.Context.LibraryCursor != true || playbackQueue.Current?.Track.Id != fixture.Tracks[31].Id)
                throw new InvalidOperationException("Profile playback lost its artist context or selected track.");
            shellScroll.SetScrollOffsets(0, 420);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && Math.Abs(shellScroll.VerticalOffset - 420) < 1);
            await ShowSettingsAsync(SettingsSection.General); await MoveNavigationAsync(-1);
            await WaitForLikedLayoutAsync(() => page.Value == Page.UserProfile && userSection.Value == UserSection.Tracks &&
                userProfileState!.Sections[UserSection.Tracks].Items.Length == 45 && pendingNavigationScroll == null && Math.Abs(shellScroll.VerticalOffset - 420) < 1);
            foreach (var section in new[] { UserSection.Albums, UserSection.Playlists, UserSection.Reposts })
            {
                await OpenUserSectionAsync(section);
                await WaitForLikedLayoutAsync(() => userCardGrid.ItemsSource.Count == 1);
            }
            userProfileState!.Sections.Remove(UserSection.Likes);
            fixture.FailLikes = true; await OpenUserSectionAsync(UserSection.Likes);
            if (!userRetry.IsVisible || userProfileState!.Sections.ContainsKey(UserSection.Likes)) throw new InvalidOperationException("Profile section failure did not expose retry.");
            fixture.FailLikes = false; RetryUserProfile();
            await WaitForLikedLayoutAsync(() => userProfileState!.Sections.ContainsKey(UserSection.Likes) && !userRetry.IsVisible);
            await OpenUserSectionAsync(UserSection.Followers);
            await WaitForLikedLayoutAsync(() => collectionCards.Values.Any(card => card.Item.User?.Id == 8 && card.Cover.ActualWidth > 100));
            var follower = collectionCards.Single(pair => pair.Value.Item.User?.Id == 8).Key;
            RouteWaveformClick(SettingsCenter(follower));
            await WaitForLikedLayoutAsync(() => userProfileState?.User.Id == 8 && !navigationHistory[navigationIndex].Pending);
            if (userSidebarAboutSection.IsVisible || !string.IsNullOrWhiteSpace(userSidebarBiography.Text) || userSidebarLinks.Children.Count != 1)
                throw new InvalidOperationException("A missing biography left a description block or hid valid external links");
            if (userProfileState!.User.TrackCount != 0 || userPublicationRows.Count != 1 || userTracksCounter.IsVisible || userCounters.ColumnDefinitions.Count != 2)
                throw new InvalidOperationException("A listener with no own tracks lost their repost activity");
            await WaitForLikedLayoutAsync(() => !userCounters.IsArrangeDirty);
            var metricButtons = userCounters.Children.OfType<Button>().Where(button => button.IsVisible).ToArray();
            if (metricButtons.Length != 2 || Math.Abs(metricButtons[0].ActualWidth - metricButtons[1].ActualWidth) > 1 ||
                Math.Abs(metricButtons[1].Bounds.Right - userCounters.Bounds.Right) > 1)
                throw new InvalidOperationException("Hiding the zero-track metric left an empty column");
            await WaitForLikedLayoutAsync(() => userArtworkReady && !userHeader.Backdrop.Running);
            if (userBanner.Source != null || userHeader.Backdrop.Target == PlayerPalette.Neutral)
                throw new InvalidOperationException("A profile without a banner lost its avatar-derived gradient");
            if (userOverviewScroll.Padding.Bottom != 0 || userAboutScroll.Padding.Bottom != 0 || userCommentsScroll.Padding.Bottom != 0 ||
                !contentBottomFade.IsVisible || userOverviewScroll.Bounds.Bottom < contentBottomFade.Bounds.Bottom - 1)
                throw new InvalidOperationException("Profile content stopped above the shared bottom fade");
            CaptureUiPreview("user-profile-no-tracks");
            await PlayUserTrackAsync(userPublicationRows[0].Track!);
            if (playbackQueue.Context?.Key != "user:8:activity") throw new InvalidOperationException("Listener playback did not use their reposts");
            await MoveNavigationAsync(-1);
            await WaitForLikedLayoutAsync(() => userProfileState?.User.Id == 7 && userSection.Value == UserSection.Followers);
            await OpenUserSectionAsync(UserSection.Overview);
            Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
            var follow = artistFollowButtons.Single(entry => entry.Button.Content is StackPanel).Button;
            RouteWaveformClick(SettingsCenter(follow)); RouteWaveformClick(SettingsCenter(follow));
            await WaitForLikedLayoutAsync(() => fixture.FollowWrites == 1 && followingIds.Contains(7) && pendingFollows.Count == 0);
            if (((StackPanel)follow.Content!).Children.OfType<TextBlock>().Single().Text != "Вы подписаны")
                throw new InvalidOperationException("Profile follow state did not confirm the server response.");
            var lateBanner = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            const string lateBannerUrl = "https://i1.sndcdn.com/late-user-profile-banner.jpg";
            userHeroImages[lateBannerUrl] = lateBanner.Task;
            var late = OpenUserProfileAsync(fixture.User with { Id = 9, Username = "Delayed", Visuals = new([new(lateBannerUrl)]) });
            await WaitForLikedLayoutAsync(() => userProfileState?.User.Id == 9);
            await OpenUserProfileAsync(fixture.User);
            await WaitForLikedLayoutAsync(() => userArtworkReady);
            var cachedBanner = userBanner.Source;
            fixture.Delayed.SetResult(); lateBanner.SetResult(TrackArtFixture()); await late;
            await WaitForLoginFrameAsync();
            if (!ReferenceEquals(cachedBanner, userBanner.Source) || !ReferenceEquals(cachedBanner, await UserHeroImageAsync(fixture.User.BannerUrl)))
                throw new InvalidOperationException("A stale banner overwrote the active profile or a cached banner was decoded again");
            if (userProfileState?.User.Id != 7 || userName.Text != fixture.User.Username)
                throw new InvalidOperationException("A late profile reply replaced the current profile.");
            await OpenUserProfileAsync(me);
            if (follow.IsVisible) throw new InvalidOperationException("The profile offered to follow the current account.");
            Console.WriteLine("USER_PROFILE_UI_OK: independent loading skeletons, stable column geometry, avatar palette, banner crossfade/cache, shared bottom fade, hydrated hero, banner/avatar, overview, seven tabs and follower navigation, virtualized deduplicated pagination, source-preserving linked biography, retry, history/scroll restoration, responsive widths, confirmed follow and stale response isolation");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; CaptureUiPreview("user-profile-failure"); Console.Error.WriteLine("USER_PROFILE_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private sealed class UserProfileFixture : ISoundCloudBrowserTransport
    {
        internal SoundCloudUser User { get; } = new() { Id = 7, Username = "kurokovvv", FullName = "Даниил Куроков", City = "Москва", CountryCode = "RU", Verified = true,
            Description = "Музыка и истории, которые остаются с тобой 🎧\nНовые релизы, редкие записи и немного личного\nтгк: kurokovvv\nhttps://example.com/music",
            FollowersCount = 321, FollowingsCount = 24, TrackCount = 45, AvatarUrl = "https://i1.sndcdn.com/user-profile-avatar.jpg",
            PermalinkUrl = "https://soundcloud.com/kurokovvv", Visuals = new([new("https://i1.sndcdn.com/user-profile-banner.jpg")]) };
        internal SoundCloudTrack[] Tracks { get; }
        private static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        internal bool FailLikes;
        internal TaskCompletionSource? ActivityHold, SidebarHold;
        internal int FollowWrites, PlaylistLikeWrites, PlaylistRepostWrites;
        internal TaskCompletionSource Delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal UserProfileFixture() => Tracks = Enumerable.Range(0, 45).Select(i => new SoundCloudTrack { Id = 30000 + i, Title = "Всё ещё звучит — " + (i + 1),
            Visuals = i == 0 ? new(true, [new("https://i1.sndcdn.com/profile-track-art-original.jpg")]) : null,
            User = User, Duration = 120000, CreatedAt = "2026-10-20T14:00:00Z", Genre = "Electronic", PlaybackCount = 13520, LikesCount = 944, RepostsCount = 7, CommentCount = 31, WaveformUrl = $"https://wave.sndcdn.com/profile-{i}.json", PermalinkUrl = "https://soundcloud.com/kurokovvv/track-" + i }).ToArray();
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken token = default)
        {
            var path = uri.AbsolutePath;
            if (path == "/stream/users/7" && ActivityHold != null) await ActivityHold.Task;
            if (path.StartsWith("/users/7/") && !path.EndsWith("web-profiles") && SidebarHold != null) await SidebarHold.Task;
            if (path is "/me/playlist_likes/ids" or "/me/playlist_reposts/ids") return JsonDocument.Parse("[]");
            if (path == "/users/42/followings/ids") return JsonDocument.Parse("[]");
            if (path is "/users/7" or "/users/8" or "/users/9" or "/users/42")
            {
                var id = long.Parse(path.Split('/')[2]);
                if (id == 9) await Delayed.Task;
                return JsonSerializer.SerializeToDocument(User with { Id = id, Username = id == 8 ? "Другой слушатель" : User.Username, Visuals = id == 8 ? null : User.Visuals, TrackCount = id == 8 ? 0 : User.TrackCount, Description = id == 8 ? "  " : User.Description }, SoundCloudJson.Default.SoundCloudUser);
            }
            if (path.EndsWith("web-profiles")) return JsonDocument.Parse("""[{"url":"https://example.com/music","title":"Мой сайт"}]""");
            if (path == "/stream/users/8")
                return JsonSerializer.SerializeToDocument(new { collection = new[] { new { type = "track-repost", created_at = "2026-10-22T12:00:00Z", origin = Tracks[2] } } }, json);
            if (path == "/stream/users/7" && uri.Query.Contains("cursor="))
                return JsonSerializer.SerializeToDocument(new { collection = new[] {
                    new { type = "track-repost", created_at = "2026-10-20T12:00:00Z", origin = Tracks[1] },
                    new { type = "track", created_at = "2026-10-19T12:00:00Z", origin = Tracks[2] }
                } }, json);
            if (path == "/stream/users/7")
                return JsonSerializer.SerializeToDocument(new { collection = new object[] {
                    new { type = "track", created_at = "2026-10-22T12:00:00Z", origin = Tracks[0] },
                    new { type = "playlist", created_at = "2026-10-21T13:00:00Z", origin = new { kind = "playlist", id = 102, title = "Пустой плейлист", track_count = 0,
                        artwork_url = "https://i1.sndcdn.com/user-profile-banner.jpg", user = User, tracks = Array.Empty<SoundCloudTrack>() } },
                    new { type = "playlist", created_at = "2026-10-21T12:00:00Z", origin = new { kind = "playlist", id = 101, title = "Там, где мы остались", track_count = 19, permalink_url = "https://soundcloud.com/kurokovvv/sets/collection",
                        artwork_url = "https://i1.sndcdn.com/user-profile-banner.jpg", user = User, tracks = Tracks.Take(3) } },
                    new { type = "track-repost", created_at = "2026-10-20T12:00:00Z", origin = Tracks[1] with { User = User with { Id = 8, Username = "Другой исполнитель" } } }
                }, next_href = "https://api-v2.soundcloud.com/stream/users/7?cursor=next" }, json);
            if (path == "/playlists/101")
                return JsonSerializer.SerializeToDocument(new SoundCloudPlaylist { Id = 101, Title = "Там, где мы остались", TrackCount = 19, User = User, Tracks = Tracks.Take(19).ToArray() }, SoundCloudJson.Default.SoundCloudPlaylist);
            if (path == "/users/7/comments")
            {
                var next = uri.Query.Contains("cursor=");
                return JsonSerializer.SerializeToDocument(new { collection = Enumerable.Range(next ? 29 : 0, next ? 6 : 30).Select(i => new {
                    id = 50000 + i, body = "Этот момент очень нравится 🎧 — " + i, created_at = "2026-10-22T13:00:00Z", user = User, track = Tracks[i % Tracks.Length], timestamp = 15000
                }), next_href = next ? null : "https://api-v2.soundcloud.com/users/7/comments?cursor=next" }, json);
            }
            if (path == "/users/7/tracks")
            {
                var next = uri.Query.Contains("cursor=");
                return JsonSerializer.SerializeToDocument(new { collection = next ? Tracks.Skip(29) : Tracks.Take(30), next_href = next ? null : "https://api-v2.soundcloud.com/users/7/tracks?cursor=next" }, json);
            }
            if (FailLikes && path == "/users/7/likes") throw new SoundCloudException("Временная ошибка", 503);
            if (path == "/users/7/followers" || path == "/users/7/followings") return JsonDocument.Parse("""{"collection":[{"id":8,"username":"Другой слушатель","followers_count":12}]}""");
            if (path == "/users/7/albums" || path == "/users/7/playlists_without_albums")
                return JsonDocument.Parse("""{"collection":[{"kind":"playlist","id":101,"title":"Там, где мы остались","track_count":19,"artwork_url":"https://i1.sndcdn.com/user-profile-banner.jpg","user":{"id":7,"username":"kurokovvv"}},{"kind":"playlist","id":102,"title":"Пустой плейлист","track_count":0,"artwork_url":"https://i1.sndcdn.com/user-profile-banner.jpg"}]}""");
            if (path == "/stream/users/7/reposts" || path == "/users/7/likes")
                return JsonSerializer.SerializeToDocument(new { collection = new[] { new { track = Tracks[0] } } }, json);
            if (path.StartsWith("/users/") || path.StartsWith("/stream/users/")) return JsonDocument.Parse("""{"collection":[]}""");
            throw new InvalidOperationException("Unexpected profile request: " + path);
        }
        public async Task SetPlaylistLikedAsync(long userId, long playlistId, bool liked, CancellationToken token = default)
        { PlaylistLikeWrites++; await Task.Delay(80, token); }
        public Task SetPlaylistRepostedAsync(long playlistId, bool reposted, CancellationToken token = default)
        { PlaylistRepostWrites++; return Task.CompletedTask; }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken token = default) => Task.CompletedTask;
        public async Task SetFollowingAsync(long userId, long artistId, bool following, CancellationToken token = default)
        { FollowWrites++; await Task.Delay(100, token); }
    }
}
