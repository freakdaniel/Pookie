namespace Pookie.SoundCloud;

// A source owns its API transport. Regional API routing can be injected here later;
// the resulting CDN URL is always consumed directly by the player.
public interface ISoundCloudPlaybackSource
{
    Task<SoundCloudTrack> GetTrackAsync(long id, CancellationToken cancellationToken = default);
    Task<SoundCloudStream?> ResolveTranscodingAsync(SoundCloudTrack track, Transcoding transcoding, CancellationToken cancellationToken = default);
}

public sealed record ResolvedPlayback(SoundCloudTrack Track, SoundCloudStream Stream)
{
    public override string ToString() => $"Resolved SoundCloud track {Track.Id} ({Stream.Protocol}; credentials redacted)";
}

public sealed class SoundCloudStreamResolver(ISoundCloudPlaybackSource primary, params ISoundCloudPlaybackSource[] regionalSources)
{
    public async Task<ResolvedPlayback> ResolveAsync(long trackId, CancellationToken cancellationToken = default, string[]? protocols = null)
    {
        if (trackId <= 0) throw new ArgumentOutOfRangeException(nameof(trackId));
        protocols ??= ["progressive", "hls", "ctr-encrypted-hls", "cbc-encrypted-hls"];
        var blocked = false;
        var protectedAvailable = false;
        foreach (var source in new[] { primary }.Concat(regionalSources))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = await source.GetTrackAsync(trackId, cancellationToken);
            if (track.Id != trackId) throw new SoundCloudException("Резольвер вернул другой трек.");
            // Search/likes can contain old access flags and expired track_authorization.
            if (track.Access == "blocked" || track.Policy == "BLOCK") { blocked = true; continue; }
            if (track.Access == "preview" || track.Policy == "SNIP")
                throw new SoundCloudException("SoundCloud предоставил только отрывок. Полное воспроизведение требует доступа к треку.");
            var full = (track.Media?.Transcodings ?? []).Where(t => !t.Snipped &&
                !string.IsNullOrEmpty(t.Url) && !t.Url.Contains("/preview", StringComparison.OrdinalIgnoreCase) &&
                t.Format?.Protocol is "progressive" or "hls" or "ctr-encrypted-hls" or "cbc-encrypted-hls").ToArray();
            protectedAvailable |= full.Any(t => t.Format!.Protocol.Contains("encrypted", StringComparison.Ordinal));
            foreach (var transcoding in full.Where(t => protocols.Contains(t.Format!.Protocol, StringComparer.Ordinal))
                .OrderBy(t => Array.IndexOf(protocols, t.Format!.Protocol))
                .ThenBy(t => t.Format!.MimeType?.Contains("mpeg", StringComparison.OrdinalIgnoreCase) == true ? 0 : 1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var stream = await source.ResolveTranscodingAsync(track, transcoding, cancellationToken);
                    if (stream == null || !SoundCloudWebClient.IsMediaUri(stream.Uri)) continue;
                    if (stream.Protected && (stream.LicenseAuthToken is not { Length: > 0 and <= 16384 } ||
                        stream.LicenseAuthToken.Any(c => c < 32 || c == 127))) continue;
                    return new(track, stream);
                }
                catch (SoundCloudException error) when (!error.RequiresBrowserVerification && error.StatusCode is 403 or 404) { }
            }
        }
        throw new SoundCloudException(blocked ? "SoundCloud ограничил доступ к треку для подключения резольвера." : protectedAvailable
            ? "Для трека доступен только DRM-поток. Поддержка CDM ещё не подключена к аудиоплееру."
            : "SoundCloud не предоставил доступный полный аудиопоток.");
    }
}
