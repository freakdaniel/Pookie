using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed record PlaylistCreateRequest(long UserId, long TrackId, string Title, string Sharing)
{
    public bool IsValid() => UserId is > 0 and <= 9007199254740991 && TrackId is > 0 and <= 9007199254740991 &&
        Title is { Length: > 0 and <= 255 } && !string.IsNullOrWhiteSpace(Title) && !Title.Any(char.IsControl) && Sharing is "public" or "private";
    public bool Confirms(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && number > 0 &&
        root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object && user.TryGetProperty("id", out var owner) && owner.ValueKind == JsonValueKind.Number && owner.TryGetInt64(out var ownerId) && ownerId == UserId &&
        root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String && title.GetString() == Title &&
        root.TryGetProperty("sharing", out var sharing) && sharing.ValueKind == JsonValueKind.String && sharing.GetString() == Sharing &&
        root.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array && tracks.EnumerateArray().Any(track =>
            track.ValueKind == JsonValueKind.Object && track.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var trackId) && trackId == TrackId);
    public override string ToString() => "Playlist creation (title redacted)";
}
