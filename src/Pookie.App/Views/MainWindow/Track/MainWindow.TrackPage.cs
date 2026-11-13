using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record TrackDetailState(SoundCloudTrack Track, TrackCommentPage Comments, TrackPage Related,
        bool CommentsLoading = true, bool RelatedLoading = true, string CommentsError = "", string RelatedError = "", string MetadataError = "")
    {
        public TrackSidebar? Sidebar { get; init; }
        public bool SidebarLoading { get; init; } = true;
        public bool MetadataLoading { get; init; } = true;
        public IReadOnlyDictionary<TrackSection, LibraryPage> Sections { get; init; } = new Dictionary<TrackSection, LibraryPage>();
        public bool SectionLoading { get; init; }
        public string SectionError { get; init; } = "";
        public string CommentSort { get; init; } = "NEWEST";
        public string CommentDraft { get; init; } = "";
        public double? CommentTimestamp { get; init; }
    }
    private TrackDetailState? trackDetailState;
    private ScrollViewer trackDetailScroll = null!;
    private Image trackDetailCover = null!, trackDetailBackdrop = null!, trackDetailAvatar = null!, trackDetailPlayIcon = null!, trackDetailHeart = null!;
    private Border trackDetailScrim = null!;
    private TextBlock trackDetailTitle = null!, trackDetailAuthor = null!, trackDetailMeta = null!, trackDetailStats = null!;
    private DescriptionTextBlock trackDetailDescription = null!;
    private DescriptionLinks trackDescriptionLinks = null!;
    private TextBlock trackDetailAuthorName = null!, trackDetailAuthorStats = null!, trackDetailCommentsTitle = null!, trackDetailCommentsStatus = null!, trackDetailRelatedStatus = null!;
    private TextBlock trackDetailLikeCount = null!, trackDetailPosition = null!, trackDetailDuration = null!, trackDetailError = null!, trackDetailLicense = null!;
    private TrackWaveform trackDetailWaveform = null!;
    private float[]? renderedTrackDetailSamples;
    private StackPanel trackDetailComments = null!;
    private LibraryLoadingView trackCommentsLoadingView = null!;
    private Button trackDetailPlay = null!, trackDetailLike = null!, trackDetailMoreComments = null!;
    private readonly List<CompactTrackRow> trackDetailRelatedRows = [];
    private long renderedCommentsTrack;
    private double renderedCommentsDuration;
    private long renderedCommentsViewer;
    private SoundCloudComment[] renderedComments = [];
    private int renderedCommentCount;
    private SoundCloudComment[]? stagedCommentRows;
    private TaskCompletionSource? stagedCommentsReady;
    private bool trackPageWorkQueued;
    private long trackResizeCoalescingStarted;
    private bool trackTimelineVisible;

    private Task OpenTrackPageAsync(SoundCloudTrack track, TrackSection section = TrackSection.Overview, bool reload = false)
    {
        if (!reload && page.Value == Page.Track && trackDetailState?.Track.Id == track.Id)
            return OpenTrackSectionAsync(section);
        return NavigateRouteAsync(new(Page.Track, Track: track, TrackSection: section), async generation =>
        {
            var token = loading!.Token;
            trackDetailSection.Value = section; failedTrackSectionCursor = null;
            trackDetailState = new(track, new([], null), new([], null)) { CommentSort = preferences.CommentSort };
            RenderTrackDetail();
            if (demo && api.Session == null)
            {
                trackDetailState = trackDetailState with { CommentsLoading = false, RelatedLoading = false, SidebarLoading = false, MetadataLoading = false };
                RenderTrackDetail(); return;
            }
            Run(LoadFollowingAsync);
            // These sections are independent: a slow comments response must not hold
            // the title, cover or waveform behind a loading screen.
            await Task.WhenAll(LoadTrackDetailMetadataAsync(track.Id, generation, token),
                LoadTrackDetailCommentsAsync(track.Id, generation, token), LoadTrackDetailRelatedAsync(track.Id, generation, token),
                LoadTrackDetailSidebarAsync(track.Id, generation, token),
                section == TrackSection.Overview ? Task.CompletedTask : LoadTrackSectionAsync(section, generation, token));
        });
    }

    private bool IsTrackDetailCurrent(long id, long generation) => !disposed && page.Value == Page.Track &&
        generation == navigationGeneration && trackDetailState?.Track.Id == id;

    private void BeginTrackResize()
    {
        firstTrackWheelAfterResize = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!OperatingSystem.IsWindows() || Window.WindowState == WindowState.Minimized)
        {
            CompleteTrackResizePaint();
            return;
        }
        trackResizePaintPending = true;
        if (trackResizeCoalescer.IsEnabled) return;
        // WM_PAINT can arrive before the first wheel message. Give input one
        // native message-loop turn to join the resized frame. With no wheel,
        // the low-priority Win32 timer releases the paint on its own.
        trackResizeCoalescingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        trackResizeCoalescer.Start();
    }

    private bool HoldTrackResizeFrame() => trackResizeCoalescer.IsEnabled && page.Value == Page.Track &&
        System.Diagnostics.Stopwatch.GetElapsedTime(trackResizeCoalescingStarted).TotalMilliseconds < 32;

    private void EndTrackResizeCoalescing() => ReleaseTrackResizeCoalescing(invalidate: true);

    private bool ReleaseTrackResizeCoalescing(bool invalidate)
    {
        if (!trackResizeCoalescer.IsEnabled) return false;
        trackResizeCoalescer.Stop();
        if (invalidate) Window.InvalidateVisual();
        // Repost the native wake for work that remained queued during resize.
        Application.Current.Dispatcher!.BeginInvoke(DispatcherPriority.Background, static () => { });
        return true;
    }

    private void PaintResizedTrackWindow()
    {
        // The wheel has updated the offset. Present it before another native
        // message can spend the same frame on layout or loading work.
        Window.PerformLayout();
        const uint invalidate = 0x0001, noErase = 0x0020, noChildren = 0x0040, updateNow = 0x0100;
        var header = startupHeader as ShellHeader;
        var reuse = !expandedHost.IsVisible && !InAppPopup.IsPresenting(Window) &&
            !shellMenus.Any(menu => menu.Layer.Reveal > 0) &&
            header?.ReuseBlurForResizedFrame() == true;
        var previousPlayerRasterReuse = playerBackdrop.ReuseRasterForResize;
        var renderLoop = Application.Current.RenderLoopSettings;
        var vsync = renderLoop.VSyncEnabled;
        playerBackdrop.ReuseRasterForResize |= reuse;
        // This synchronous input frame goes through the composited swap chain.
        // Let the compositor pick it up without making the UI wait for vblank.
        if (reuse) renderLoop.VSyncEnabled = false;
        try { _ = RedrawTrackWindow(Window.Handle, 0, 0, invalidate | noErase | noChildren | updateNow); }
        finally
        {
            renderLoop.VSyncEnabled = vsync;
            playerBackdrop.ReuseRasterForResize = previousPlayerRasterReuse;
            if (reuse) header!.ResumeBlurCapture();
        }
        if (reuse)
        {
            header!.InvalidateVisual();
            playerBackdrop.InvalidateVisual();
            playerPlayButton.InvalidateVisual();
        }
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "RedrawWindow")]
    private static partial int RedrawTrackWindow(nint window, nint updateRect, nint updateRegion, uint flags);

    private void CompleteTrackResizePaint()
    {
        trackResizePaintPending = false;
        trackResizePaint?.TrySetResult();
        trackResizePaint = null;
    }

    private void TrackWindowFramePresented()
    {
        CompleteTrackResizePaint();
        if (!OperatingSystem.IsWindows() || disposed || page.Value != Page.Track || trackPageWorkQueued ||
            Window.WindowState == WindowState.Minimized || !workspace.IsVisible) return;
        var glass = expandedHost.IsVisible ? null : trackDetailGlass;
        glass?.RefreshArtwork(keepCurrent: TrackScrollInMotion());
        if (stagedCommentRows == null && !CanPrepareTrackGlass(glass)) return;
        trackPageWorkQueued = true;
        Application.Current.Dispatcher!.BeginInvoke(DispatcherPriority.Background, () =>
        {
            trackPageWorkQueued = false;
            if (disposed || page.Value != Page.Track || trackResizePaintPending) return;
            // One potentially expensive update per presented frame. The
            // current blur stays visible while the next sample is pending.
            if (!expandedHost.IsVisible && CanPrepareTrackGlass(glass) && glass!.Prepare()) return;
            if (stagedCommentRows != null) AppendTrackCommentBatch();
        });
    }

    private bool CanPrepareTrackGlass(CoverGlassBackdrop? glass) => glass is { NeedsPrepare: true } &&
        !(glass.CanReuseWhileScrolling && TrackScrollInMotion());

    private bool TrackScrollInMotion() => smoothScrolls.TryGetValue(shellScroll, out var motion) && motion.IsRunning;

    // Network results can arrive between WM_SIZE and the new frame. Keep
    // creating comment rows and sidebar controls off that first paint's path.
    private Task WaitForTrackResizePaintAsync(CancellationToken token) =>
        trackResizePaintPending && Window.WindowState != WindowState.Minimized
            ? (trackResizePaint ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(token)
            : Task.CompletedTask;

    private async Task LoadTrackDetailMetadataAsync(long id, long generation, CancellationToken token)
    {
        var commentsRevision = trackCommentCountRevision;
        try
        {
            var track = await api.GetTrackAsync(id, token);
            await WaitForTrackResizePaintAsync(token);
            if (!IsTrackDetailCurrent(id, generation)) return;
            if (commentsRevision != trackCommentCountRevision) track = track with { CommentCount = trackDetailState!.Track.CommentCount };
            trackDetailState = trackDetailState! with { Track = track, MetadataLoading = false };
            RenderTrackDetailHeader();
            RenderTrackDetailComments();
            await LoadTrackDetailArtworkAsync(track, generation);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        {
            if (!IsTrackDetailCurrent(id, generation)) return;
            trackDetailState = trackDetailState! with { MetadataError = FriendlyError(error), MetadataLoading = false };
            RenderTrackDetailHeader();
        }
    }

    private async Task LoadTrackDetailCommentsAsync(long id, long generation, CancellationToken token, string? cursor = null)
    {
        var sort = trackDetailState?.CommentSort ?? "NEWEST";
        var revision = trackCommentsRevision;
        try
        {
            var result = cursor == null ? await api.GetTrackCommentsAsync(id, token, sort) : await api.GetTrackCommentsNextAsync(id, cursor, token, sort);
            await WaitForTrackResizePaintAsync(token);
            if (!IsTrackDetailCurrent(id, generation) || revision != trackCommentsRevision || trackDetailState!.CommentSort != sort) return;
            var comments = cursor == null ? result.Comments : trackDetailState!.Comments.Comments.Concat(result.Comments).DistinctBy(comment => comment.Id).ToArray();
            trackDetailState = trackDetailState! with { Comments = new(comments, result.NextHref == cursor ? null : result.NextHref), CommentsLoading = false, CommentsError = "" };
            if (!OperatingSystem.IsWindows())
            {
                RenderTrackDetailComments();
                return;
            }
            FinishStagedComments();
            stagedCommentRows = comments;
            var rowsReady = stagedCommentsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RenderTrackDetailComments();
            await rowsReady.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        {
            if (!IsTrackDetailCurrent(id, generation) || revision != trackCommentsRevision || trackDetailState!.CommentSort != sort) return;
            trackDetailState = trackDetailState! with { CommentsLoading = false, CommentsError = FriendlyError(error) };
            RenderTrackDetailComments();
        }
    }

    private async Task LoadTrackDetailRelatedAsync(long id, long generation, CancellationToken token)
    {
        try
        {
            var result = await api.GetRelatedTracksAsync(id, token);
            await WaitForTrackResizePaintAsync(token);
            if (!IsTrackDetailCurrent(id, generation)) return;
            trackDetailState = trackDetailState! with { Related = result, RelatedLoading = false };
            RenderTrackDetailRelated();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        {
            if (!IsTrackDetailCurrent(id, generation)) return;
            trackDetailState = trackDetailState! with { RelatedLoading = false, RelatedError = FriendlyError(error) };
            RenderTrackDetailRelated();
        }
    }

    private async Task MoreTrackCommentsAsync()
    {
        if (trackDetailState is not { CommentsLoading: false } state) return;
        trackDetailState = state with { CommentsLoading = true, CommentsError = "" };
        RenderTrackDetailComments();
        // A failed first page can be retried through the same action.
        await LoadTrackDetailCommentsAsync(state.Track.Id, navigationGeneration, loading!.Token, state.Comments.NextHref);
    }

    private async Task LoadTrackDetailArtworkAsync(SoundCloudTrack track, long generation)
    {
        var sourceTask = GetLibraryArtworkAsync(track);
        var backdropTask = LoadTrackDetailBackdropAsync(track, generation, sourceTask);
        var samplesTask = GetTrackWaveformAsync(track);
        var avatarTask = SharedArtworkAsync(track.User?.AvatarUrl);
        try
        {
            var source = await sourceTask;
            if (!IsTrackDetailArtworkCurrent(track, generation)) return;
            SetCardArtwork(trackDetailCover, source, track.ArtworkUrl ?? track.User?.AvatarUrl, finished: true);
            var samples = await samplesTask;
            if (!IsTrackDetailArtworkCurrent(track, generation)) return;
            if (samples != null && !ReferenceEquals(renderedTrackDetailSamples, samples))
            { renderedTrackDetailSamples = samples; trackDetailWaveform.SetSamples(samples); }
            var avatar = await avatarTask;
            if (IsTrackDetailArtworkCurrent(track, generation)) trackDetailAvatar.Source = avatar != null ? avatar : Icons.Source("user");
        }
        finally { await backdropTask; }
    }

    private void RenderTrackDetail()
    {
        if (trackDetailState == null) return;
        renderedTrackDetailSamples = null; trackDetailWaveform.SetSamples([]); trackDetailBackdrop.Source = null;
        trackDetailScrim.Background = ArtworkBackdrop.CardScrim;
        trackDetailWaveform.OnArtwork = false;
        trackDetailGlass?.RefreshArtwork();
        var track = trackDetailState.Track;
        SetCardArtwork(trackDetailCover, libraryCoverCache.GetValueOrDefault(track.Id), track.ArtworkUrl ?? track.User?.AvatarUrl);
        trackDetailAvatar.Source = Icons.Source("user");
        RenderTrackDetailHeader(); RenderTrackDetailComments(); RenderTrackDetailRelated(); RenderTrackDetailSidebar(); RenderTrackSection();
        Run(() => LoadTrackDetailArtworkAsync(track, navigationGeneration));
    }

    private void RenderTrackDetailHeader()
    {
        if (trackDetailState is not { } state) return;
        var track = state.Track;
        // A late primary metadata response may still contain the preview length
        // after playback has resolved the full track through a regional source.
        if (audioReady && current is { } playing && playing.Id == track.Id && playing.DurationSeconds > track.DurationSeconds)
        {
            track = track with { Duration = playing.Duration, FullDuration = playing.FullDuration };
            trackDetailState = state = state with { Track = track };
        }
        RenderTrackCommentComposer();
        trackDetailTitle.Text = track.Title; trackDetailAuthor.Text = trackDetailAuthorName.Text = track.Author;
        trackDetailMeta.Text = "· " + string.Join(" · ", new[] { track.Genre, DetailAge(track.CreatedAt) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        trackDetailStats.Text = DetailCompactCount(track.PlaybackCount);
        trackDetailSidebarLikes.Text = DetailCompactCount(track.LikesCount);
        trackDetailSidebarReposts.Text = DetailCompactCount(track.RepostsCount);
        trackDetailCommentsTitle.Text = CommentHeading(track.CommentCount ?? state.Comments.Comments.Length);
        trackDetailDescription.Text = track.Description ?? "";
        trackDetailDescription.IsVisible = !string.IsNullOrWhiteSpace(track.Description);
        trackDetailLicense.Text = TrackLicense(track.License);
        trackDetailLicense.IsVisible = trackDetailLicense.Text.Length > 0;
        trackDetailAuthorStats.Text = string.Join("  ·  ", new[] {
            track.User?.FollowersCount is { } followers ? $"{DetailCount(followers)} подписчиков" : null,
            track.User?.TrackCount is { } trackCount ? $"{DetailCount(trackCount)} треков" : null }.Where(value => value != null));
        trackDetailDuration.Text = FormatTime(track.DurationSeconds);
        trackDetailError.Text = state.MetadataError;
        trackDetailError.IsVisible = state.MetadataError.Length > 0;
        RefreshTrackDetailPlayback(); RefreshFollowingButtons();
    }

    private void RefreshTrackDetailPlayback()
    {
        if (page.Value != Page.Track || trackDetailState is not { } state) return;
        var track = state.Track; var selected = track.Id == current?.Id;
        trackDetailPlayIcon.Source = Icons.Source(selected && isPlaying.Value ? "pause-solid" : "play-solid", TrackButtons.OnSurface);
        trackDetailPlay.Hint(selected && isPlaying.Value ? "Приостановить" : "Воспроизвести");
        trackDetailWaveform.IsPlaying = selected && isPlaying.Value;
        RefreshTrackDetailPosition();
        var liked = IsTrackLiked(track);
        TrackButtons.SetGlyph(trackDetailHeart, liked ? "heart-filled" : "heart", liked ? TrackButtons.OnSelected : TrackButtons.OnSurface);
        TrackButtons.SetTextColor(trackDetailLikeCount, liked ? TrackButtons.OnSelected : TrackButtons.OnSurface);
        TrackButtons.SetLiked(trackDetailLike, liked, TrackButtons.Glass);
        RefreshRepostState();
        trackDetailLike.Hint(liked ? "Убрать из понравившегося" : "Добавить в понравившееся");
        trackDetailLikeCount.Text = DetailCount(track.LikesCount);
        TrackButtons.SetAvailability(trackDetailLike, CanLikeTrack(track), IsLikePending(track));
    }

    private void RefreshTrackDetailPosition()
    {
        if (page.Value != Page.Track || trackDetailState is not { } state) return;
        if (!TrackTimelineInViewport())
        {
            trackTimelineVisible = false;
            AdvanceTrackCommentPreview(0, false);
            return;
        }
        var selected = state.Track.Id == current?.Id;
        trackDetailWaveform.Progress = selected && state.Track.DurationSeconds > 0 ? likedPlaybackPosition / state.Track.DurationSeconds : 0;
        trackDetailPosition.Text = FormatTime(selected ? likedPlaybackPosition : 0);
        var playing = selected && isPlaying.Value && !expandedOpen && Window.IsActive && Window.WindowState != WindowState.Minimized;
        var previewingSeek = seekDragging || pendingSeek != null || seeking;
        AdvanceTrackCommentPreview(selected ? likedPlaybackPosition : 0, playing && !previewingSeek);
    }

    private bool TrackTimelineInViewport() => page.Value == Page.Track && workspace.IsVisible &&
        trackDetailWaveform.Bounds.IntersectsWith(trackDetailScroll.Bounds.Intersect(shellScroll.Bounds));

    private void RefreshTrackTimelineViewport()
    {
        var visible = TrackTimelineInViewport();
        if (visible == trackTimelineVisible) return;
        trackTimelineVisible = visible;
        // Hidden text changes dirty the hero's layout. Retain playback position
        // in the model and catch up when scrolling reveals the hero again.
        RefreshTrackDetailPosition();
    }

    private void RenderTrackDetailRelated()
    {
        if (trackDetailState is not { } state) return;
        var related = state.Related.Tracks.Where(track => track.Id != state.Track.Id).Take(6).ToArray();
        for (var i = 0; i < trackDetailRelatedRows.Count; i++)
        {
            var row = trackDetailRelatedRows[i]; row.Root.IsVisible = i < related.Length;
            BindCompactTrackRow(row, i < related.Length ? related[i] : null);
        }
        trackDetailRelatedStatus.Text = state.RelatedLoading ? "Загружаем похожие треки…" : state.RelatedError.Length > 0 ? state.RelatedError : related.Length == 0 ? "Похожих треков пока нет." : "";
        trackDetailRelatedStatus.IsVisible = trackDetailRelatedStatus.Text.Length > 0;
    }

    private void RenderTrackDetailComments()
    {
        if (trackDetailState is not { } state) return;
        trackCommentSortLabel.Text = CommentSortLabel(state.CommentSort);
        trackDetailCommentsTitle.Text = CommentHeading(state.Track.CommentCount ?? state.Comments.Comments.Length);
        trackDetailCommentsStatus.Text = state.CommentsLoading ? "" : state.CommentsError.Length > 0 ? state.CommentsError : state.Comments.Comments.Length == 0 ? "К этому треку ещё нет комментариев." : "";
        trackDetailCommentsStatus.IsVisible = trackDetailCommentsStatus.Text.Length > 0;
        var comments = state.Comments.Comments;
        var changed = renderedCommentsTrack != state.Track.Id || renderedCommentsDuration != state.Track.DurationSeconds || !comments.SequenceEqual(renderedComments);
        if (stagedCommentRows != null && !ReferenceEquals(stagedCommentRows, comments)) FinishStagedComments();
        if (renderedCommentsTrack != state.Track.Id || renderedCommentsDuration != state.Track.DurationSeconds || renderedCommentsViewer != (me?.Id ?? 0) ||
            !comments.Take(renderedCommentCount).SequenceEqual(renderedComments.Take(renderedCommentCount)))
        {
            trackDetailComments.Clear(); renderedCommentCount = 0; renderedCommentsTrack = state.Track.Id;
            renderedCommentsDuration = state.Track.DurationSeconds;
            renderedCommentsViewer = me?.Id ?? 0;
        }
        // Keep existing rows and their decoded avatars when another page arrives.
        renderedComments = comments;
        if (stagedCommentRows == null)
            while (renderedCommentCount < comments.Length) AddTrackCommentRow(state.Track);
        else if (renderedCommentCount == comments.Length) FinishStagedComments();
        else trackDetailComments.InvalidateVisual();
        UpdateTrackCommentLoading(state);
        if (changed) RenderTrackCommentMarkers();
        ((TextBlock)trackDetailMoreComments.Content!).Text = state.CommentsError.Length > 0 ? "Повторить загрузку" : "Ещё комментарии";
    }

    private void AppendTrackCommentBatch()
    {
        if (trackDetailState is not { } state || !ReferenceEquals(state.Comments.Comments, stagedCommentRows))
        {
            FinishStagedComments();
            return;
        }
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var limit = Math.Min(renderedCommentCount + 4, renderedComments.Length);
        while (renderedCommentCount < limit)
        {
            AddTrackCommentRow(state.Track);
            if (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 3) break;
        }
        if (renderedCommentCount == renderedComments.Length) FinishStagedComments();
        UpdateTrackCommentLoading(state);
    }

    private void AddTrackCommentRow(SoundCloudTrack track)
    {
        trackDetailComments.Add(TrackCommentView(renderedComments[renderedCommentCount], track));
        renderedCommentCount++;
    }

    private void FinishStagedComments()
    {
        stagedCommentRows = null;
        stagedCommentsReady?.TrySetResult();
        stagedCommentsReady = null;
    }

    private void UpdateTrackCommentLoading(TrackDetailState state)
    {
        trackCommentsLoadingView.SetLoading(renderedCommentCount == 0 && (state.CommentsLoading || stagedCommentRows != null));
        trackDetailMoreComments.IsVisible = !state.CommentsLoading && stagedCommentRows == null &&
            (state.Comments.NextHref != null || state.CommentsError.Length > 0);
    }

    private static string CommentHeading(long count) => DetailCount(count) + " " +
        (count % 100 is >= 11 and <= 14 ? "комментариев" : (count % 10) switch { 1 => "комментарий", 2 or 3 or 4 => "комментария", _ => "комментариев" });

    private static string DetailCount(long? count) => count is { } value ? value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) : "—";
    private static string DetailCompactCount(long? count) => count switch
    {
        >= 1_000_000 => (count.Value / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "M",
        >= 10_000 => (count.Value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K",
        _ => DetailCount(count)
    };

    private static string TrackLicense(string? license) => license switch
    {
        "all-rights-reserved" => "",
        "cc-by" => "Creative Commons · Attribution",
        "cc-by-sa" => "Creative Commons · Attribution — ShareAlike",
        "cc-by-nd" => "Creative Commons · Attribution — NoDerivatives",
        "cc-by-nc" => "Creative Commons · Attribution — NonCommercial",
        "cc-by-nc-sa" => "Creative Commons · Attribution — NonCommercial — ShareAlike",
        "cc-by-nc-nd" => "Creative Commons · Attribution — NonCommercial — NoDerivatives",
        "cc0" => "Creative Commons · Public Domain",
        _ => ""
    };

    private void AttachTrackTitle(TextBlock title, Func<SoundCloudTrack?> track)
    {
        title.Cursor = CursorType.Hand;
        AttachTextLinkHover(title, normal => Color.FromArgb((byte)Math.Round(normal.A * .72), normal.R, normal.G, normal.B));
        title.MouseDown += args =>
        {
            if (args.Button != MouseButton.Left || track() is not { } item) return;
            args.Handled = true;
            Run(() => OpenTrackPageAsync(item));
        };
    }
}
