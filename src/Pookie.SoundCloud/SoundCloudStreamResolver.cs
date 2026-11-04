namespace Pookie.SoundCloud;

// A source owns its API transport. A regional source fetches its own metadata;
// the resulting CDN URL is always consumed directly by the player.
public interface ISoundCloudPlaybackSource
{
    Task<SoundCloudTrack> GetTrackAsync(long id, CancellationToken cancellationToken = default);
    Task<SoundCloudStream?> ResolveTranscodingAsync(SoundCloudTrack track, Transcoding transcoding, CancellationToken cancellationToken = default);
}

public sealed record ResolvedPlayback(SoundCloudTrack Track, SoundCloudStream Stream)
{
    public bool UsedRegionalSource { get; init; }
    public override string ToString() => $"Resolved SoundCloud track {Track.Id} ({Stream.Protocol}; credentials redacted)";
}

public interface ISoundCloudPlaybackResolver
{
    Task<ResolvedPlayback> ResolveAsync(long trackId, CancellationToken cancellationToken = default, string[]? protocols = null);
}

public sealed class SoundCloudStreamResolver(ISoundCloudPlaybackSource primary, params ISoundCloudPlaybackSource[] regionalSources) : ISoundCloudPlaybackResolver
{
    public ISoundCloudPlaybackResolver? RegionalResolver { get; init; }
    public async Task<ResolvedPlayback> ResolveAsync(long trackId, CancellationToken cancellationToken = default, string[]? protocols = null)
    {
        if (trackId <= 0) throw new ArgumentOutOfRangeException(nameof(trackId));
        protocols ??= ["progressive", "hls", "ctr-encrypted-hls", "cbc-encrypted-hls"];
        var blocked = false;
        var protectedAvailable = false;
        var preview = false;
        var regionalFailed = false;
        for (var sourceIndex = -1; sourceIndex < regionalSources.Length; sourceIndex++)
        {
            var source = sourceIndex < 0 ? primary : regionalSources[sourceIndex];
            var regional = sourceIndex >= 0;
            try
            {
                var mobile = source is SoundCloudWebClient { Session.MobileIdentity: not null };
                cancellationToken.ThrowIfCancellationRequested();
                SoundCloudTrack track;
                try { track = await source.GetTrackAsync(trackId, cancellationToken); }
                catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 404 or 410)
                { throw new SoundCloudException(error.Message, error.StatusCode, trackUnavailable: true); }
                if (track.Id != trackId) throw new SoundCloudException("Резольвер вернул другой трек.");
                if (track.Access == "blocked" || track.Policy == "BLOCK") { blocked = true; continue; }
                if (track.Access == "preview" || track.Policy == "SNIP")
                { preview = true; continue; }
                var full = (track.Media?.Transcodings ?? []).Where(t => !t.Snipped &&
                    !string.IsNullOrEmpty(t.Url) && !t.Url.Contains("/preview", StringComparison.OrdinalIgnoreCase) &&
                    t.Format?.Protocol is "progressive" or "hls" or "ctr-encrypted-hls" or "cbc-encrypted-hls").ToArray();
                protectedAvailable |= full.Any(t => t.Format!.Protocol.Contains("encrypted", StringComparison.Ordinal));
                foreach (var transcoding in full.Where(t => protocols.Contains(t.Format!.Protocol, StringComparer.Ordinal))
                    .OrderBy(t => Array.IndexOf(protocols, t.Format!.Protocol))
                    .ThenBy(t => mobile && t.Format!.Protocol != "progressive" ? 0 : t.Format!.MimeType?.Contains("mpeg", StringComparison.OrdinalIgnoreCase) == true ? 0 : 1)
                    .ThenBy(t => !mobile ? 0 : t.Quality == "hq" ? 0 : t.Quality == "sq" ? 1 : 2)
                    .ThenBy(t => mobile && t.Preset == "aac_256k" ? 0 : 1))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var stream = await source.ResolveTranscodingAsync(track, transcoding, cancellationToken);
                        if (!IsPlayable(stream, protocols)) continue;
                        cancellationToken.ThrowIfCancellationRequested();
                        return new(track, stream!) { UsedRegionalSource = regional };
                    }
                    catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 403 or 404) { }
                }
            }
            catch (SoundCloudException) when (regional) { regionalFailed = true; }
            catch (HttpRequestException) when (regional) { regionalFailed = true; }
            catch (OperationCanceledException) when (regional && !cancellationToken.IsCancellationRequested) { regionalFailed = true; }
        }
        if (RegionalResolver != null && (blocked || preview))
        {
            try
            {
                var result = await RegionalResolver.ResolveAsync(trackId, cancellationToken, protocols).ConfigureAwait(false);
                if (result.Track.Id != trackId || result.Track.Access is "blocked" or "preview" || result.Track.Policy is "BLOCK" or "SNIP" ||
                    !IsPlayable(result.Stream, protocols)) throw new SoundCloudException("Региональный источник не подтвердил полный поток.");
                cancellationToken.ThrowIfCancellationRequested();
                return result with { UsedRegionalSource = true };
            }
            catch (SoundCloudException) { regionalFailed = true; }
            catch (HttpRequestException) { regionalFailed = true; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { regionalFailed = true; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new SoundCloudException(regionalFailed ? "Не удалось получить полный поток: региональное соединение SoundCloud временно недоступно. Повтори попытку позже."
            : preview ? "SoundCloud предоставил только отрывок во всех доступных подключениях."
            : blocked ? "SoundCloud ограничил доступ к треку для доступных подключений."
            : protectedAvailable ? "SoundCloud не предоставил доступную ссылку и авторизацию защищённого потока."
            : "SoundCloud не предоставил доступный полный аудиопоток.", trackUnavailable: (blocked || preview) && !regionalFailed);
    }

    private static bool IsPlayable(SoundCloudStream? stream, string[] protocols) => stream != null && SoundCloudWebClient.IsPlaybackUri(stream.Uri) &&
        protocols.Contains(stream.Protocol, StringComparer.Ordinal) && (!stream.Protected || stream.LicenseAuthToken is { Length: > 0 and <= 16384 } license &&
            !license.Any(c => c < 32 || c == 127));
}
