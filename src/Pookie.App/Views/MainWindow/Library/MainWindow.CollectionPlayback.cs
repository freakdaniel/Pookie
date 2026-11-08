using System.Runtime.CompilerServices;
using Aprillz.MewUI.Controls;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Sidebar tiles are rebuilt independently of virtualized library cards.
    // Weak references let both share playback state without retaining removed tiles.
    private readonly ConditionalWeakTable<Button, CollectionCardView> collectionPlaybackViews = new();
    private long collectionPlaybackRequest;

    private CollectionCardView CollectionCard(Button root) =>
        collectionPlaybackViews.TryGetValue(root, out var card) ? card : throw new InvalidOperationException("Collection card is not registered.");

    private void RefreshCollectionCardsPlayback()
    {
        foreach (var (_, card) in collectionPlaybackViews) RefreshCollectionCardPlayback(card);
    }

    private void RefreshCollectionCardPlayback(CollectionCardView card, bool animate = true)
    {
        var selected = card.Item is { } item && (item.Playlist != null
            ? current != null && playbackQueue.Context?.Key == item.Key
            : item.Track != null && item.Track.Id == current?.Id);
        card.Playback.SetPlaying(selected, isPlaying.Value, animate);
    }

    private AnimatedContextMenu CreatePlaylistCardMenu(CollectionCardView card)
    {
        var menu = PopupAppearance.CreateMenu();
        Uri? Link() => TrackLink(card.Item?.Playlist?.PermalinkUrl);
        void Item(string id, string label, string icon, Action<LibraryItem> action, Func<bool>? enabled = null)
        {
            var command = PopupAppearance.MenuCommand("pookie.collection.card." + id, label, icon);
            card.Root.Commands.Register(command,
                () => { if (card.Item is { Playlist: not null } item) action(item); },
                () => card.Item?.Playlist != null && (enabled?.Invoke() ?? true));
            menu.Item(command);
        }
        Item("open", "Открыть страницу подборки", "books", item => Run(() => OpenLibraryItemAsync(item)));
        Item("play", "Воспроизвести подборку", "play", item => Run(() => PlayLibraryCollectionAsync(item)));
        Item("queue", "Добавить подборку в очередь", "queue", item => Run(() => EnqueueLibraryCollectionAsync(item)),
            () => !demo && api.Session != null);
        Item("share", "Поделиться", "arrow-square-out", OpenShare);
        Item("copy", "Скопировать ссылку", "copy", _ => { if (Link() is { } link) CopyText(link.AbsoluteUri); }, () => Link() != null);
        Item("soundcloud", "Открыть в SoundCloud", "globe", item => OpenSoundCloudPage(item.Playlist?.PermalinkUrl), () => Link() != null);
        return menu;
    }

    private async Task EnqueueLibraryCollectionAsync(LibraryItem item)
    {
        if (!CanUseWorkspace || api.Session is not { } session) return;
        var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, lifetime.Token);
        if (disposed || api.Session != session) return;
        collectionDetails.TryAdd(item.Key, detail);
        var page = collectionDetails[item.Key].Tracks;
        var tracks = new List<SoundCloudTrack>(page.Tracks);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        while (page.NextHref is { } cursor)
        {
            if (!cursors.Add(cursor)) throw new SoundCloudException("Не удалось загрузить все треки подборки.");
            page = await api.GetNextPageAsync(cursor, lifetime.Token);
            if (disposed || api.Session != session) return;
            tracks.AddRange(page.Tracks);
        }
        EnqueueCollectionTracks(tracks);
    }

    private void EnqueueCollectionTracks(IEnumerable<SoundCloudTrack> tracks)
    {
        var added = false;
        foreach (var track in tracks) { playbackQueue.Enqueue(track, false); added = true; }
        if (added) RefreshQueue();
        status.Value = added ? "Треки добавлены в очередь." : "В подборке пока нет треков.";
    }

    private async Task PlayLibraryCollectionAsync(LibraryItem item)
    {
        if (!CanUseWorkspace || item.Playlist == null) return;
        var request = ++collectionPlaybackRequest;
        if (playbackQueue.Context?.Key == item.Key && current != null) { await ToggleAsync(); return; }
        var session = api.Session; var sourceVersion = playbackQueue.SourceVersion; var generation = playGeneration;
        var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, lifetime.Token);
        if (disposed || request != collectionPlaybackRequest || api.Session != session ||
            sourceVersion != playbackQueue.SourceVersion || generation != playGeneration) return;
        collectionDetails[item.Key] = detail;
        var tracks = detail.Tracks.Tracks;
        if (tracks.Length == 0) { status.Value = "В этом плейлисте пока нет треков."; return; }
        StartQueueContext(new(item.Key, detail.Item.Title,
            item.IsAlbum ? "Сейчас играет альбом" : "Сейчас играет плейлист", detail.Tracks.NextHref,
            Kind: item.IsAlbum ? PlaybackContextKind.Album : PlaybackContextKind.Playlist,
            ResourceUrn: PlaybackHistoryContextUrn(detail.Item)), tracks, tracks[0], 0);
        await PlayAsync(tracks[0], queueAdvance: true);
    }
}
