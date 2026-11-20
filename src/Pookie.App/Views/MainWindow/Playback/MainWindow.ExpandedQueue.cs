using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.App.Playback;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum QueueBlockKind { Track, Source, Upcoming, Context, Spacer }
    private sealed record QueueBlock(string Key, QueueBlockKind Kind, SoundCloudTrack? Track = null, string Title = "", string Subtitle = "", long? EntryId = null);
    private readonly PaginationItems expandedQueueSource = new(LoadingRowStyle.Compact, item => ((QueueBlock)item).Key);
    private readonly Dictionary<QueueRowMotion, QueueBlock?> expandedQueueBlocks = [];
    private readonly Dictionary<string, double> expandedQueuePositions = [];
    private QueueBlock[] expandedQueueData = [];
    private HashSet<string> expandedNewKeys = [];
    private long expandedQueueChangedAt;
    private ScrollViewer expandedQueueScroll = null!;
    private QueueRowAnimation expandedQueueAnimation = null!;
    private QueueEdgeFade expandedQueueFade = null!;
    private (string Key, double Y)? expandedQueueAnchor;
    private bool expandedQueueFollowing = true;
    private bool expandedQueueLayoutQueued;
    private string? expandedCurrentKey;
    private long expandedSourceVersion = -1;
    private bool expandedRowsNeedCleanup;
    private string queueOriginTitle = UiText.Get("queue.heading"), queueOriginKind = UiText.Get("player.nowPlaying");

    private void CaptureQueueOrigin(bool recent = false)
    {
        queueOriginTitle = page.Value switch
        {
            Page.LibraryCollection => librarySectionTitle.Value,
            Page.Library or Page.LibraryTracks => recent ? UiText.Get("history.recentlyPlayed") : UiText.Get("library.likedTracks"),
            Page.LibraryHistory => UiText.Get("history.listening.heading"),
            Page.Search => UiText.Format("navigation.search.query", navigationHistory[navigationIndex].Route.Search ?? query.Value),
            Page.Feed => UiText.Get("header.feed"), Page.Home => UiText.Get("header.home"),
            _ => UiText.Get("queue.heading")
        };
        var collection = navigationHistory[navigationIndex].Route.Item;
        queueOriginKind = page.Value switch
        {
            Page.LibraryCollection when collection?.User != null => UiText.Get("queue.source.artist"),
            Page.LibraryCollection when collection?.IsAlbum == true => UiText.Get("queue.source.fromAlbum"),
            Page.LibraryCollection => UiText.Get("queue.source.fromPlaylist"),
            Page.Search => UiText.Get("queue.source.search"),
            Page.Home => UiText.Get("queue.source.recommendations"),
            Page.Feed => UiText.Get("queue.source.feed"),
            Page.LibraryHistory => UiText.Get("queue.source.history"),
            _ => UiText.Get("queue.source.collection")
        };
    }

    private FrameworkElement ExpandedQueuePanel()
    {
        expandedQueue = new ItemsControl().ItemHeight(64).VariableHeightPresenter().Background(Color.Transparent)
            .BorderThickness(0).Padding(0).ItemPadding(new Thickness(0));
        expandedQueue.ItemsSource = expandedQueueSource.View;
        expandedQueueAnimation = new QueueRowAnimation(expandedQueue.InvalidateVisual);
        expandedQueue.ItemTemplate = new DelegateTemplate<QueueBlock>(context =>
        {
            CompactTrackRow row = null!;
            row = CreateCompactTrackRow(_ =>
                { if (row.EntryId is { } id) Run(() => PlayQueueEntryAsync(id)); }, PlayerSecondaryText, queueRow: true);
            AttachTrackQueueMenu(row.Root, () => row.Track, () => row.EntryId);
            var label = new TextBlock().FontSize(12).SemiBold().Foreground(PlayerSecondaryText);
            var title = new TextBlock().FontSize(22).Bold().Foreground(Color.White).TextTrimming(TextTrimming.CharacterEllipsis);
            var heading = new StackPanel().Vertical().Spacing(5).Margin(8, 26, 8, 18).Children(label, title);
            // Cache the stationary contents, not the translating wrapper. Text,
            // icons and artwork then move as one bitmap during queue edits.
            var root = new Grid().Columns("*").Rows("Auto").Children(row.Root, heading).Cached();
            var motion = new QueueRowMotion(expandedQueueAnimation).Content(root);
            expandedQueueRows[row.Root] = row; expandedQueueBlocks[motion] = null;
            context.Register("motion", motion); context.Register("row", row.Root);
            context.Register("heading", heading); context.Register("label", label); context.Register("title", title);
            return motion;
        }, (_, block, _, context) =>
        {
            var motion = context.Get<QueueRowMotion>("motion");
            expandedQueueBlocks[motion] = block;
            var spacer = block.Kind == QueueBlockKind.Spacer;
            motion.Height = spacer ? ExpandedQueueEdgeSpace : double.NaN;
            ((Grid)motion.Content!).IsVisible = !spacer;
            // Layout/binding refreshes for the same retained item must not stop
            // an in-flight translation (for example when its artwork arrives).
            if (motion.RetainedKey != block.Key)
                RetargetExpandedQueueRow(motion, block);
            motion.RetainedKey = block.Key;
            var row = expandedQueueRows[context.Get<Grid>("row")];
            row.Root.IsVisible = block.Kind == QueueBlockKind.Track;
            row.EntryId = block.EntryId;
            BindCompactTrackRow(row, block.Track);
            context.Get<StackPanel>("heading").IsVisible = block.Kind != QueueBlockKind.Track;
            context.Get<TextBlock>("label").Text = block.Subtitle;
            context.Get<TextBlock>("label").IsVisible = block.Subtitle.Length > 0;
            context.Get<TextBlock>("title").Text = block.Title;
            context.Get<TextBlock>("title").FontSize = block.Kind == QueueBlockKind.Source ? 22 : 16;
        }, (_, _, _, context) =>
        {
            // The presenter unbinds before *every* rebind, including the same
            // retained item. Clear genuinely recycled rows after layout settles.
            expandedQueueBlocks[context.Get<QueueRowMotion>("motion")] = null;
            expandedRowsNeedCleanup = true;
        });
        expandedQueueScroll = (ScrollViewer)expandedQueue.FindVisualChild<ScrollViewer>()!;
        ((IVisualTreeHost)expandedQueueScroll).VisitChildren(element =>
        {
            if (element is ScrollBar bar) { bar.Opacity = 0; bar.IsHitTestVisible = false; }
            return true;
        });
        smoothScrolls[expandedQueueScroll] = new SmoothScroll(expandedQueueScroll)
        {
            UserScrolling = () => { expandedQueueFollowing = false; expandedQueueAnchor = null; },
            // Carry an active wheel movement through the presenter's row-height corrections.
            RestoringAnchor = () => true
        };
        expandedQueueScroll.SizeChanged += size =>
        {
            if (size.NewSize.Height == size.OldSize.Height) return;
            foreach (var (motion, block) in expandedQueueBlocks)
                if (block?.Kind == QueueBlockKind.Spacer) motion.Height = ExpandedQueueEdgeSpace;
        };
        expandedQueueFade = new QueueEdgeFade(expandedBackdrop) { IsHitTestVisible = false };
        return new Grid().Columns("*").Rows("*").Children(
            new ScrollLayoutHost(RestoreExpandedQueueAnchor).Background(Color.Transparent).BorderThickness(0).Padding(0).Content(expandedQueue),
            expandedQueueFade);
    }

    private void ClearUnboundExpandedQueueRows()
    {
        if (!expandedRowsNeedCleanup) return;
        expandedRowsNeedCleanup = false;
        foreach (var (motion, block) in expandedQueueBlocks)
        {
            if (block != null || motion.RetainedKey == null) continue;
            motion.RetainedKey = null; motion.Retarget(null);
            BindCompactTrackRow(expandedQueueRows[(Grid)((Grid)motion.Content!).Children[0]], null);
        }
    }

    // Scrollable end space lets even the first and last track reach the middle.
    private double ExpandedQueueEdgeSpace => Math.Max(1, (expandedQueueScroll.ActualHeight - expandedQueue.ItemHeight) / 2);

    private void FollowExpandedQueue()
    {
        expandedQueueFollowing = true;
        expandedQueueAnchor = null;
        smoothScrolls[expandedQueueScroll].Stop();
        // A focused virtual row can retain its old bounds while it is off screen.
        if (expandedOpen && expandedPanelMode == PlayerPanel.Queue && expandedCurrentKey != null)
            expandedQueue.ScrollIntoView(Array.FindIndex(expandedQueueData, block => block.Key == expandedCurrentKey));
        expandedQueue.InvalidateMeasure();
    }

    private bool RestoreExpandedQueueAnchor()
    {
        if (!expandedOpen || expandedPanelMode != PlayerPanel.Queue || expandedQueueScroll.ViewportHeight <= 0) return false;
        var target = expandedQueueAnchor;
        if (expandedQueueFollowing && expandedCurrentKey != null)
            target = (expandedCurrentKey, expandedQueueScroll.ViewportHeight / 2);
        if (target is not { } anchor) return false;
        var row = expandedQueueBlocks.FirstOrDefault(pair => pair.Value?.Key == anchor.Key).Key;
        if (row == null)
        {
            var index = Array.FindIndex(expandedQueueData, block => block.Key == anchor.Key);
            if (index < 0) { expandedQueueAnchor = null; return false; }
            expandedQueue.ScrollIntoView(index);
            return true;
        }
        var y = expandedQueueFollowing ? row.Bounds.Center.Y : row.Bounds.Y;
        var delta = y - expandedQueueScroll.Bounds.Y - anchor.Y;
        var scale = Window.DpiScale;
        if (Math.Abs(delta) * scale <= .5) { expandedQueueAnchor = null; return false; }
        var before = expandedQueueScroll.VerticalOffset;
        // Row bounds use the pixel-snapped offset, even while wheel input is fractional.
        var renderedOffset = LayoutRounding.RoundToPixel(before, scale);
        var offset = LayoutRounding.RoundToPixel(renderedOffset + delta, scale);
        smoothScrolls[expandedQueueScroll].CorrectLayoutOffset(offset - before);
        if (expandedQueueScroll.VerticalOffset == before) { expandedQueueAnchor = null; return false; }
        return true;
    }

    private void RequestExpandedQueueLayout()
    {
        if (expandedQueueLayoutQueued) return;
        expandedQueueLayoutQueued = true;
        Window.FrameRendered += CompleteExpandedQueueLayout;
        Window.InvalidateVisual();
    }

    private void CompleteExpandedQueueLayout()
    {
        Window.FrameRendered -= CompleteExpandedQueueLayout;
        Application.Current.Dispatcher!.BeginInvoke(() =>
        {
            expandedQueueLayoutQueued = false;
            if (!disposed) expandedQueue.InvalidateMeasure();
        });
    }

    private bool QueueEntryIsNew(QueueBlock block) => expandedOpen && expandedPanelMode == PlayerPanel.Queue && panelReveal > .999 &&
        Environment.TickCount64 - expandedQueueChangedAt < 500 && expandedNewKeys.Contains(block.Key);

    private void RetargetExpandedQueueRow(QueueRowMotion motion, QueueBlock block)
    {
        motion.Retarget(expandedQueuePositions.TryGetValue(block.Key, out var y) ? y : null, QueueEntryIsNew(block));
    }

    private void RefreshExpandedQueue()
    {
        if (expandedQueue == null) return;
        var state = playbackQueue.Snapshot;
        expandedUpcoming = state.Upcoming.Select(entry => entry.Track).ToArray();
        static QueueBlock TrackBlock(PlaybackEntry entry) => new($"entry:{entry.EntryId}", QueueBlockKind.Track, entry.Track, EntryId: entry.EntryId);
        var next = new List<QueueBlock> { new("start-space", QueueBlockKind.Spacer) };
        next.AddRange(state.History.Select(TrackBlock));
        var manualCurrent = state.Current?.Origin == PlaybackEntryOrigin.Manual;
        var recommendedCurrent = state.Current?.Origin == PlaybackEntryOrigin.Recommendation;
        next.Add(new("source", QueueBlockKind.Source,
            Title: manualCurrent ? UiText.Get("player.nowPlaying") : UiText.Refresh(state.Context?.Title ?? queueOriginTitle),
            Subtitle: manualCurrent ? "" : recommendedCurrent ? UiText.Get("queue.source.related") : UiText.Refresh(state.Context?.Subtitle ?? queueOriginKind)));
        var currentBlock = state.Current is { } playing ? TrackBlock(playing) : null;
        if (currentBlock != null) next.Add(currentBlock);
        if (state.Recommendations.Count == 0 || state.ManualUpcoming.Count + state.Forward.Count + state.ContextUpcoming.Count > 0)
            next.Add(new("upcoming", QueueBlockKind.Upcoming,
                Title: state.ManualUpcoming.Count > 0 ? UiText.Get("queue.addedByYou") :
                    expandedUpcoming.Length > 0 ? UiText.Get("common.next") : UiText.Get("queue.empty.next")));
        next.AddRange(state.ManualUpcoming.Select(TrackBlock));
        if (state.ManualUpcoming.Count > 0 && state.ContextUpcoming.Count + state.Forward.Count > 0)
            next.Add(new("context", QueueBlockKind.Context, Title: UiText.Get("queue.heading")));
        next.AddRange(state.Forward.Select(TrackBlock));
        next.AddRange(state.ContextUpcoming.Select(TrackBlock));
        if (state.Recommendations.Count > 0)
        {
            next.Add(new("recommendations", QueueBlockKind.Context, Title: state.Context?.Kind == PlaybackContextKind.Radio ? UiText.Get("radio.upNext") : UiText.Get("queue.related.upNext")));
            next.AddRange(state.Recommendations.Select(TrackBlock));
        }
        next.Add(new("end-space", QueueBlockKind.Spacer));
        var data = next.ToArray();
        if (expandedQueueData.SequenceEqual(data)) return;
        // Resolving audio can replace track metadata without changing queue order.
        // Rebind the retained rows without restarting their ongoing movement.
        if (expandedQueueData.Select(block => block.Key).SequenceEqual(data.Select(block => block.Key)))
        {
            expandedQueueData = data;
            expandedQueueSource.SetData(data);
            RequestExpandedQueueLayout();
            return;
        }
        expandedQueuePositions.Clear();
        if (expandedOpen && expandedPanelMode == PlayerPanel.Queue && panelReveal > .999)
        {
            foreach (var (motion, block) in expandedQueueBlocks)
                if (block != null) expandedQueuePositions[block.Key] = motion.VisualY;
        }
        expandedQueueAnimation.Begin(460);
        var currentChanged = expandedSourceVersion != state.SourceVersion || expandedCurrentKey != currentBlock?.Key;
        if (!currentChanged && !expandedQueueFollowing && expandedQueueAnchor == null)
        {
            var visible = expandedQueueBlocks.Where(pair => pair.Value is { Kind: not QueueBlockKind.Spacer } &&
                pair.Key.Bounds.Bottom > expandedQueueScroll.Bounds.Y && pair.Key.Bounds.Y < expandedQueueScroll.Bounds.Bottom)
                .OrderBy(pair => pair.Key.Bounds.Y).FirstOrDefault();
            if (visible.Key != null) expandedQueueAnchor = (visible.Value!.Key, visible.Key.Bounds.Y - expandedQueueScroll.Bounds.Y);
        }
        expandedNewKeys = data.Select(block => block.Key).Except(expandedQueueData.Select(block => block.Key)).ToHashSet();
        expandedQueueChangedAt = Environment.TickCount64;
        expandedCurrentKey = currentBlock?.Key; expandedSourceVersion = state.SourceVersion;
        expandedQueueData = data;
        expandedQueueSource.SetData(data);
        if (currentChanged) FollowExpandedQueue();
        foreach (var (motion, block) in expandedQueueBlocks)
            if (block != null) RetargetExpandedQueueRow(motion, block);
        expandedQueue.InvalidateMeasure();
        RequestExpandedQueueLayout();
    }
}
