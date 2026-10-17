using System.Text.Json;

namespace Pookie.SoundCloud;

public interface ISoundCloudBrowserTransport
{
    Task<JsonDocument> RequestChatAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support chats.");
    Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default);
    Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default);
    Task RecordPlaybackAsync(long trackId, string? contextUrn = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support playback history.");
    Task SetRepostedAsync(long trackId, bool reposted, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support reposts.");
    Task SetPlaylistLikedAsync(long userId, long playlistId, bool liked, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support playlist likes.");
    Task SetPlaylistRepostedAsync(long playlistId, bool reposted, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support playlist reposts.");
    Task SetFollowingAsync(long userId, long artistId, bool following, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support following.");
    Task AddToPlaylistAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support playlist edits.");
    Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support track page reads.");
    Task<JsonDocument> WriteCommentAsync(CommentWriteRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support comments.");
}
