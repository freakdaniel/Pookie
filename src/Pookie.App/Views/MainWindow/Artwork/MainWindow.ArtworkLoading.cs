using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using System.Runtime.CompilerServices;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // A cover owns its loading layer. Registering it must not keep removed sidebar cards alive.
    private readonly ConditionalWeakTable<Image, LibrarySkeleton> artworkLoading = new();
    private static readonly Storage.CacheBudget ArtworkCacheBudget = new(64L * 1024 * 1024);
    private static long ArtworkBytes(ImageSource source) => (long)source.PixelWidth * source.PixelHeight * 4;
    private readonly Storage.AsyncResourceCache<string, ImageSource> artworkRequests = new(256, ArtworkCacheBudget, ArtworkBytes);
    private readonly Storage.AsyncResourceCache<(string Url, int Side), ImageSource> trackAvatarRequests = new(512, ArtworkCacheBudget, ArtworkBytes);

    private Task<ImageSource?> TrackAvatarAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult<ImageSource?>(null);
        // Comments and sidebar fans use at most 44 DIP. Keep these sources
        // independent of 500px cover artwork, including after a DPI change.
        var side = Math.Clamp((int)Math.Ceiling(44 * Window.DpiScale / 32) * 32, 64, 256);
        return trackAvatarRequests.GetAsync((url, side), () => FetchTrackAvatarAsync(url, side), lifetime.Token);
    }

    private async Task<ImageSource?> FetchTrackAvatarAsync(string url, int side)
    {
        await coverGate.WaitAsync(lifetime.Token);
        try
        {
            // The API's "large" avatar is 100px. Preserve sharpness on very
            // high-DPI displays while still bounding the decoded source.
            if (side > 100 && ToLargeArtworkUrl(url) is { } larger &&
                await FetchImageAsync(larger.AbsoluteUri, lifetime.Token, side) is { } image) return image;
            return await FetchImageAsync(url, lifetime.Token, side);
        }
        finally { coverGate.Release(); }
    }

    private Grid ArtworkLayer(Image cover)
    {
        cover.VectorCacheMode = ImageVectorCacheMode.Direct;
        var skeleton = new LibrarySkeleton { ArtworkOnly = true, IsVisible = false };
        artworkLoading.Add(cover, skeleton);
        return new Grid().Columns("*").Rows("*").Children(cover, skeleton);
    }

    private void SetCardArtwork(Image cover, ImageSource? source, string? url, string fallback = "music-notes", bool finished = false)
    {
        var pending = !finished && source == null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && SoundCloudWebClient.IsMediaUri(uri);
        cover.Source = source != null ? source : pending ? null : Icons.Source(fallback);
        if (artworkLoading.TryGetValue(cover, out var skeleton))
        {
            skeleton.IsVisible = pending;
            skeleton.SetActive(pending);
        }
    }

    private void StopCardArtwork(Image cover)
    {
        if (artworkLoading.TryGetValue(cover, out var skeleton)) { skeleton.SetActive(false); skeleton.IsVisible = false; }
    }

    private Task<ImageSource?> SharedArtworkAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult<ImageSource?>(null);
        return artworkRequests.GetAsync(url, () => FetchSharedArtworkAsync(url), lifetime.Token);
    }

    private async Task<ImageSource?> FetchSharedArtworkAsync(string url)
    {
        await coverGate.WaitAsync(lifetime.Token);
        try { return await FetchLargeArtworkAsync(new SoundCloudTrack { ArtworkUrl = url }, lifetime.Token); }
        finally { coverGate.Release(); }
    }

    private int FirstArtworkCount(bool preview = false, bool rows = false) => preview ? 6 :
        rows ? Math.Max(1, (int)Math.Ceiling(Math.Max(196, Window.ClientSize.Height - 240) / 196)) :
        libraryColumns * Math.Max(1, (int)Math.Ceiling(Math.Max(200, Window.ClientSize.Height - 240) / (likedArtworkSize + 90)));

    private async Task PrepareTrackArtworkAsync(IEnumerable<SoundCloudTrack> items, CancellationToken token, bool preview = false)
    {
        await Task.WhenAll(items.Take(FirstArtworkCount(preview, page.Value == Page.LibraryTracks && likesAsList.Value))
            .Select(GetLibraryArtworkAsync)).WaitAsync(token);
    }

    private async Task PrepareCollectionArtworkAsync(IEnumerable<LibraryItem> items, CancellationToken token, bool preview = false, bool rows = false)
    {
        await Task.WhenAll(items.Take(FirstArtworkCount(preview, rows)).Select(GetCollectionArtworkAsync)).WaitAsync(token);
    }

    private static string? CollectionArtworkUrl(LibraryItem item) =>
        item.ArtworkUrl ?? item.Track?.ArtworkUrl ?? item.Track?.User?.AvatarUrl ?? item.User?.AvatarUrl;

    private ImageSource? CachedCollectionArtwork(LibraryItem item)
    {
        if (item.Track is { } track && libraryCoverCache.TryGetValue(track.Id, out var cover)) return cover;
        return CollectionArtworkUrl(item) is { } url ? collectionImages.GetValueOrDefault(url) : null;
    }

    private void SetCollectionArtwork(Image cover, LibraryItem item, ImageSource? source = null, bool finished = false) =>
        SetCardArtwork(cover, source ?? CachedCollectionArtwork(item), CollectionArtworkUrl(item),
            item.User != null ? "user-circle" : "music-notes", finished);

    private void RefreshCachedCollectionArtwork()
    {
        // Retained cards may have been bound before another page loaded the cover.
        foreach (var card in collectionCards.Values)
            if (CachedCollectionArtwork(card.Item) is { } source)
                SetCollectionArtwork(card.Cover, card.Item, source, finished: true);
    }

    private async Task<ImageSource?> GetCollectionArtworkAsync(LibraryItem item)
    {
        if (CachedCollectionArtwork(item) is { } existing) return existing;
        var url = CollectionArtworkUrl(item);
        // Tracks share the likes cache even when history contains no artwork URL.
        // Metadata-only collections still cache by their resolved image URL.
        var source = item.Track is { } track
            ? await GetLibraryArtworkAsync(track with { ArtworkUrl = url })
            : await SharedArtworkAsync(url);
        if (source != null && !disposed && url != null)
        {
            collectionImages[url] = source;
        }
        return source;
    }
}
