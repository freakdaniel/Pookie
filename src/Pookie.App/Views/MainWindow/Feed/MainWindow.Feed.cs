using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.App.Playback;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record FeedState(FeedEntry[] Entries, string? Cursor, WebSession? Session, bool Loaded = false);
    private FeedState? feedState;
    private readonly ObservableValue<bool> feedReposts = new(true);
    private bool feedLoading;
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
        feedList = new ItemsControl().Items(Array.Empty<FeedEntry>(), entry => entry.Key).ItemHeight(256).VariableHeightPresenter()
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
        feedRetry = TrackButtons.Action(new TextBlock().Text("Попробовать снова").FontSize(13).SemiBold(),
            () => Run(() => LoadFeedAsync(failedFeedAppend)), TrackButtons.Glass, 36).Left();
        feedMore = TrackButtons.Action(new TextBlock().Text("Загрузить ещё").FontSize(13).SemiBold(),
            () => { failedPageCursor = null; emptyPageStreak = 0; Run(LoadNextVisiblePageAsync); }, TrackButtons.Glass, 36).Left();
        feedFilters = new StackPanel().Horizontal().Spacing(24).Children(
            SectionTab("Все публикации", feedReposts, true, () => SetFeedReposts(true)),
            SectionTab("Без репостов", feedReposts, false, () => SetFeedReposts(false)));
        var main = new Grid().Columns("*").Rows("*").Children(feedList, feedSkeleton.Top(),
            new StackPanel().Vertical().Spacing(16).Top().Children(feedEmpty, feedMore));
        feedColumns = new FeedColumns(main, feedSidebar, () => shellScroll.Bounds.Y + ShellChromeGlass.HeaderHeight + 16).Initialize();
        return new DockPanel().LastChildFill().Spacing(24).Children(feedFilters.DockTop(),
            new StackPanel().Vertical().Spacing(10).Children(feedNotice, feedRetry).DockTop(), feedColumns);
    }

    private Task OpenFeedAsync(bool force = false) => NavigateRouteAsync(new(Page.Feed), async _ =>
    {
        if (feedState?.Session != api.Session) feedState = null;
        feedError = "";
        RenderFeed();
        if (force || feedState?.Loaded != true) await LoadFeedAsync();
    });

    private async Task LoadFeedAsync(bool append = false)
    {
        if (page.Value != Page.Feed || feedLoading || append && feedState?.Cursor == null) return;
        if (!demo && (api.Session == null || me == null)) { RenderFeed(); return; }
        var generation = navigationGeneration; var session = api.Session; var token = loading!.Token;
        var cursor = append ? feedState?.Cursor : null;
        feedLoading = true; feedError = ""; RenderFeed();
        try
        {
            var result = demo ? new FeedPage(tracks.Select(track => new FeedEntry("demo:" + track.Id, LibraryItem.FromTrack(track), track.User, "track", track.CreatedAt)).ToArray(), null)
                : await api.GetFeedActivitiesAsync(cursor, token);
            if (disposed || token.IsCancellationRequested || page.Value != Page.Feed || generation != navigationGeneration || session != api.Session) return;
            var entries = (append ? feedState?.Entries ?? [] : Array.Empty<FeedEntry>()).Concat(result.Entries).DistinctBy(entry => entry.Key).ToArray();
            feedState = new(entries, result.NextHref == cursor ? null : result.NextHref, session, true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
        { if (generation == navigationGeneration && !disposed) { failedFeedAppend = append; feedError = FriendlyError(error).TrimEnd('.'); throw; } }
        finally
        {
            if (generation == navigationGeneration && !disposed) { feedLoading = false; RenderFeed(); }
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
        feedSkeleton.IsVisible = feedLoading && entries.Length == 0;
        feedSkeleton.SetActive(feedSkeleton.IsVisible);
        feedEmpty.IsVisible = !feedLoading && entries.Length == 0 && feedError.Length == 0;
        feedEmpty.Text = !demo && me == null ? "Войди в SoundCloud, чтобы увидеть публикации и репосты своих подписок" :
            feedState?.Entries.Length > 0 && !feedReposts.Value ? "Здесь пока только репосты — включи все публикации или загрузи более ранние записи" :
            "Здесь появятся новые треки и плейлисты твоих подписок";
        feedNotice.Text = feedError; feedNotice.IsVisible = feedRetry.IsVisible = feedError.Length > 0;
        feedRetry.IsEnabled = !feedLoading;
        feedMore.IsVisible = feedEmpty.IsVisible && feedState?.Cursor != null;
        RenderFeedSidebar();
        Run(() => EnsureCollectionActionsAsync(entries.Any(entry => entry.Item.Playlist != null)));
    }

    private async Task PlayFeedTrackAsync(SoundCloudTrack track)
    {
        var key = feedReposts.Value ? "feed" : "feed:originals";
        if (current?.Id == track.Id && playbackQueue.Context?.Key == key) { await ToggleAsync(); return; }
        CaptureQueueOrigin();
        var data = VisibleFeedEntries.Select(entry => entry.Item.Track).OfType<SoundCloudTrack>().DistinctBy(value => value.Id).ToArray();
        StartQueueContext(new(key, "Лента", "Сейчас играет из ленты", feedState?.Cursor,
            Kind: PlaybackContextKind.Feed), data.Length > 0 ? data : [track], track);
        await PlayAsync(track);
    }
    private async Task SeekFeedTrackAsync(SoundCloudTrack track, double fraction)
    {
        if (current?.Id != track.Id) await PlayFeedTrackAsync(track);
        await SeekLikedTrackAsync(track, fraction);
    }
}
