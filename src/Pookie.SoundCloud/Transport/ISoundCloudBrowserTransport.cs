using System.Text.Json;

namespace Pookie.SoundCloud;

[Flags]
public enum SoundCloudTransportCapabilities
{
    None = 0, Read = 1, TrackLikes = 2, TrackDetails = 4, Comments = 8,
    Chats = 16, Playlists = 32, Following = 64, History = 128, FollowSigning = 256,
    SystemPlaylistPrivacy = 512,
    All = SystemPlaylistPrivacy | Read | TrackLikes | TrackDetails | Comments | Chats | Playlists | Following | History | FollowSigning
}

public interface ISoundCloudTransport
{
    // Legacy/fake adapters retain their optional default methods. Production
    // adapters explicitly advertise supported capabilities so callers can
    // negotiate features before presenting an action, rather than catching an
    // unexpected NotSupportedException after a user invokes it.
    SoundCloudTransportCapabilities Capabilities => SoundCloudTransportCapabilities.Read | SoundCloudTransportCapabilities.TrackLikes;
    Task SetSystemPlaylistPublicAsync(string urn, bool isPublic, CancellationToken token = default) =>
        throw new NotSupportedException("This transport does not support system playlist privacy.");
    Task<JsonDocument> EditPlaylistAsync(PlaylistEditRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support playlist editing.");
    Task<string> GetFollowSignatureAsync(long userId, long artistId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support follow signing.");
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
    Task<JsonDocument> CreatePlaylistAsync(PlaylistCreateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support playlist creation.");
    Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support track page reads.");
    Task<JsonDocument> WriteCommentAsync(CommentWriteRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This transport does not support comments.");
}

public static class SoundCloudTransportFeatures
{
    public static bool Supports(this ISoundCloudTransport transport, SoundCloudTransportCapabilities capabilities) =>
        (transport.Capabilities & capabilities) == capabilities;

    public static void Require(this ISoundCloudTransport transport, SoundCloudTransportCapabilities capabilities)
    {
        if (!transport.Supports(capabilities)) throw new SoundCloudException("Подключённый транспорт SoundCloud не поддерживает это действие.");
    }
}

// Browser adapters remain available for session verification and compatibility.
public interface ISoundCloudBrowserTransport : ISoundCloudTransport;

// Preparation must only read account/session state, never perform the rejected
// mutation. Native HTTP gets the first opportunity to resume after preparation.
public interface ISoundCloudSessionVerifier
{
    Task PrepareSessionAsync(CancellationToken cancellationToken = default);
}
