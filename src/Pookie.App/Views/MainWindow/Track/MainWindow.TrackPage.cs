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
    }
    private TrackDetailState? trackDetailState;
    private ScrollViewer trackDetailScroll = null!;
    private Image trackDetailCover = null!, trackDetailBackdrop = null!, trackDetailAvatar = null!, trackDetailPlayIcon = null!, trackDetailHeart = null!;
    private TextBlock trackDetailTitle = null!, trackDetailAuthor = null!, trackDetailMeta = null!, trackDetailStats = null!, trackDetailDescription = null!;
    private TextBlock trackDetailAuthorName = null!, trackDetailAuthorStats = null!, trackDetailCommentsTitle = null!, trackDetailCommentsStatus = null!, trackDetailRelatedStatus = null!;
    private TextBlock trackDetailLikeCount = null!, trackDetailPosition = null!, trackDetailDuration = null!, trackDetailError = null!, trackDetailLicense = null!;
    private TrackWaveform trackDetailWaveform = null!;
    private float[]? renderedTrackDetailSamples;
    private StackPanel trackDetailComments = null!;
    private Button trackDetailPlay = null!, trackDetailLike = null!, trackDetailMoreComments = null!;
    private readonly List<CompactTrackRow> trackDetailRelatedRows = [];
    private long renderedCommentsTrack;
    private double renderedCommentsDuration;
    private SoundCloudComment[] renderedComments = [];

    private Task OpenTrackPageAsync(SoundCloudTrack track, TrackSection section = TrackSection.Overview) => NavigateRouteAsync(new(Page.Track, Track: track, TrackSection: section), async generation =>
    {
        var token = loading!.Token;
        trackDetailSection.Value = section; failedTrackSectionCursor = null;
        trackDetailState = new(track, new([], null), new([], null));
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

    private bool IsTrackDetailCurrent(long id, long generation) => !disposed && page.Value == Page.Track &&
        generation == navigationGeneration && trackDetailState?.Track.Id == id;

    private async Task LoadTrackDetailMetadataAsync(long id, long generation, CancellationToken token)
    {
        try
        {
            var track = await api.GetTrackAsync(id, token);
            if (!IsTrackDetailCurrent(id, generation)) return;
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
        try
        {
            var result = cursor == null ? await api.GetTrackCommentsAsync(id, token) : await api.GetTrackCommentsNextAsync(id, cursor, token);
            if (!IsTrackDetailCurrent(id, generation)) return;
            var comments = cursor == null ? result.Comments : trackDetailState!.Comments.Comments.Concat(result.Comments).DistinctBy(comment => comment.Id).ToArray();
            trackDetailState = trackDetailState! with { Comments = new(comments, result.NextHref == cursor ? null : result.NextHref), CommentsLoading = false, CommentsError = "" };
            RenderTrackDetailComments();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        {
            if (!IsTrackDetailCurrent(id, generation)) return;
            trackDetailState = trackDetailState! with { CommentsLoading = false, CommentsError = FriendlyError(error) };
            RenderTrackDetailComments();
        }
    }

    private async Task LoadTrackDetailRelatedAsync(long id, long generation, CancellationToken token)
    {
        try
        {
            var result = await api.GetRelatedTracksAsync(id, token);
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
        var samplesTask = GetTrackWaveformAsync(track);
        var avatarTask = SharedArtworkAsync(track.User?.AvatarUrl);
        var source = await sourceTask;
        if (!IsTrackDetailCurrent(track.Id, generation)) return;
        SetCardArtwork(trackDetailCover, source, track.ArtworkUrl ?? track.User?.AvatarUrl, finished: true);
        var backdrop = source == null ? null : await Task.Run(() => ArtworkBackdrop.Create(source), lifetime.Token);
        if (!IsTrackDetailCurrent(track.Id, generation)) return;
        trackDetailBackdrop.Source = backdrop;
        var samples = await samplesTask;
        if (!IsTrackDetailCurrent(track.Id, generation)) return;
        if (samples != null && !ReferenceEquals(renderedTrackDetailSamples, samples))
        { renderedTrackDetailSamples = samples; trackDetailWaveform.SetSamples(samples); }
        var avatar = await avatarTask;
        if (IsTrackDetailCurrent(track.Id, generation)) trackDetailAvatar.Source = avatar != null ? avatar : Icons.Source("user");
    }

    private void RenderTrackDetail()
    {
        if (trackDetailState == null) return;
        renderedTrackDetailSamples = null; trackDetailWaveform.SetSamples([]); trackDetailBackdrop.Source = null;
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
        trackDetailPlayIcon.Source = Icons.Source(selected && isPlaying.Value ? "pause-solid" : "play-solid", TrackButtons.OnPrimary);
        trackDetailPlay.ToolTip(selected && isPlaying.Value ? "Приостановить" : "Воспроизвести");
        trackDetailWaveform.IsPlaying = selected && isPlaying.Value;
        trackDetailWaveform.Progress = selected && track.DurationSeconds > 0 ? likedPlaybackPosition / track.DurationSeconds : 0;
        trackDetailPosition.Text = FormatTime(selected ? likedPlaybackPosition : 0);
        var liked = likedIds.Contains(track.Id);
        TrackButtons.SetGlyph(trackDetailHeart, liked ? "heart-filled" : "heart", liked ? TrackButtons.OnSelected : TrackButtons.OnSurface);
        TrackButtons.SetTextColor(trackDetailLikeCount, liked ? TrackButtons.OnSelected : TrackButtons.OnSurface);
        TrackButtons.SetLiked(trackDetailLike, liked, TrackButtons.Glass);
        RefreshRepostState();
        trackDetailLike.ToolTip(liked ? "Убрать из понравившегося" : "Добавить в понравившееся");
        trackDetailLikeCount.Text = DetailCount(track.LikesCount);
        TrackButtons.SetAvailability(trackDetailLike, CanLikeTrack(track), IsLikePending(track));
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
        trackDetailCommentsTitle.Text = CommentHeading(state.Track.CommentCount ?? state.Comments.Comments.Length);
        trackDetailCommentsStatus.Text = state.CommentsLoading ? "Загружаем комментарии…" : state.CommentsError.Length > 0 ? state.CommentsError : state.Comments.Comments.Length == 0 ? "К этому треку ещё нет комментариев." : "";
        trackDetailCommentsStatus.IsVisible = trackDetailCommentsStatus.Text.Length > 0;
        var comments = state.Comments.Comments;
        var changed = renderedCommentsTrack != state.Track.Id || renderedCommentsDuration != state.Track.Duration || !comments.SequenceEqual(renderedComments);
        if (renderedCommentsTrack != state.Track.Id || renderedCommentsDuration != state.Track.Duration || !comments.Take(renderedComments.Length).SequenceEqual(renderedComments))
        {
            trackDetailComments.Clear(); renderedComments = []; renderedCommentsTrack = state.Track.Id;
            renderedCommentsDuration = state.Track.Duration;
        }
        // Keep existing rows and their decoded avatars when another page arrives.
        foreach (var comment in comments.Skip(renderedComments.Length)) trackDetailComments.Add(TrackCommentView(comment, state.Track));
        renderedComments = comments;
        if (changed) RenderTrackCommentMarkers();
        trackDetailMoreComments.IsVisible = !state.CommentsLoading && (state.Comments.NextHref != null || state.CommentsError.Length > 0);
        ((TextBlock)trackDetailMoreComments.Content!).Text = state.CommentsError.Length > 0 ? "Повторить загрузку" : "Ещё комментарии";
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
        "all-rights-reserved" => "Все права защищены",
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
        title.MouseEnter += () => title.Opacity = .72;
        title.MouseLeave += () => title.Opacity = 1;
        title.MouseDown += args =>
        {
            if (args.Button != MouseButton.Left || track() is not { } item) return;
            args.Handled = true;
            Run(() => OpenTrackPageAsync(item));
        };
    }
}
