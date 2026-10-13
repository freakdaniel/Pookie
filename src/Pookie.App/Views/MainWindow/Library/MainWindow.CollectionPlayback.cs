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
