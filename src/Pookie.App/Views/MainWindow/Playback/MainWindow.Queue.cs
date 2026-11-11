using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly PlaybackQueue playbackQueue = new();
    private readonly QueueContinuation queueContinuation = new();
    private PlaybackQueueLoader queueLoader = null!;
    private readonly PaginationItems popupQueueSource = new(LoadingRowStyle.Compact, item => ((QueueBlock)item).Key);
    private readonly Dictionary<QueueRowMotion, QueueBlock?> popupQueueBlocks = [];
    private readonly Dictionary<string, double> popupQueuePositions = [];
    private QueueBlock[] popupQueueData = [];
    private HashSet<string> popupQueueEntering = [];
    private QueueRowAnimation popupQueueAnimation = null!;
    private long popupQueueChangedAt;
    private readonly ObservableValue<bool> repeatActive = new(false), repeatTrack = new(false);

    private void InitializePlaybackQueue()
    {
        playbackQueue.SetShuffle(shuffle.Value);
        playbackQueue.SetAutoContinue(preferences.AutoContinue);
        queueLoader = new(playbackQueue, async (context, cursor, token) =>
        {
            if (context.Kind == PlaybackContextKind.Feed)
            {
                var feed = await api.GetFeedActivitiesAsync(cursor, token);
                var ids = playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).ToHashSet();
                return new(feed.Entries.Where(entry => context.Key != "feed:originals" || !entry.IsRepost).Select(entry => entry.Item.Track)
                    .OfType<SoundCloudTrack>().Where(track => ids.Add(track.Id)).ToArray(), feed.NextHref);
            }
            if (context.LibraryCursor)
            {
                var page = await api.GetLibraryNextAsync(cursor, token);
                return new(page.Items.Where(item => item.Track != null).Select(item => item.Track!).ToArray(), page.NextHref);
            }
            return await api.GetNextPageAsync(cursor, token);
        }, () => RefreshQueue(), lifetime.Token, GetQueueContinuationAsync);
        shuffle.Changed += () => { playbackQueue.SetShuffle(shuffle.Value); RefreshQueue(); UpdateSystemMedia(); };
        RefreshRepeatState();
    }

    private void SetQueue(SoundCloudTrack first, int? selectedIndex = null)
    {
        CaptureQueueOrigin();
        var route = navigationHistory[navigationIndex].Route;
        var data = page.Value is Page.Track or Page.Search ? [first] :
            page.Value == Page.LibraryCollection ? activeCollection?.Items.Where(item => item.Track != null).Select(item => item.Track!).ToArray() ?? [first] :
            page.Value is Page.Library or Page.LibraryTracks ? libraryLikes?.Tracks ?? tracks.ToArray() : tracks.ToArray();
        var cursor = page.Value is Page.Search or Page.Track ? null : page.Value == Page.LibraryCollection ? activeCollection?.NextHref :
            page.Value is Page.Library or Page.LibraryTracks ? libraryLikes?.NextHref ?? nextHref : nextHref;
        if (!data.Any(track => track.Id == first.Id)) data = [.. data, first];
        var key = page.Value is Page.Search or Page.Track ? $"track:{first.Id}" : route.Item?.Key ?? (page.Value is Page.Library or Page.LibraryTracks ? "likes" : page.Value.ToString());
        var kind = page.Value switch
        {
            Page.Library or Page.LibraryTracks => PlaybackContextKind.Likes,
            Page.LibraryCollection when route.Item?.User != null => PlaybackContextKind.Artist,
            Page.LibraryCollection when route.Item?.IsAlbum == true => PlaybackContextKind.Album,
            Page.LibraryCollection => PlaybackContextKind.Playlist,
            Page.Feed => PlaybackContextKind.Feed,
            Page.Home => PlaybackContextKind.Recommendations,
            _ => PlaybackContextKind.Track
        };
        if (page.Value is Page.Search or Page.Track) { queueOriginTitle = first.Title; queueOriginKind = "Сейчас играет отдельный трек"; }
        if (page.Value == Page.LibraryCollection && selectedIndex is { } cardIndex && activeCollection != null)
            selectedIndex = activeCollection.Items.Take(cardIndex).Count(item => item.Track != null);
        StartQueueContext(new(key, queueOriginTitle, queueOriginKind, cursor, Kind: kind,
            ResourceUrn: page.Value == Page.LibraryCollection ? PlaybackHistoryContextUrn(route.Item) : null), data, first, selectedIndex);
    }

    private void StartQueueContext(PlaybackContext context, SoundCloudTrack[] data, SoundCloudTrack first, int? selectedIndex = null, bool preserveManual = false)
    {
        var index = selectedIndex is { } position && position >= 0 && position < data.Length && data[position].Id == first.Id ? position :
            Array.FindIndex(data, track => ReferenceEquals(track, first));
        if (index < 0) index = Array.FindIndex(data, track => track.Id == first.Id);
        if (index < 0) { data = [.. data, first]; index = data.Length - 1; }
        playbackQueue.StartContext(context, data, index, preserveManual);
        queueLoader.ContextChanged(); RefreshQueue();
    }

    private PlaybackEntry SelectPlaybackTrack(SoundCloudTrack track)
    {
        if (playbackQueue.Current?.Track.Id == track.Id) return playbackQueue.Current;
        var entry = playbackQueue.Snapshot.Upcoming.FirstOrDefault(item => item.Track.Id == track.Id);
        if (entry != null) return playbackQueue.Select(entry.EntryId)!;
        // Direct selection outside the current source must never copy the page
        // that happens to be open while a stream is being prepared.
        StartQueueContext(new($"track:{track.Id}", track.Title, "Сейчас играет отдельный трек"), [track], track);
        return playbackQueue.Current!;
    }

    private async Task PlayQueueEntryAsync(long entryId)
    {
        // The selected row is recycled when the queue advances. Keep focus in
        // its persistent card so focus-based dismissal does not close the queue.
        if (queueOpen.Value)
            foreach (var menu in shellMenus.Where(menu => ReferenceEquals(menu.State, queueOpen))) menu.Layer.Card.Focus();
        if (playbackQueue.Current?.EntryId == entryId) { await ToggleAsync(); return; }
        var entry = playbackQueue.Select(entryId);
        if (entry != null) await PlayAsync(entry.Track);
    }

    private void RefreshQueue()
    {
        if (queueList == null) return;
        var state = playbackQueue.Snapshot;
        static QueueBlock TrackBlock(PlaybackEntry entry) => new($"entry:{entry.EntryId}", QueueBlockKind.Track, entry.Track, EntryId: entry.EntryId);
        var data = new List<QueueBlock>();
        if (state.Current is { } selected)
        {
            data.Add(new("now", QueueBlockKind.Upcoming, Title: "Сейчас играет"));
            data.Add(TrackBlock(selected));
        }
        if (state.ManualUpcoming.Count > 0) data.Add(new("manual", QueueBlockKind.Upcoming, Title: "Добавлено тобой"));
        data.AddRange(state.ManualUpcoming.Select(TrackBlock));
        if (state.Forward.Count + state.ContextUpcoming.Count > 0)
            data.Add(new("next", QueueBlockKind.Context, Title: "Далее"));
        data.AddRange(state.Forward.Concat(state.ContextUpcoming).Select(TrackBlock));
        if (state.Recommendations.Count > 0)
        {
            data.Add(new("recommendations", QueueBlockKind.Context, Title: state.Context?.Kind == PlaybackContextKind.Radio ? "Далее на радио" : "Похожие дальше"));
            data.AddRange(state.Recommendations.Select(TrackBlock));
        }
        RefreshPopupQueue(data.ToArray());
        queueStatus.Value = state.Context?.Title ?? "Очередь воспроизведения";
        RefreshExpandedQueue(); UpdateSystemMedia();
    }

    private void RefreshPopupQueue(QueueBlock[] data)
    {
        if (popupQueueData.SequenceEqual(data)) return;
        if (popupQueueData.Select(block => block.Key).SequenceEqual(data.Select(block => block.Key)))
        { popupQueueData = data; popupQueueSource.SetData(data); return; }
        popupQueuePositions.Clear();
        if (queueOpen.Value)
            foreach (var (row, block) in popupQueueBlocks)
                if (block != null) popupQueuePositions[block.Key] = row.VisualY;
        popupQueueAnimation.Begin(280);
        popupQueueEntering = data.Select(block => block.Key).Except(popupQueueData.Select(block => block.Key)).ToHashSet();
        popupQueueChangedAt = Environment.TickCount64;
        popupQueueData = data; popupQueueSource.SetData(data);
        foreach (var (row, block) in popupQueueBlocks)
            if (block != null) RetargetPopupQueueRow(row, block);
    }

    private void RetargetPopupQueueRow(QueueRowMotion row, QueueBlock block) =>
        row.Retarget(popupQueuePositions.TryGetValue(block.Key, out var y) ? y : null,
            queueOpen.Value &&
            Environment.TickCount64 - popupQueueChangedAt < 500 && popupQueueEntering.Contains(block.Key));

    private async Task SkipAsync(int offset, bool naturalEnd = false)
    {
        if (playbackQueue.Current == null && !playbackQueue.Snapshot.CanNext) { await ToggleAsync(); return; }
        if (offset < 0)
        {
            if (progress.Value >= 3 && audioReady)
            {
                progress.Value = 0; FlushSeek(); return;
            }
            var previous = playbackQueue.Previous();
            if (previous != null) await PlayAsync(previous.Track);
            return;
        }
        // Never wrap past an unloaded page. The loading token belongs to this
        // context and survives navigation away from its originating screen.
        var sourceVersion = playbackQueue.SourceVersion;
        var selectionGeneration = playGeneration;
        for (var attempt = 0; attempt < 4 && !playbackQueue.Snapshot.Upcoming.Any() && playbackQueue.Context?.NextHref != null; attempt++)
        {
            if (naturalEnd && playbackQueue.Repeat == RepeatMode.Track) break;
            await queueLoader.EnsureAheadAsync(force: true);
            if (disposed || sourceVersion != playbackQueue.SourceVersion || selectionGeneration != playGeneration) return;
        }
        for (var attempt = 0; attempt < 3 && !playbackQueue.Snapshot.Upcoming.Any() &&
            playbackQueue.Snapshot.CanLoadRecommendations && !(naturalEnd && playbackQueue.Repeat == RepeatMode.Track); attempt++)
        {
            await queueLoader.EnsureAheadAsync(force: true);
            if (disposed || sourceVersion != playbackQueue.SourceVersion || selectionGeneration != playGeneration) return;
        }
        var next = playbackQueue.Next(naturalEnd);
        if (next != null) await PlayAsync(next.Track, queueAdvance: true);
        else EndPlayback();
    }

    private void EndPlayback()
    {
        ++playGeneration; playLoading?.Cancel(); CancelSeek(); if (player != null) Run(player.StopAsync);
        audioReady = audioPreparing = paused = false;
        isPlaying.Value = playbackLoading.Value = false;
        presence?.Clear(); RefreshLikedPlayback(); RefreshQueue();
    }

    private void CycleRepeat()
    {
        playbackQueue.SetRepeat((RepeatMode)(((int)playbackQueue.Repeat + 1) % 3));
        RefreshRepeatState(); RefreshQueue(); queueLoader.PolicyChanged(); configurationTimer.Stop(); configurationTimer.Start();
        Run(() => queueLoader.EnsureAheadAsync());
    }
    private void RefreshRepeatState()
    {
        repeatActive.Value = playbackQueue.Repeat != RepeatMode.Off;
        repeatTrack.Value = playbackQueue.Repeat == RepeatMode.Track;
    }
    private FrameworkElement RepeatIcon(int size) => new Grid().Columns("*").Rows("*").Children(
        Icons.View("arrows-clockwise", size, Color.White).Center(),
        new TextBlock().Text("1").FontSize(8).Bold().Foreground(Color.White).Center().BindIsVisible(repeatTrack));

    private void EnqueueTrack(SoundCloudTrack track, bool next)
    {
        playbackQueue.Enqueue(track, next); RefreshQueue();
        Run(() => queueLoader.EnsureAheadAsync());
    }
    private void ChangeManualQueue(Action change) { change(); RefreshQueue(); }

    private AnimatedContextMenu AttachTrackQueueMenu(FrameworkElement root, Func<SoundCloudTrack?> track, Func<long?>? entryId = null, bool extraActions = false)
    {
        var menu = PopupAppearance.CreateMenu(MenuPlacement.Below);
        void Item(string id, string label, string icon, Action execute, Func<bool> enabled)
        {
            var command = PopupAppearance.MenuCommand("pookie.queue." + id, label, icon);
            root.Commands.Register(command, execute, enabled);
            menu.Item(command);
        }
        Item("open", "Открыть страницу трека", "music-notes", () => { if (track() is { } item) Run(() => OpenTrackPageAsync(item)); }, () => track() != null);
        Item("radio", "Запустить радио по треку", "broadcast", () => { if (track() is { } item) Run(() => StartTrackRadioAsync(item)); },
            () => track() is { } item && CanStartTrackRadio(item));
        Item("next", "Воспроизвести следующим", "skip-forward", () => { if (track() is { } item) EnqueueTrack(item, true); }, () => track() != null);
        Item("add", "Добавить в очередь", "queue", () => { if (track() is { } item) EnqueueTrack(item, false); }, () => track() != null);
        Item("playlist", "Добавить в плейлист", "playlist-plus", () => { if (track() is { } item) Run(() => OpenPlaylistPickerAsync(item)); }, () => track() != null && me != null && !demo);
        Item("share", "Поделиться", "arrow-square-out", () => { if (track() is { } item) ShareTrack(item); }, () => track() != null);
        if (extraActions)
        {
            Item("copy", "Скопировать ссылку", "copy", () => { if (track() is { } item) CopyTrackLink(item); }, () => track() != null);
            Item("repost", "Сделать репост", "repeat", () => { if (track() is { } item) Run(() => ToggleTrackRepostAsync(item)); },
                () => track() is { } item && CanRepostTrack(item));
        }
        if (entryId != null)
        {
            int Index() => playbackQueue.Snapshot.ManualUpcoming.ToList().FindIndex(entry => entry.EntryId == entryId());
            Item("up", "Выше в очереди", "caret-up", () => ChangeManualQueue(() => playbackQueue.MoveManual(entryId()!.Value, Index() - 1)), () => Index() > 0);
            Item("down", "Ниже в очереди", "caret-down", () => ChangeManualQueue(() => playbackQueue.MoveManual(entryId()!.Value, Index() + 1)),
                () => Index() >= 0 && Index() < playbackQueue.Snapshot.ManualUpcoming.Count - 1);
            Item("remove", "Убрать из очереди", "x", () => ChangeManualQueue(() => playbackQueue.RemoveManual(entryId()!.Value)), () => Index() >= 0);
        }
        PopupAppearance.Attach(root, menu);
        return menu;
    }

    private ItemsControl CreatePlaybackQueueList()
    {
        var result = new ItemsControl().ItemHeight(64).VariableHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(16, 0, 16, 0));
        result.ItemsSource = popupQueueSource.View;
        popupQueueAnimation = new QueueRowAnimation(result.InvalidateVisual);
        result.ItemTemplate = new DelegateTemplate<QueueBlock>(context =>
        {
            CompactTrackRow row = null!;
            row = CreateCompactTrackRow(_ => { if (row.EntryId is { } id) Run(() => PlayQueueEntryAsync(id)); }, PlayerSecondaryText, queueRow: true);
            AttachTrackQueueMenu(row.Root, () => row.Track, () => row.EntryId);
            var heading = new TextBlock().FontSize(12).SemiBold().Foreground(Muted).Margin(8, 16, 8, 8);
            var motion = new QueueRowMotion(popupQueueAnimation).Content(new Grid().Columns("*").Rows("Auto").Children(row.Root, heading));
            popupQueueBlocks[motion] = null;
            context.Register("motion", motion);
            context.Register("row", row.Root); context.Register("heading", heading);
            return motion;
        }, (_, block, _, context) =>
        {
            var row = compactTrackRows.Single(row => row.Root == context.Get<Grid>("row"));
            var motion = context.Get<QueueRowMotion>("motion"); popupQueueBlocks[motion] = block;
            if (motion.RetainedKey != block.Key) RetargetPopupQueueRow(motion, block);
            motion.RetainedKey = block.Key;
            row.Root.IsVisible = block.Kind == QueueBlockKind.Track;
            row.EntryId = block.EntryId; BindCompactTrackRow(row, block.Track);
            var heading = context.Get<TextBlock>("heading");
            heading.Text = block.Title; heading.IsVisible = block.Kind != QueueBlockKind.Track;
            heading.Margin = new Thickness(8, block.Key == "now" ? 8 : 12, 8, 6);
        }, (_, _, _, context) =>
        {
            var row = compactTrackRows.Single(row => row.Root == context.Get<Grid>("row"));
            popupQueueBlocks[context.Get<QueueRowMotion>("motion")] = null;
            row.EntryId = null; BindCompactTrackRow(row, null);
        });
        return result;
    }
}
