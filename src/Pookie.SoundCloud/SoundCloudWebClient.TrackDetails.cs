using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<TrackCommentPage> GetTrackCommentsAsync(long trackId, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        using var document = await GetJsonAsync(Api($"tracks/{trackId}/comments?limit=30&linked_partitioning=1&threaded=0&filter_replies=1"), token);
        return ParseComments(document.RootElement);
    }

    public async Task<TrackCommentPage> GetTrackCommentsNextAsync(long trackId, string cursor, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        if (!BrowserRequestCommand.IsReadUrl(cursor) || !Uri.TryCreate(cursor, UriKind.Absolute, out var uri) ||
            uri.AbsolutePath != $"/tracks/{trackId}/comments")
            throw new SoundCloudException("Неверный адрес страницы комментариев.");
        using var document = await GetJsonAsync(uri, token);
        return ParseComments(document.RootElement);
    }

    public async Task<TrackPage> GetRelatedTracksAsync(long trackId, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        using var document = await GetJsonAsync(Api($"tracks/{trackId}/related?limit=6&linked_partitioning=1"), token);
        return ParsePage(document.RootElement);
    }

    private static void ValidateDetailTrackId(long id)
    {
        if (id is <= 0 or > 9007199254740991) throw new ArgumentOutOfRangeException(nameof(id));
    }

    private static TrackCommentPage ParseComments(JsonElement root)
    {
        var collection = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
        var comments = new List<SoundCloudComment>();
        foreach (var item in collection.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var comment = item.Deserialize(SoundCloudJson.Default.SoundCloudComment);
            if (comment is { Id: > 0, Body.Length: > 0 }) comments.Add(comment);
        }
        var next = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_href", out var href) &&
            href.ValueKind == JsonValueKind.String ? href.GetString() : null;
        return new(comments.ToArray(), next);
    }
}
