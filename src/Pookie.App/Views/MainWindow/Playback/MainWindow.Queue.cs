using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly PlaybackQueue playbackQueue = new();
    private PlaybackQueueLoader queueLoader = null!;
    private readonly PaginationItems popupQueueSource = new(LoadingRowStyle.Compact, item => ((PlaybackEntry)item).EntryId);
    private readonly ObservableValue<bool> repeatActive = new(false), repeatTrack = new(false);

    private void InitializePlaybackQueue()
    {
        playbackQueue.SetShuffle(shuffle.Value);
        queueLoader = new(playbackQueue, async (context, cursor, token) =>
        {
            if (context.LibraryCursor)
            {
                var page = await api.GetLibraryNextAsync(cursor, token);
                return new(page.Items.Where(item => item.Track != null).Select(item => item.Track!).ToArray(), page.NextHref);
            }
            return await api.GetNextPageAsync(cursor, token);
        }, RefreshQueue, lifetime.Token);
        shuffle.Changed += () => { playbackQueue.SetShuffle(shuffle.Value); RefreshQueue(); UpdateSystemMedia(); };
        RefreshRepeatState();
    }

    private void SetQueue(SoundCloudTrack first, int? selectedIndex = null)
    {
        CaptureQueueOrigin();
        var route = navigationHistory[navigationIndex].Route;
        var data = page.Value == Page.Track ? new[] { first }.Concat(trackDetailState?.Related.Tracks.Where(track => track.Id != first.Id) ?? []).ToArray() : page.Value == Page.Search ? [first] :
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
        StartQueueContext(new(key, queueOriginTitle, queueOriginKind, cursor, Kind: kind), data, first, selectedIndex);
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

    private void RefreshQueue()
    {
        if (queueList == null) return;
        var state = playbackQueue.Snapshot;
        var data = state.Current is { } selected ? new[] { selected }.Concat(state.Upcoming).ToArray() : state.Upcoming.ToArray();
        popupQueueSource.SetData(data);
        queueStatus.Value = state.Context?.Title ?? "Очередь воспроизведения";
        RefreshExpandedQueue(); UpdateSystemMedia();
    }

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
        var next = playbackQueue.Next(naturalEnd);
        if (next != null) await PlayAsync(next.Track);
        else EndPlayback();
    }

    private void EndPlayback()
    {
        ++playGeneration; playLoading?.Cancel(); CancelSeek(); player?.Stop();
        audioReady = audioPreparing = paused = false;
        isPlaying.Value = playbackLoading.Value = false;
        presence?.Clear(); RefreshLikedPlayback(); RefreshQueue();
    }

    private void CycleRepeat()
    {
        playbackQueue.SetRepeat((RepeatMode)(((int)playbackQueue.Repeat + 1) % 3));
        RefreshRepeatState(); RefreshQueue(); configurationTimer.Stop(); configurationTimer.Start();
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
    }
    private void ChangeManualQueue(Action change) { change(); RefreshQueue(); }

    private void AttachTrackQueueMenu(FrameworkElement root, Func<SoundCloudTrack?> track, Func<long?>? entryId = null)
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
        var result = new ItemsControl().ItemHeight(64).Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0));
        result.ItemsSource = popupQueueSource.View;
        result.ItemTemplate = new DelegateTemplate<PlaybackEntry>(context =>
        {
            CompactTrackRow row = null!;
            row = CreateCompactTrackRow(_ => { if (row.EntryId is { } id) Run(() => PlayQueueEntryAsync(id)); }, queueRow: true);
            AttachTrackQueueMenu(row.Root, () => row.Track, () => row.EntryId);
            context.Register("row", row.Root); return row.Root;
        }, (_, entry, _, context) =>
        {
            var row = compactTrackRows.Single(row => row.Root == context.Get<Grid>("row"));
            row.EntryId = entry.EntryId; BindCompactTrackRow(row, entry.Track);
        }, (_, _, _, context) =>
        {
            var row = compactTrackRows.Single(row => row.Root == context.Get<Grid>("row"));
            row.EntryId = null; BindCompactTrackRow(row, null);
        });
        return result;
    }
}
