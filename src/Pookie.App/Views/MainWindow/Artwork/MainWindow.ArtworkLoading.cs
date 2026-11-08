using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using System.Runtime.CompilerServices;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // A cover owns its loading layer. Registering it must not keep removed sidebar cards alive.
    private readonly ConditionalWeakTable<Image, ArtworkPresentation> artworkLoading = new();
    private readonly ConditionalWeakTable<Image, TrackArtworkOverlay> artworkPlayback = new();
    private static readonly Storage.CacheBudget ArtworkCacheBudget = new(64L * 1024 * 1024);
    private static long ArtworkBytes(ImageSource source) => (long)source.PixelWidth * source.PixelHeight * 4;
    private readonly Storage.AsyncResourceCache<string, ImageSource> artworkRequests = new(256, ArtworkCacheBudget, ArtworkBytes);
    private readonly Storage.AsyncResourceCache<(string Url, int Side), ImageSource> trackAvatarRequests = new(512, ArtworkCacheBudget, ArtworkBytes);
    private readonly Storage.AsyncResourceCache<(string Url, int Side), ImageSource> compactArtworkRequests = new(512, ArtworkCacheBudget, ArtworkBytes);

    private const int CompactCoverSize = 44;

    private sealed class ArtworkPresentation(Grid root, LibrarySkeleton skeleton)
    {
        internal LibrarySkeleton Skeleton { get; } = skeleton;
        private Image? placeholder;
        private AnimationClock? fade;

        internal void Reveal(Image cover, string fallback)
        {
            StopReveal();
            var overlay = new Image { IsHitTestVisible = false, VectorCacheMode = ImageVectorCacheMode.Direct }
                .Source(Icons.Source(fallback)).StretchMode(cover.StretchMode);
            placeholder = overlay;
            root.Insert(1, overlay);
            fade = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1))
            {
                TickCallback = progress => overlay.Opacity = 1 - progress,
                CompletedCallback = StopReveal
            };
            fade.Start();
        }

        internal void StopReveal()
        {
            fade?.Stop();
            fade = null;
            if (placeholder is not { } overlay) return;
            placeholder = null;
            root.Remove(overlay);
            overlay.Dispose();
        }
    }

    private int CompactArtworkSide(double size) => Math.Clamp((int)Math.Ceiling(size * Window.DpiScale), 1, 256);

    private Task<ImageSource?> CompactArtworkAsync(string? url, int side, ImageSource? cached = null)
    {
        if (cached != null && Math.Max(cached.PixelWidth, cached.PixelHeight) <= side)
            return Task.FromResult<ImageSource?>(cached);
        Task<ImageSource?> Load() => cached == null ? FetchCompactArtworkAsync(url!, side) :
            Task.Run<ImageSource?>(() => Storage.ArtworkImage.Resize(cached, side), lifetime.Token);
        if (string.IsNullOrEmpty(url)) return cached == null ? Task.FromResult<ImageSource?>(null) : Load();
        return compactArtworkRequests.GetAsync((url, side), Load, lifetime.Token);
    }

    private async Task<ImageSource?> FetchCompactArtworkAsync(string url, int side)
    {
        await coverGate.WaitAsync(lifetime.Token);
        try
        {
            var variant = side <= 100 ? ToArtworkVariantUrl(url, "-large") : ToLargeArtworkUrl(url);
            // At normal DPI, reuse older on-disk full covers when a small
            // variant is missing from the cache, including offline.
            var cachedAlternative = side <= 100 ? (variant != null ? url : ToLargeArtworkUrl(url)?.AbsoluteUri) : null;
            if (await FetchImageAsync(variant?.AbsoluteUri ?? url, lifetime.Token, side, cachedAlternative) is { } thumbnail)
                return thumbnail;
            return variant == null ? null : await FetchImageAsync(url, lifetime.Token, side);
        }
        finally { coverGate.Release(); }
    }

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
        var root = new Grid().Columns("*").Rows("*").Children(cover, skeleton);
        artworkLoading.Add(cover, new(root, skeleton));
        return root;
    }

    private TrackArtworkOverlay CreateCoverPlayback(Image cover, Action play, double size = 46, int? glyphSize = null, Color? scrim = null)
    {
        var glass = new CoverGlassBackdrop(cover, centeredSampleSize: size + 48);
        var overlay = new TrackArtworkOverlay(glass, play, size, ownsGlass: true, glyphSize: glyphSize,
            scrim: scrim ?? Color.FromArgb(64, 0, 0, 0));
        artworkPlayback.Add(cover, overlay);
        return overlay;
    }

    private void SetCardArtwork(Image cover, ImageSource? source, string? url, string fallback = Icons.MissingTrackArtwork, bool finished = false)
    {
        var placeholder = Icons.Source(fallback);
        var key = TryArtworkUri(url, out var uri) ? uri.AbsoluteUri : null;
        var knownMissing = key != null && unavailableArtwork.ContainsKey(key);
        if (finished && source == null && key != null && !knownMissing)
            unavailableArtwork[key] = DateTimeOffset.UtcNow + ArtworkRetryDelay;
        var wasPlaceholder = ReferenceEquals(cover.Source, placeholder);
        var pending = !finished && source == null && key != null && !knownMissing && !wasPlaceholder;
        IImageSource? next = source != null ? source : pending ? null : placeholder;
        artworkLoading.TryGetValue(cover, out var presentation);
        if (!ReferenceEquals(cover.Source, next))
        {
            if (source != null && wasPlaceholder && cover.FindVisualRoot() is Window) presentation?.Reveal(cover, fallback);
            else presentation?.StopReveal();
            cover.Source = next;
            if (artworkPlayback.TryGetValue(cover, out var overlay)) overlay.ArtworkChanged();
        }
        if (presentation?.Skeleton is { } skeleton)
        {
            skeleton.IsVisible = pending;
            skeleton.SetActive(pending);
        }
    }

    private void StopCardArtwork(Image cover)
    {
        if (!artworkLoading.TryGetValue(cover, out var presentation)) return;
        presentation.StopReveal();
        presentation.Skeleton.SetActive(false);
        presentation.Skeleton.IsVisible = false;
    }

    private void ClearCardArtwork(Image cover)
    {
        StopCardArtwork(cover);
        cover.Source = null;
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

    private int FirstArtworkCount(bool preview = false, bool rows = false, double stride = 196) => preview ? 6 :
        rows ? Math.Max(1, (int)Math.Ceiling(Math.Max(196, Window.ClientSize.Height - 240) / stride)) :
        libraryColumns * Math.Max(1, (int)Math.Ceiling(Math.Max(200, Window.ClientSize.Height - 240) / (likedArtworkSize + 90)));

    private async Task PrepareTrackArtworkAsync(IEnumerable<SoundCloudTrack> items, CancellationToken token,
        bool preview = false, TrackViewMode mode = TrackViewMode.Cards)
    {
        var first = items.Take(FirstArtworkCount(preview, mode != TrackViewMode.Cards, mode == TrackViewMode.List ? 64 : 196));
        var side = CompactArtworkSide(CompactCoverSize);
        var requests = mode == TrackViewMode.List
            ? first.Select(track => CompactArtworkAsync(track.ArtworkUrl ?? track.User?.AvatarUrl, side, libraryCoverCache.GetValueOrDefault(track.Id)))
            : first.Select(GetLibraryArtworkAsync);
        await Task.WhenAll(requests).WaitAsync(token);
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
            item.User != null ? "user-circle" : item.Track != null ? Icons.MissingTrackArtwork : "music-notes", finished);

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
