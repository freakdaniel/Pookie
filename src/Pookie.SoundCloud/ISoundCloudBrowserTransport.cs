using System.Text.Json;

namespace Pookie.SoundCloud;

public interface ISoundCloudBrowserTransport
{
    Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default);
    Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default);
}
