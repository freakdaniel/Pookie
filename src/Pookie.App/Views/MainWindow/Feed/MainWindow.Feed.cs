using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.App.Playback;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record FeedState(FeedEntry[] Entries, string? Cursor, WebSession? Session, bool Loaded = false,
        int Pages = 1, DateTimeOffset Checked = default);
    private FeedState? feedState;
    private readonly ObservableValue<bool> feedReposts = new(true);
    private bool feedLoading;
    private long feedFollowingRevision;
    private bool failedFeedAppend;
    private string feedError = "";
    private ItemsControl feedList = null!;
    private StackPanel feedSidebar = null!;
    private LibrarySkeleton feedSkeleton = null!;
    private TextBlock feedEmpty = null!, feedNotice = null!;
    private Button feedRetry = null!, feedMore = null!;
    private FeedColumns feedColumns = null!;
    private StackPanel feedFilters = null!;

    private FrameworkElement FeedPageView()
    {
        // Both publication types have a 28px byline, 12px gap and 184px body.
        feedList = new ItemsControl().Items(Array.Empty<FeedEntry>(), entry => entry.Key).ItemHeight(256).FixedHeightPresenter()
            .Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0, 0, 0, 32));
        feedList.ItemTemplate = new DelegateTemplate<FeedEntry>(context =>
        {
            var host = new StackPanel().Vertical().Spacing(12); context.Register("host", host); return host;
        }, (_, entry, _, context) => BindFeedPublication(context.Get<StackPanel>("host"), entry),
        (_, _, _, context) => ClearFeedPublication(context.Get<StackPanel>("host")));
        feedSidebar = new StackPanel().Vertical().Spacing(28);
        feedSkeleton = new LibrarySkeleton().Height(600);
        feedSkeleton.ConfigureLoadingRow(likedArtworkSize, libraryColumns, LoadingRowStyle.Waveform);
        feedEmpty = new TextBlock().FontSize(15).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxWidth(560).Top();
        feedNotice = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        feedRetry = TrackButtons.Action(new TextBlock().Text(UiText.Get("common.tryAgain")).FontSize(13).SemiBold(),
            () => Run(() => LoadFeedAsync(failedFeedAppend)), TrackButtons.Glass, 36).Left();
        feedMore = TrackButtons.Action(new TextBlock().Text(UiText.Get("common.loadMore")).FontSize(13).SemiBold(),
            () => { failedPageCursor = null; emptyPageStreak = 0; Run(LoadNextVisiblePageAsync); }, TrackButtons.Glass, 36).Left();
        feedFilters = new StackPanel().Horizontal().Spacing(24).Children(
            SectionTab(UiText.Get("feed.filter.allPosts"), feedReposts, true, () => SetFeedReposts(true)),
            SectionTab(UiText.Get("feed.hideReposts"), feedReposts, false, () => SetFeedReposts(false)));
        var main = new Grid().Columns("*").Rows("*").Children(feedList, feedSkeleton.Top(),
            new StackPanel().Vertical().Spacing(16).Top().Children(feedEmpty, feedMore));
        feedColumns = new FeedColumns(main, feedSidebar, () => shellScroll.Bounds.Y + ShellHeader.HeaderHeight + 16).Initialize();
        return new DockPanel().LastChildFill().Spacing(24).Children(feedFilters.DockTop(),
            new StackPanel().Vertical().Spacing(10).Children(feedNotice, feedRetry).DockTop(), feedColumns);
    }

    private Task OpenFeedAsync(bool force = false) => NavigateRouteAsync(new(Page.Feed), async _ =>
    {
        if (feedState?.Session != api.Session) feedState = null;
        feedError = "";
        RenderFeed();
        if (force || feedState?.Loaded != true || DateTimeOffset.UtcNow - feedState.Checked >= PageFreshness) await LoadFeedAsync();
    });

    private async Task LoadFeedAsync(bool append = false)
    {
        if (page.Value != Page.Feed || feedLoading || append && feedState?.Cursor == null) return;
        if (!demo && (api.Session == null || me == null)) { RenderFeed(); return; }
        var generation = navigationGeneration; var session = api.Session; var token = loading!.Token;
        var followingRevision = feedFollowingRevision;
        var cursor = append ? feedState?.Cursor : null;
        feedLoading = true; feedError = ""; RenderFeed();
        try
        {
            var entries = new List<FeedEntry>();
            if (append) entries.AddRange(feedState!.Entries);
            var pages = append ? feedState!.Pages : 0;
            var limit = append ? 1 : Math.Max(1, feedState?.Pages ?? 1);
            var cursors = new HashSet<string>();
            if (cursor != null) cursors.Add(cursor);
            for (int i = 0; i < limit; i++)
            {
                var result = demo ? new FeedPage(tracks.Select(track => new FeedEntry("demo:" + track.Id, LibraryItem.FromTrack(track), track.User, "track", track.CreatedAt)).ToArray(), null)
                    : await api.GetFeedActivitiesAsync(cursor, token);
                if (disposed || token.IsCancellationRequested || page.Value != Page.Feed || generation != navigationGeneration || session != api.Session || followingRevision != feedFollowingRevision) return;
                entries.AddRange(result.Entries); pages++;
                cursor = result.NextHref;
                if (cursor == null || !cursors.Add(cursor)) { cursor = null; break; }
            }
            if (!append) { loadedPageCursors.Clear(); emptyPageStreak = 0; failedPageCursor = null; }
            feedState = new(entries.DistinctBy(entry => entry.Key).ToArray(), cursor, session, true, pages,
                append ? feedState!.Checked : DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        { if (generation == navigationGeneration && !disposed) { failedFeedAppend = append; feedError = FriendlyError(error).TrimEnd('.'); throw; } }
        finally
        {
            if (generation == navigationGeneration && !disposed)
            {
                feedLoading = false;
                RenderFeed();
                if (followingRevision != feedFollowingRevision) Run(() => LoadFeedAsync());
            }
        }
    }

    private FeedEntry[] VisibleFeedEntries => (feedState?.Entries ?? []).Where(entry => feedReposts.Value || !entry.IsRepost).ToArray();
    private void SetFeedReposts(bool show)
    {
        feedReposts.Value = show;
        failedPageCursor = null; emptyPageStreak = 0;
        shellScroll.SetScrollOffsets(0, 0);
        RenderFeed();
    }
    private void RenderFeed()
    {
        if (page.Value != Page.Feed) return;
        var entries = VisibleFeedEntries;
        SetPageItems(feedList, entries, LoadingRowStyle.Waveform);
        feedSkeleton.IsVisible = feedLoading && feedState?.Loaded != true && entries.Length == 0;
        feedSkeleton.SetActive(feedSkeleton.IsVisible);
        feedEmpty.IsVisible = (!feedLoading || feedState?.Loaded == true) && entries.Length == 0 && feedError.Length == 0;
        feedEmpty.Text = !demo && me == null ? UiText.Get("feed.signIn.description") :
            feedState?.Entries.Length > 0 && !feedReposts.Value ? UiText.Get("feed.empty.onlyReposts") :
            UiText.Get("feed.empty.description");
        feedNotice.Text = feedError; feedNotice.IsVisible = feedRetry.IsVisible = feedError.Length > 0;
        feedRetry.IsEnabled = !feedLoading;
        feedMore.IsVisible = feedEmpty.IsVisible && feedState?.Cursor != null;
        RenderFeedSidebar();
        Run(() => EnsureCollectionActionsAsync(entries.Any(entry => entry.Item.Playlist != null)));
    }

    private async Task PlayFeedTrackAsync(SoundCloudTrack track)
    {
        var key = feedReposts.Value ? "feed" : "feed:originals";
        if (IsCurrentTrack(track)) { await ToggleAsync(); return; }
        CaptureQueueOrigin();
        var data = VisibleFeedEntries.Select(entry => entry.Item.Track).OfType<SoundCloudTrack>().DistinctBy(value => value.Id).ToArray();
        StartQueueContext(new(key, UiText.Get("header.feed"), UiText.Get("queue.source.feed"), feedState?.Cursor,
            Kind: PlaybackContextKind.Feed), data.Length > 0 ? data : [track], track);
        await PlayAsync(track);
    }
    private async Task SeekFeedTrackAsync(SoundCloudTrack track, double fraction)
    {
        if (current?.Id != track.Id) await PlayFeedTrackAsync(track);
        await SeekLikedTrackAsync(track, fraction);
    }
}
