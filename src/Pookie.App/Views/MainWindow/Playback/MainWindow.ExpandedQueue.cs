using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using Pookie.App.Playback;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum QueueBlockKind { Track, Source, Upcoming, Context }
    private sealed record QueueBlock(string Key, QueueBlockKind Kind, SoundCloudTrack? Track = null, string Title = "", string Subtitle = "", long? EntryId = null);
    private readonly PaginationItems expandedQueueSource = new(LoadingRowStyle.Compact, item => ((QueueBlock)item).Key);
    private readonly Dictionary<QueueRowMotion, QueueBlock?> expandedQueueBlocks = [];
    private readonly Dictionary<string, double> expandedQueuePositions = [];
    private QueueBlock[] expandedQueueData = [];
    private HashSet<string> expandedNewKeys = [];
    private long expandedQueueChangedAt;
    private ScrollViewer expandedQueueScroll = null!;
    private QueueEdgeFade expandedQueueFade = null!;
    private (string Key, double Y)? expandedQueueAnchor;
    private bool expandedQueueStartPending = true;
    private long? expandedCurrentEntry;
    private long expandedSourceVersion = -1;
    private string queueOriginTitle = "Очередь воспроизведения", queueOriginKind = "Сейчас играет";

    private void CaptureQueueOrigin(bool recent = false)
    {
        expandedQueueStartPending = true;
        expandedQueueAnchor = null;
        queueOriginTitle = page.Value switch
        {
            Page.LibraryCollection => librarySectionTitle.Value,
            Page.Library or Page.LibraryTracks => recent ? "Недавно прослушанное" : "Понравившиеся треки",
            Page.LibraryHistory => "История прослушивания",
            Page.Search => $"Поиск «{navigationHistory[navigationIndex].Route.Search ?? query.Value}»",
            Page.Feed => "Лента", Page.Home => "На твоей волне",
            _ => "Очередь воспроизведения"
        };
        var collection = navigationHistory[navigationIndex].Route.Item;
        queueOriginKind = page.Value switch
        {
            Page.LibraryCollection when collection?.User != null => "Сейчас играет у исполнителя",
            Page.LibraryCollection when collection?.IsAlbum == true => "Сейчас играет из альбома",
            Page.LibraryCollection => "Сейчас играет из плейлиста",
            Page.Search => "Сейчас играет из результатов поиска",
            Page.Home => "Сейчас играет из рекомендаций",
            Page.Feed => "Сейчас играет из ленты",
            Page.LibraryHistory => "Сейчас играет из истории",
            _ => "Сейчас играет из подборки"
        };
    }

    private FrameworkElement ExpandedQueuePanel()
    {
        expandedQueue = new ItemsControl().ItemHeight(64).VariableHeightPresenter().Background(Color.Transparent)
            .BorderThickness(0).Padding(0).ItemPadding(new Thickness(0));
        expandedQueue.ItemsSource = expandedQueueSource.View;
        expandedQueue.ItemTemplate = new DelegateTemplate<QueueBlock>(context =>
        {
            CompactTrackRow row = null!;
            row = CreateCompactTrackRow(_ =>
                { if (row.EntryId is { } id) Run(() => PlayQueueEntryAsync(id)); }, PlayerSecondaryText, queueRow: true);
            AttachTrackQueueMenu(row.Root, () => row.Track, () => row.EntryId);
            var label = new TextBlock().FontSize(12).SemiBold().Foreground(PlayerSecondaryText);
            var title = new TextBlock().FontSize(22).Bold().Foreground(Color.White).TextTrimming(TextTrimming.CharacterEllipsis);
            var heading = new StackPanel().Vertical().Spacing(5).Margin(8, 26, 8, 18).Children(label, title);
            var root = new Grid().Columns("*").Rows("Auto").Children(row.Root, heading);
            var motion = new QueueRowMotion().Content(root);
            expandedQueueRows[row.Root] = row; expandedQueueBlocks[motion] = null;
            context.Register("motion", motion); context.Register("row", row.Root);
            context.Register("heading", heading); context.Register("label", label); context.Register("title", title);
            return motion;
        }, (_, block, _, context) =>
        {
            var motion = context.Get<QueueRowMotion>("motion");
            expandedQueueBlocks[motion] = block;
            // Layout/binding refreshes for the same retained item must not stop
            // an in-flight translation (for example when its artwork arrives).
            if (motion.RetainedKey != block.Key)
                motion.Retarget(expandedQueuePositions.TryGetValue(block.Key, out var y) ? y : null, QueueEntryIsNew(block));
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
        });
        expandedQueueScroll = (ScrollViewer)expandedQueue.FindVisualChild<ScrollViewer>()!;
        ((IVisualTreeHost)expandedQueueScroll).VisitChildren(element =>
        {
            if (element is ScrollBar bar) { bar.Opacity = 0; bar.IsHitTestVisible = false; }
            return true;
        });
        smoothScrolls[expandedQueueScroll] = new SmoothScroll(expandedQueueScroll);
        expandedQueueFade = new QueueEdgeFade(expandedBackdrop) { IsHitTestVisible = false };
        return new Grid().Columns("*").Rows("*").Children(
            new ScrollLayoutHost(RestoreExpandedQueueAnchor).Background(Color.Transparent).BorderThickness(0).Padding(0).Content(expandedQueue),
            expandedQueueFade);
    }

    private void ClearUnboundExpandedQueueRows()
    {
        foreach (var (motion, block) in expandedQueueBlocks)
        {
            if (block != null || motion.RetainedKey == null) continue;
            motion.RetainedKey = null; motion.Retarget(null);
            BindCompactTrackRow(expandedQueueRows[(Grid)((Grid)motion.Content!).Children[0]], null);
        }
    }

    private bool RestoreExpandedQueueAnchor()
    {
        if (expandedQueueStartPending && expandedQueueData.Length > 0)
        {
            expandedQueueStartPending = false;
            expandedQueueAnchor = ("source", 0);
            expandedQueue.ScrollIntoView(Array.FindIndex(expandedQueueData, block => block.Kind == QueueBlockKind.Source));
            return true;
        }
        if (expandedQueueAnchor is not { } anchor) return false;
        var row = expandedQueueBlocks.FirstOrDefault(pair => pair.Value?.Key == anchor.Key).Key;
        if (row == null)
        {
            var index = Array.FindIndex(expandedQueueData, block => block.Key == anchor.Key);
            if (index < 0) { expandedQueueAnchor = null; return false; }
            expandedQueue.ScrollIntoView(index);
            return true;
        }
        var delta = row.Bounds.Y - expandedQueueScroll.Bounds.Y - anchor.Y;
        if (Math.Abs(delta) < .5) { expandedQueueAnchor = null; return false; }
        var before = expandedQueueScroll.VerticalOffset;
        smoothScrolls[expandedQueueScroll].CorrectLayoutOffset(delta);
        if (Math.Abs(expandedQueueScroll.VerticalOffset - before) < .5) { expandedQueueAnchor = null; return false; }
        return true;
    }

    private bool QueueEntryIsNew(QueueBlock block) => expandedOpen && expandedPanelMode == PlayerPanel.Queue && panelReveal > .999 &&
        Environment.TickCount64 - expandedQueueChangedAt < 500 && expandedNewKeys.Contains(block.Key);

    private void RefreshExpandedQueue()
    {
        if (expandedQueue == null) return;
        var state = playbackQueue.Snapshot;
        expandedUpcoming = state.Upcoming.Select(entry => entry.Track).ToArray();
        static QueueBlock TrackBlock(PlaybackEntry entry) => new($"entry:{entry.EntryId}", QueueBlockKind.Track, entry.Track, EntryId: entry.EntryId);
        var next = state.History.Select(TrackBlock).ToList();
        var manualCurrent = state.Current?.Origin == PlaybackEntryOrigin.Manual;
        next.Add(new("source", QueueBlockKind.Source,
            Title: manualCurrent ? "Сейчас играет" : state.Context?.Title ?? queueOriginTitle,
            Subtitle: manualCurrent ? "" : state.Context?.Subtitle ?? queueOriginKind));
        if (state.Current is { } playing) next.Add(TrackBlock(playing));
        next.Add(new("upcoming", QueueBlockKind.Upcoming,
            Title: expandedUpcoming.Length > 0 ? "Далее" : "Следующих треков пока нет"));
        next.AddRange(state.ManualUpcoming.Select(TrackBlock));
        if (state.ManualUpcoming.Count > 0 && state.ContextUpcoming.Count + state.Forward.Count > 0)
            next.Add(new("context", QueueBlockKind.Context, Title: "Очередь воспроизведения"));
        next.AddRange(state.Forward.Select(TrackBlock));
        next.AddRange(state.ContextUpcoming.Select(TrackBlock));
        var data = next.ToArray();
        if (expandedQueueData.SequenceEqual(data)) return;
        expandedQueuePositions.Clear();
        if (expandedOpen && expandedPanelMode == PlayerPanel.Queue && panelReveal > .999)
        {
            foreach (var (motion, block) in expandedQueueBlocks)
                if (block != null) expandedQueuePositions[block.Key] = motion.VisualY;
        }
        var followCurrent = expandedQueueStartPending || expandedCurrentEntry != state.Current?.EntryId || expandedSourceVersion != state.SourceVersion;
        if (followCurrent)
        {
            // Advancing playback follows the source heading. Appending a page or
            // editing upcoming entries instead preserves the user's viewport.
            smoothScrolls[expandedQueueScroll].Stop();
            expandedQueueAnchor = ("source", 0);
        }
        else
        {
            var visible = expandedQueueBlocks.Where(pair => pair.Value != null && pair.Key.Bounds.Bottom > expandedQueueScroll.Bounds.Y)
                .OrderBy(pair => pair.Key.Bounds.Y).FirstOrDefault();
            if (visible.Key != null) expandedQueueAnchor = (visible.Value!.Key, visible.Key.Bounds.Y - expandedQueueScroll.Bounds.Y);
        }
        expandedNewKeys = data.Select(block => block.Key).Except(expandedQueueData.Select(block => block.Key)).ToHashSet();
        expandedQueueChangedAt = Environment.TickCount64;
        expandedCurrentEntry = state.Current?.EntryId; expandedSourceVersion = state.SourceVersion;
        expandedQueueData = data;
        expandedQueueSource.SetData(data);
        if (followCurrent) expandedQueue.ScrollIntoView(Array.FindIndex(data, block => block.Kind == QueueBlockKind.Source));
        foreach (var (motion, block) in expandedQueueBlocks)
            if (block != null) motion.Retarget(expandedQueuePositions.TryGetValue(block.Key, out var y) ? y : null, QueueEntryIsNew(block));
        expandedQueue.InvalidateMeasure();
    }
}
