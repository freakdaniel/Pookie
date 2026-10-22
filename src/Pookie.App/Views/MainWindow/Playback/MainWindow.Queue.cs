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
            if (context.Kind == PlaybackContextKind.Wave) return await GetWavePageAsync(token);
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

    private void StartQueueContext(PlaybackContext context, SoundCloudTrack[] data, SoundCloudTrack first, int? selectedIndex = null)
    {
        var index = selectedIndex is { } position && position >= 0 && position < data.Length && data[position].Id == first.Id ? position :
            Array.FindIndex(data, track => ReferenceEquals(track, first));
        if (index < 0) index = Array.FindIndex(data, track => track.Id == first.Id);
        if (index < 0) { data = [.. data, first]; index = data.Length - 1; }
        playbackQueue.StartContext(context, data, index);
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
        if (playbackQueue.Current?.EntryId == entryId) { await ToggleAsync(); return; }
        var entry = playbackQueue.Select(entryId);
        if (entry != null) await PlayAsync(entry.Track);
    }

    private void RefreshQueue(bool dissolveWave = false)
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
        var wave = state.Context?.Kind == PlaybackContextKind.Wave;
        if (state.ManualUpcoming.Count > 0) data.Add(new("manual", QueueBlockKind.Upcoming, Title: "Добавлено тобой"));
        data.AddRange(state.ManualUpcoming.Select(TrackBlock));
        if (wave || state.Forward.Count + state.ContextUpcoming.Count > 0)
            data.Add(new(wave ? "wave" : "next", QueueBlockKind.Context, Title: wave ? "Далее на твоей волне" : "Далее"));
        data.AddRange(state.Forward.Concat(state.ContextUpcoming).Select(TrackBlock));
        if (state.Recommendations.Count > 0)
        {
            data.Add(new("recommendations", QueueBlockKind.Context, Title: "Похожие дальше"));
            data.AddRange(state.Recommendations.Select(TrackBlock));
        }
        RefreshPopupQueue(data.ToArray(), wave, dissolveWave);
        queueStatus.Value = state.Context?.Title ?? "Очередь воспроизведения";
        RefreshExpandedQueue(dissolveWave); UpdateSystemMedia();
    }

    private void RefreshPopupQueue(QueueBlock[] data, bool wave, bool dissolveWave)
    {
        if (popupQueueData.SequenceEqual(data)) return;
        if (popupQueueData.Select(block => block.Key).SequenceEqual(data.Select(block => block.Key)))
        { popupQueueData = data; popupQueueSource.SetData(data); return; }
        popupQueuePositions.Clear();
        if (wave && queueOpen.Value)
            foreach (var (row, block) in popupQueueBlocks)
                if (block != null) popupQueuePositions[block.Key] = row.VisualY;
        popupQueueAnimation.Begin(dissolveWave ? 220 : 280);
        popupQueueEntering = data.Select(block => block.Key).Except(popupQueueData.Select(block => block.Key)).ToHashSet();
        if (dissolveWave)
            foreach (var entry in playbackQueue.Snapshot.ContextUpcoming)
            {
                var key = $"entry:{entry.EntryId}";
                if (Array.FindIndex(data, block => block.Key == key) != Array.FindIndex(popupQueueData, block => block.Key == key))
                { popupQueueEntering.Add(key); popupQueuePositions.Remove(key); }
            }
        popupQueueChangedAt = Environment.TickCount64;
        popupQueueData = data; popupQueueSource.SetData(data);
        foreach (var (row, block) in popupQueueBlocks)
            if (block != null) RetargetPopupQueueRow(row, block);
    }

    private void RetargetPopupQueueRow(QueueRowMotion row, QueueBlock block) =>
        row.Retarget(popupQueuePositions.TryGetValue(block.Key, out var y) ? y : null,
            queueOpen.Value && playbackQueue.Context?.Kind == PlaybackContextKind.Wave &&
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
            FinishWaveListening(reason: Pookie.Recommendations.ListeningEndReason.Previous);
            var previous = playbackQueue.Previous();
            if (previous != null) await PlayAsync(previous.Track);
            return;
        }
        FinishWaveListening(skipped: !naturalEnd, reason: naturalEnd ? Pookie.Recommendations.ListeningEndReason.NaturalEnd : Pookie.Recommendations.ListeningEndReason.Skip);
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
        if (!playbackQueue.Snapshot.Upcoming.Any() && !(naturalEnd && playbackQueue.Repeat == RepeatMode.Track))
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
        FinishWaveListening(reason: Pookie.Recommendations.ListeningEndReason.Stopped);
        ++playGeneration; playLoading?.Cancel(); CancelSeek(); player?.Stop();
        audioReady = audioPreparing = paused = false;
        isPlaying.Value = playbackLoading.Value = false;
        presence?.Clear(); RefreshLikedPlayback(); RefreshQueue();
    }

    private void CycleRepeat()
    {
        playbackQueue.SetRepeat((RepeatMode)(((int)playbackQueue.Repeat + 1) % 3));
        RefreshRepeatState(); RefreshQueue(); configurationTimer.Stop(); configurationTimer.Start();
        Run(() => queueLoader.EnsureAheadAsync());
    }
    private void RefreshRepeatState()
    {
        repeatActive.Value = playbackQueue.Repeat != RepeatMode.Off;
        repeatTrack.Value = playbackQueue.Repeat == RepeatMode.Track;
    }
    private FrameworkElement RepeatIcon(int size) => new Grid().Columns("*").Rows("*").Children(
        Icons.View("repeat", size).Center(),
        new TextBlock().Text("1").FontSize(8).Bold().Center().BindIsVisible(repeatTrack));

    private void EnqueueTrack(SoundCloudTrack track, bool next)
    {
        playbackQueue.Enqueue(track, next); RefreshQueue();
        Run(() => queueLoader.EnsureAheadAsync());
    }
    private void ChangeManualQueue(Action change) { change(); RefreshQueue(); }

    private void AttachTrackQueueMenu(FrameworkElement root, Func<SoundCloudTrack?> track, Func<long?>? entryId = null, bool extraActions = false)
    {
        var menu = new ContextMenu();
        void Item(string id, string label, Action execute, Func<bool> enabled)
        {
            var command = new Command("pookie.queue." + id, label);
            root.Commands.Register(command, execute, enabled);
            menu.Item(command);
        }
        Item("open", "Открыть страницу трека", () => { if (track() is { } item) Run(() => OpenTrackPageAsync(item)); }, () => track() != null);
        Item("next", "Воспроизвести следующим", () => { if (track() is { } item) EnqueueTrack(item, true); }, () => track() != null);
        Item("add", "Добавить в очередь", () => { if (track() is { } item) EnqueueTrack(item, false); }, () => track() != null);
        Item("playlist", "Добавить в плейлист", () => { if (track() is { } item) Run(() => OpenPlaylistPickerAsync(item)); }, () => track() != null && me != null && !demo);
        if (extraActions)
        {
            Item("copy", "Скопировать ссылку", () => { if (track() is { } item) CopyTrackLink(item); }, () => track() != null);
            Item("repost", "Сделать репост", () => { if (track() is { } item) Run(() => ToggleTrackRepostAsync(item)); },
                () => track() is { } item && CanRepostTrack(item));
        }
        if (entryId != null)
        {
            int Index() => playbackQueue.Snapshot.ManualUpcoming.ToList().FindIndex(entry => entry.EntryId == entryId());
            Item("up", "Выше в очереди", () => ChangeManualQueue(() => playbackQueue.MoveManual(entryId()!.Value, Index() - 1)), () => Index() > 0);
            Item("down", "Ниже в очереди", () => ChangeManualQueue(() => playbackQueue.MoveManual(entryId()!.Value, Index() + 1)),
                () => Index() >= 0 && Index() < playbackQueue.Snapshot.ManualUpcoming.Count - 1);
            Item("remove", "Убрать из очереди", () => ChangeManualQueue(() => playbackQueue.RemoveManual(entryId()!.Value)), () => Index() >= 0);
        }
        root.ContextMenu = menu;
    }

    private ItemsControl CreatePlaybackQueueList()
    {
        var result = new ItemsControl().ItemHeight(64).VariableHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0));
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
        }, (_, _, _, context) =>
        {
            var row = compactTrackRows.Single(row => row.Root == context.Get<Grid>("row"));
            popupQueueBlocks[context.Get<QueueRowMotion>("motion")] = null;
            row.EntryId = null; BindCompactTrackRow(row, null);
        });
        return result;
    }
}
