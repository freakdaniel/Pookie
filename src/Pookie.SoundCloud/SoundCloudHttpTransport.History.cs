using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudHttpTransport
{
    public async Task RecordPlaybackAsync(long trackId, string? contextUrn = null, CancellationToken cancellationToken = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = cancellation.Token;
        ValidateId(trackId);
        if (!BrowserRequestCommand.IsHistoryContextUrn(contextUrn)) throw new ArgumentException("Некорректный контекст прослушивания.");
        try { await RecordNativePlaybackAsync(trackId, contextUrn, token).ConfigureAwait(false); }
        catch (SoundCloudException error) when (error.RequiresBrowserVerification && error.ChallengeKind != SoundCloudChallengeKind.HardBlock && verification != null)
        {
            await recovery.WaitAsync(token).ConfigureAwait(false);
            try { await verification.RecordPlaybackAsync(trackId, contextUrn, token).ConfigureAwait(false); }
            finally { recovery.Release(); }
        }
    }

    private async Task RecordNativePlaybackAsync(long trackId, string? contextUrn, CancellationToken token)
    {
        var userId = await UserIdAsync(token).ConfigureAwait(false);
        using var metadata = await GetJsonAsync(Api($"/tracks/{trackId}"), token).ConfigureAwait(false);
        var track = metadata.RootElement;
        if (!track.TryGetProperty("id", out var id) || !id.TryGetInt64(out var actual) || actual != trackId ||
            !track.TryGetProperty("duration", out var duration) || !duration.TryGetDouble(out var length) || length <= 0 || !double.IsFinite(length))
            throw new SoundCloudException("SoundCloud не подтвердил прослушанный трек.");
        var owner = track.TryGetProperty("user_id", out var ownerValue) && ownerValue.TryGetInt64(out var ownerId) ? ownerId :
            track.TryGetProperty("user", out var user) && user.TryGetProperty("id", out ownerValue) && ownerValue.TryGetInt64(out ownerId) ? ownerId : 0;
        ValidateId(owner);
        var account = Account();
        var trackUrn = $"soundcloud:tracks:{trackId}";
        await ActionAsync(HttpMethod.Post, "/me/play-history", Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("track_urn", trackUrn);
            if (contextUrn != null) writer.WriteString("context_urn", contextUrn);
            writer.WriteEndObject();
        }), null, token).ConfigureAwait(false);
        // The web history endpoint alone acknowledges without persisting playback.
        // Report the authenticated play only after the UI's real listen threshold.
        using var result = await ExecuteAsync(HttpMethod.Post, Api("/me"), Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteStartArray("events"); writer.WriteStartObject();
            writer.WriteString("event", "audio"); writer.WriteString("version", "v1.27.47"); writer.WriteStartObject("payload");
            writer.WriteString("action", "play"); writer.WriteString("source", "pookie"); writer.WriteString("player_type", "native");
            writer.WriteString("track", trackUrn); writer.WriteString("track_owner", $"soundcloud:users:{owner}");
            if (contextUrn?.StartsWith("soundcloud:playlists:", StringComparison.Ordinal) == true) writer.WriteString("in_playlist", contextUrn);
            writer.WriteNumber("track_length", length);
            if (track.TryGetProperty("track_authorization", out var authorization) && authorization.ValueKind == JsonValueKind.String)
                writer.WriteString("track_authorization", authorization.GetString());
            writer.WriteString("user", $"soundcloud:users:{userId}"); writer.WriteNumber("playhead_position", 0);
            writer.WriteString("anonymous_id", playbackSession); writer.WriteString("session_id", playbackSession);
            writer.WriteString("url", "https://soundcloud.com/"); writer.WriteNumber("client_id", account.MobileIdentity == null ? 46941 : 3152);
            writer.WriteNumber("ts", clock.GetUtcNow().ToUnixTimeMilliseconds());
            if (account.AppVersion != null) writer.WriteString("app_version", account.AppVersion);
            writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndArray();
            writer.WriteString("sent_at", clock.GetUtcNow()); writer.WriteString("auth_token", account.AccessToken); writer.WriteEndObject();
        }), null, token, expectJson: false).ConfigureAwait(false);
    }
}
