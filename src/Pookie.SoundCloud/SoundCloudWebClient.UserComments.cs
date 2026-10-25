using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed record UserComment(SoundCloudComment Comment, SoundCloudTrack? Track);
public sealed record UserCommentPage(UserComment[] Comments, string? NextHref);

public sealed partial class SoundCloudWebClient
{
    public async Task<UserCommentPage> GetUserCommentsAsync(long userId, string? cursor = null, CancellationToken token = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var path = $"/users/{userId}/comments";
        var uri = Api(path.TrimStart('/') + "?limit=30&linked_partitioning=1");
        if (cursor != null && (!BrowserRequestCommand.IsReadUrl(cursor) || !Uri.TryCreate(cursor, UriKind.Absolute, out uri) || uri.AbsolutePath != path))
            throw new SoundCloudException("Неверная страница комментариев пользователя");
        using var document = await GetJsonAsync(uri!, token);
        var root = document.RootElement;
        var rows = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
        var comments = new List<UserComment>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || row.Deserialize(SoundCloudJson.Default.SoundCloudComment) is not { Id: > 0 } comment || string.IsNullOrWhiteSpace(comment.Body)) continue;
            var track = row.TryGetProperty("track", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? nested.Deserialize(SoundCloudJson.Default.SoundCloudTrack) : null;
            if (track == null && row.TryGetProperty("track_id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var value) && value > 0)
                track = new() { Id = value };
            comments.Add(new(comment, track));
        }
        var missing = comments.Where(entry => entry.Track is { Id: > 0 } && string.IsNullOrWhiteSpace(entry.Track.Title))
            .Select(entry => entry.Track!.Id).Distinct().ToArray();
        var tracks = new Dictionary<long, SoundCloudTrack>();
        foreach (var ids in missing.Chunk(50))
            try
            {
                using var metadata = await GetJsonAsync(Api("tracks?ids=" + string.Join(',', ids)), token);
                foreach (var track in ParsePage(metadata.RootElement).Tracks) tracks[track.Id] = track;
            }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or JsonException) { }
        var next = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_href", out var href) && href.ValueKind == JsonValueKind.String ? href.GetString() : null;
        return new(comments.DistinctBy(entry => entry.Comment.Id).Select(entry => entry.Track is { } track && tracks.TryGetValue(track.Id, out var full) ? entry with { Track = full } : entry).ToArray(), next);
    }
}
