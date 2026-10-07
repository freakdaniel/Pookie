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

    private void SetPlayerArtwork(ImageSource? source, long generation)
    {
        artwork.Source = source != null ? source : Icons.Source("music-notes");
        expandedArtwork.Source = artwork.Source;
        if (source == null)
        {
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

    private async Task<ImageSource?> FetchImageAsync(string? url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !SoundCloudWebClient.IsMediaUri(uri)) return null;
        var cached = await imageDiskCache.ReadAsync(uri.AbsoluteUri, token);
        if (cached != null)
            try { return ImageSource.FromBytes(cached); }
            catch (ArgumentException error)
            { Pookie.Logging.AppLog.Failure("Pookie.Artwork", "Повреждённая обложка в кеше; загружаем заново", error, Serilog.Events.LogEventLevel.Debug); }
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
            var image = ImageSource.FromBytes(payload);
            await imageDiskCache.WriteAsync(uri.AbsoluteUri, payload, token);
            return image;
        }
        catch (Exception error) when (error is HttpRequestException or ArgumentException) { return null; }
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

    private static Uri? ToLargeArtworkUrl(string? artworkUrl)
    {
        if (!Uri.TryCreate(artworkUrl, UriKind.Absolute, out var uri) || !SoundCloudWebClient.IsMediaUri(uri)) return null;
        var path = uri.AbsolutePath;
        var variant = Regex.Match(path, @"-(?:large|t\d+x\d+)(?=\.[^./]+$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!variant.Success || string.Equals(variant.Value, "-t500x500", StringComparison.OrdinalIgnoreCase)) return null;
        var largePath = string.Concat(path.AsSpan(0, variant.Index), "-t500x500", path.AsSpan(variant.Index + variant.Length));
        return new UriBuilder(uri) { Path = largePath }.Uri;
    }

    private async Task LoadArtworkAsync(SoundCloudTrack track, long generation)
    {
        var source = await FetchImageAsync(track.ArtworkUrl ?? track.User?.AvatarUrl, lifetime.Token);
        if (source != null && generation == playGeneration && !disposed)
        {
            if (coverCache.Count >= 256) coverCache.Remove(coverCache.Keys.First());
            coverCache[track.Id] = source;
            SetPlayerArtwork(source, generation);
        }
    }

    private async Task LoadRowArtworkAsync(Image image, SoundCloudTrack track)
    {
        if (demo) return;
        if (!coverCache.TryGetValue(track.Id, out var source))
        {
            await coverGate.WaitAsync(lifetime.Token);
            try
            {
                if (!coverCache.TryGetValue(track.Id, out source))
                {
                    source = await FetchImageAsync(track.ArtworkUrl ?? track.User?.AvatarUrl, lifetime.Token);
                    if (source == null) return;
                    if (coverCache.Count >= 256) coverCache.Remove(coverCache.Keys.First());
                    coverCache[track.Id] = source;
                }
            }
            finally { coverGate.Release(); }
        }
        if (!disposed && rowTracks.TryGetValue(image, out var id) && id == track.Id) image.Source = source;
    }
}
