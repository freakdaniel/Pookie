using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;
using System.Text.RegularExpressions;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> hasAvatar = new(false);
    private readonly ObservableValue<string> profileInitial = new("");
    private CancellationTokenSource? avatarLoading;
    private long avatarGeneration;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, Task<PlayerPalette>> playerPalettes = new();
    private readonly Storage.ResourceCache<string, DateTimeOffset> unavailableArtwork = new(1024,
        new Storage.CacheBudget(512 * 1024), _ => 256);
    private static readonly TimeSpan ArtworkRetryDelay = TimeSpan.FromMinutes(2);
    private CoverGlassBackdrop? trackDetailGlass, collectionGlass, userHeaderGlass;
    private Storage.FaviconLoader? descriptionFavicons;

    private static CoverGlassBackdrop AttachCardGlass(TrackPageColumns card)
    {
        var backdrop = new CoverGlassBackdrop(card.BackgroundArtwork, card);
        VisualTree.Visit(card, element =>
        {
            if (element is MaterialTrackButton button) button.AttachGlassBackdrop(backdrop);
            if (element is ExpandedGlassSurface surface) surface.SetBackdrop(backdrop);
        });
        return backdrop;
    }

    private void RefreshCardGlass()
    {
        if (disposed || Window.WindowState == WindowState.Minimized) return;
        if (expandedHost.IsVisible) expandedGlassBackdrop.Prepare();
        if (!workspace.IsVisible) return;
        if (page.Value == Page.Home) homeMixCarousel.PrepareGlass();
        foreach (var row in likedRows.Values) row.PrepareTrackArtGlass(contentFrame.Bounds);
        if (page.Value == Page.Track) { trackDetailGlass?.RefreshArtwork(); trackDetailGlass?.Prepare(); }
        if (page.Value == Page.UserProfile) userHeaderGlass?.Prepare();
        if (page.Value == Page.LibraryCollection) { collectionGlass?.RefreshArtwork(); collectionGlass?.Prepare(); }
    }

    private void SetPlayerArtwork(ImageSource? source, long generation, bool pending = false)
    {
        artwork.Source = source != null ? source : Icons.Source(Icons.MissingTrackArtwork);
        expandedArtwork.Source = artwork.Source;
        expandedGlassBackdrop?.RefreshArtwork();
        if (source == null)
        {
            // Keep the previous hue until the new cover is available. Fading to
            // neutral here would reverse the colour transition when it arrives.
            if (pending) return;
            playerBackdrop.SetPalette(PlayerPalette.Neutral);
            expandedBackdrop.SetPalette(PlayerPalette.Neutral);
            return;
        }
        Run(async () =>
        {
            var palette = await playerPalettes.GetValue(source, image => Task.Run(() =>
            {
                try { return PlayerPalette.FromArtwork(image); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
                { return PlayerPalette.Neutral; }
            }, lifetime.Token));
            if (!disposed && generation == playGeneration)
            {
                playerBackdrop.SetPalette(palette);
                expandedBackdrop.SetPalette(palette);
            }
        });
    }

    private void ResetProfileAvatar()
    {
        ++avatarGeneration;
        avatarLoading?.Cancel(); avatarLoading?.Dispose(); avatarLoading = null;
        hasAvatar.Value = false;
        profileInitial.Value = "";
        profileAvatar.Source = null;
    }

    private void UpdateProfileAvatar(SoundCloudUser user)
    {
        ResetProfileAvatar();
        profileInitial.Value = System.Globalization.StringInfo.GetNextTextElement(user.Username.Trim().Length > 0 ? user.Username.Trim() : "?").ToUpperInvariant();
        avatarLoading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = avatarLoading.Token;
        var generation = avatarGeneration;
        Run(async () =>
        {
            var source = await FetchImageAsync(user.AvatarUrl, token);
            token.ThrowIfCancellationRequested();
            if (source == null || generation != avatarGeneration || disposed || me?.Id != user.Id) return;
            profileAvatar.Source = source;
            hasAvatar.Value = true;
        });
    }

    // Cache metadata, pruning and the first pixel decode must not run in a row's
    // binding/render pass. Only publishing the prepared source returns to the UI.
    private Task<ImageSource?> FetchImageAsync(string? url, CancellationToken token, int maximumSide = 1024,
        string? cachedAlternative = null)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<ImageSource?>(token);
        if (!TryArtworkUri(url, out var uri) ||
            unavailableArtwork.TryGetValue(uri.AbsoluteUri, out var retryAt) && retryAt > DateTimeOffset.UtcNow)
            return Task.FromResult<ImageSource?>(null);
        return Task.Run(async () =>
        {
            var image = await FetchImageCoreAsync(uri, token, maximumSide, cachedAlternative);
            if (image != null) unavailableArtwork.Remove(uri.AbsoluteUri);
            else unavailableArtwork[uri.AbsoluteUri] = DateTimeOffset.UtcNow + ArtworkRetryDelay;
            return image;
        }, token);
    }

    private static bool TryArtworkUri(string? url, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri) && SoundCloudWebClient.IsMediaUri(uri);

    private async Task<ImageSource?> FetchImageCoreAsync(Uri uri, CancellationToken token, int maximumSide, string? cachedAlternative)
    {
        if (await ReadCachedArtworkAsync(uri.AbsoluteUri, maximumSide, token) is { } cached) return cached;
        if (cachedAlternative != null && await ReadCachedArtworkAsync(cachedAlternative, maximumSide, token) is { } alternative)
            return alternative;
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 4 * 1024 * 1024) return null;
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                if (bytes.Length + count > 4 * 1024 * 1024) return null;
                bytes.Write(buffer, 0, count);
            }
            var payload = bytes.ToArray();
            var image = PrepareImage(payload,maximumSide);
            await imageDiskCache.WriteAsync(uri.AbsoluteUri, payload, token);
            return image;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is HttpRequestException or ArgumentException) { return null; }
    }

    private async Task<ImageSource?> ReadCachedArtworkAsync(string url, int maximumSide, CancellationToken token)
    {
        var cached = await imageDiskCache.ReadAsync(url, token);
        if (cached == null) return null;
        try { return PrepareImage(cached, maximumSide); }
        catch (ArgumentException error)
        {
            Pookie.Logging.AppLog.Failure("Pookie.Artwork", "Повреждённая обложка в кеше; загружаем заново", error, Serilog.Events.LogEventLevel.Debug);
            return null;
        }
    }

    private static ImageSource PrepareImage(byte[] payload,int maximumSide = 1024)
    {
        return Storage.ArtworkImage.Prepare(payload,maximumSide);
    }

    private async Task<ImageSource?> FetchLargeArtworkAsync(SoundCloudTrack track, CancellationToken token)
    {
        var largeUrl = ToLargeArtworkUrl(track.ArtworkUrl);
        if (largeUrl != null && !string.Equals(largeUrl.AbsoluteUri, track.ArtworkUrl, StringComparison.Ordinal))
        {
            var large = await FetchImageAsync(largeUrl.AbsoluteUri, token);
            if (large != null) return large;
        }
        return await FetchImageAsync(track.ArtworkUrl ?? track.User?.AvatarUrl, token);
    }

    private static Uri? ToLargeArtworkUrl(string? artworkUrl) => ToArtworkVariantUrl(artworkUrl, "-t500x500");

    private static Uri? ToArtworkVariantUrl(string? artworkUrl, string suffix)
    {
        if (!Uri.TryCreate(artworkUrl, UriKind.Absolute, out var uri) || !SoundCloudWebClient.IsMediaUri(uri)) return null;
        var path = uri.AbsolutePath;
        var variant = Regex.Match(path, @"-(?:large|t\d+x\d+)(?=\.[^./]+$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!variant.Success || string.Equals(variant.Value, suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var variantPath = string.Concat(path.AsSpan(0, variant.Index), suffix, path.AsSpan(variant.Index + variant.Length));
        return new UriBuilder(uri) { Path = variantPath }.Uri;
    }

    private async Task LoadArtworkAsync(SoundCloudTrack track, long generation)
    {
        // Share the same full-size source as cards instead of fetching the API's
        // thumbnail URL into a separate player cache.
        var source = await GetLibraryArtworkAsync(track);
        if (generation == playGeneration && !disposed)
        {
            if (source != null)
            {
                coverCache[track.Id] = source;
            }
            SetPlayerArtwork(source, generation);
        }
    }
}
